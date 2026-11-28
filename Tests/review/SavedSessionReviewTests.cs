using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;
using J2534Diag;

internal static class SavedSessionReviewTests
{
    static string ExpectedExe;
    static string ExpectedSource;
    static string runtime, output; static int checks, currentPid; static Form main, fuel;
    static readonly List<FileSystemWatcher> fileWatchers = new List<FileSystemWatcher>();
    static readonly object fileWatchLock = new object();
    static object manager; static Thread ui; static Exception uiError; static Assembly app;
    static readonly List<string> notes = new List<string>();
    delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder title, int maximum);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    static string Hash(string file) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); Console.WriteLine("CHECK " + checks + ": " + message); }
    static object Get(object target, string name) { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args); }
    static T UI<T>(Func<T> work) { return (T)main.Invoke(work); }
    static void UI(Action work) { main.Invoke(work); }
    static bool Wait(Func<bool> predicate, int timeout)
    {
        var clock = Stopwatch.StartNew();
        do { if (uiError != null) throw new Exception("UI worker failed", uiError); try { if (predicate()) return true; } catch (ElementNotAvailableException) { } Thread.Sleep(50); } while (clock.ElapsedMilliseconds < timeout);
        return false;
    }
    static AutomationElement[] Windows()
    {
        return AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, currentPid)).Cast<AutomationElement>().ToArray();
    }
    static AutomationElement Window(string title)
    {
        // Owned Win32 dialogs are nested beneath their owner in UIA's tree,
        // not necessarily desktop children. Enumerate their native windows.
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, parameter) =>
        {
            uint pid; GetWindowThreadProcessId(window, out pid);
            if (pid != currentPid) return true;
            var caption = new StringBuilder(512); GetWindowText(window, caption, caption.Capacity);
            if (caption.ToString() != title) return true;
            found = window; return false;
        }, IntPtr.Zero);
        if (found != IntPtr.Zero) return AutomationElement.FromHandle(found);
        // The common Save dialog can have an accessible owned-window entry
        // even when the native caption/PID lookup does not identify it.
        // Both failed-run UI trees retained these owned dialog descendants.
        foreach (var owner in Windows())
        {
            var owned = owner.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                new PropertyCondition(AutomationElement.NameProperty, title)));
            if (owned != null) return owned;
        }
        return null;
    }
    static AutomationElement WaitWindow(string title)
    {
        AutomationElement found = null;
        if (!Wait(() => (found = Window(title)) != null, 10000)) throw new Exception("Window not found: " + title + "; open=" + string.Join(" | ", Windows().Select(w => w.Current.Name)));
        return found;
    }
    static string Name(AutomationElement element) { return element.Current.Name.Replace("&", "").Trim(); }
    static AutomationElement Button(AutomationElement window, string name)
    {
        var buttons = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement button in buttons) if (Name(button).Equals(name, StringComparison.OrdinalIgnoreCase)) return button;
        throw new Exception("Button not found: " + name + " in " + window.Current.Name);
    }
    static void Invoke(AutomationElement element)
    {
        // A button may synchronously open a modal dialog; never block the driver thread.
        var worker = new Thread(() => { try { ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); } catch (ElementNotAvailableException) { } catch (Exception ex) { Console.WriteLine("ACTION: " + ex); } });
        worker.IsBackground = true; worker.SetApartmentState(ApartmentState.STA); worker.Start();
    }
    static void Foreground(AutomationElement window)
    {
        int handle = window.Current.NativeWindowHandle;
        if (handle != 0) SetForegroundWindow(new IntPtr(handle));
        else window.SetFocus();
        Thread.Sleep(120);
    }
    static void Keys(AutomationElement window, string keys) { Foreground(window); SendKeys.SendWait(keys); Thread.Sleep(150); }
    static void Screenshot(string name)
    {
        var bounds = Screen.PrimaryScreen.Bounds;
        using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        { graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size); bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png); }
    }
    static void Dump(string name)
    {
        var lines = new List<string>();
        foreach (var window in Windows())
        {
            lines.Add("WINDOW " + window.Current.Name + " / " + window.Current.ClassName);
            foreach (AutomationElement e in window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                try { lines.Add(e.Current.ControlType.ProgrammaticName + " | " + e.Current.AutomationId + " | " + e.Current.Name + " | " + e.Current.BoundingRectangle); } catch (ElementNotAvailableException) { }
        }
        File.WriteAllLines(Path.Combine(output, name + ".txt"), lines);
    }
    static void AssertProvidersClosed(string name)
    {
        UI(() =>
        {
            Check(((IList)manager.GetType().GetProperty("Devices").GetValue(manager)).Count == 0, "No registered J2534 providers in actual manager");
            foreach (string field in new[] { "Api", "Device", "Channel" }) Check(manager.GetType().GetProperty(field).GetValue(manager) == null, "No managed " + field + " opened");
            Check(!(bool)manager.GetType().GetProperty("NativeModeSelected").GetValue(manager) && manager.GetType().GetProperty("NativeSession").GetValue(manager) == null, "No native provider/session selected");
        });
        Modules(Process.GetCurrentProcess(), name);
    }
    static void Modules(Process process, string name)
    {
        process.Refresh(); var lines = new List<string>();
        foreach (ProcessModule module in process.Modules)
        {
            lines.Add(module.FileName);
            string lower = Path.GetFileName(module.FileName).ToLowerInvariant();
            Check(!new[] { "topdon", "rlink", "artidiag", "passthru", "drewtech", "openport" }.Any(lower.Contains), "No named vendor provider module: " + lower);
        }
        File.WriteAllLines(Path.Combine(output, name + "-modules.txt"), lines);
    }

    static object GetReview(string field) { return Get(fuel, field); }
    static void StartWindow()
    {
        uiError = null; main = null; fuel = null; currentPid = Process.GetCurrentProcess().Id;
        ui = new Thread(() =>
        {
            try
            {
                main = new MainForm();
                main.StartPosition = FormStartPosition.Manual;
                main.Bounds = new Rectangle(8, 8, 1000, 720);
                main.Shown += (sender, args) => {
                    manager = Get(main, "j2534Manager");
                    fuel = (Form)Get(main, "_savedSessionReview");
                    var tabs = (TabControl)Get(main, "tabControl1");
                    tabs.SelectedTab = tabs.TabPages["tabSavedSession"];
                };
                Application.Run(main);
            }
            catch (Exception ex) { uiError = ex; }
        });
        ui.IsBackground = true; ui.SetApartmentState(ApartmentState.STA); ui.Start();
        Check(Wait(() => main != null && main.IsHandleCreated && fuel != null, 10000), "R29 production main window and Saved session tab started");
        Check(UI(() => main.Text.Contains("R29")), "R29 build identity visible");
        AssertProvidersClosed("startup");
    }
    static string FileHash(string path) { return Hash(path); }
    static void ExpectReject(string path, string label)
    {
        bool rejected = false;
        try { SavedSessionReport.Read(path); }
        catch (FormatException) { rejected = true; }
        Check(rejected, label);
    }
    static string Fixture(string root)
    {
        string path = Path.Combine(root, "roundtrip.csv");
        var report = new FuelTrimSessionReport(1000);
        DateTime utc = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        report.Begin("offline fixture", "", utc);
        Check(report.Observe("0x7E8", 6, 0, 100, utc, 100), "Fixture accepts measured zero");
        report.Event("WorkerError", "  quoted,\"unit\"\r\nsecond line  ", utc);
        report.Event("Disconnect", "Failed: synthetic reader", utc);
        report.Account(FuelTrimEvidenceCounter.QueueCapacityDiscarded, 123);
        report.Export(path, "R29 test fixture", new string('a', 64), "Failed: synthetic reader");
        return path;
    }
    static void ParserChecks(string root, string inputs)
    {
        string file = Fixture(root); string before = Hash(file);
        var report = SavedSessionReport.Read(file);
        Check(report.SourceSha256 == before && report.SourcePath == Path.GetFullPath(file), "File hash/path identify the parsed byte snapshot");
        Check(report.ObservationRows == 1 && report.DisconnectRows == 1, "Observation and disconnect counts preserved");
        Check(report.Rows.Single(r => r.Cell(2) == "Observation").Cell(11) == "0", "Measured zero remains zero");
        Check(report.Rows.Any(r => r.Cell(2) == "State" && r.Cell(12) == "Unknown" && r.Cell(11) == ""), "Unknown empty values stay empty");
        Check(report.Rows.Single(r => r.Cell(2) == "WorkerError").Cell(15) == "  quoted,\"unit\"\r\nsecond line  ", "Quoted comma, escaped quote, whitespace and multiline error text preserved");
        Check(report.ExportMetadata.Contains("QueueCapacityDiscarded=123"), "Recorded counters retained without decoding");
        Check(report.Rows.Select(r => r.SourceRecord).SequenceEqual(Enumerable.Range(1, report.Rows.Count)), "Original data-record order retained");
        var copy = report.Rows[0].Values; copy[2] = "changed";
        Check(report.Rows[0].Cell(2) == "ExportMetadata", "Returned cell copy cannot mutate saved document");
        Check(Hash(file) == before, "Review does not write source file");
        string text = File.ReadAllText(file);
        string invalid = Path.Combine(root, "invalid.csv");
        File.WriteAllText(invalid, "Index,ArbId,Data\r\n1,7E8,00\r\n"); ExpectReject(invalid, "Raw CAN format rejected as a scalar report");
        File.WriteAllText(invalid, text.Replace("Schema=2;", "Schema=999;")); ExpectReject(invalid, "Unknown schema rejected");
        File.WriteAllText(invalid, text.Substring(0, text.LastIndexOf("\n", text.Length - 3) + 1)); ExpectReject(invalid, "Truncated missing summary rejected");
        File.WriteAllText(invalid, text.Replace("RetainedRows=", "BadRetainedRows=")); ExpectReject(invalid, "Missing retained-row count rejected");
        File.WriteAllText(invalid, text.Replace("Utc,SessionId", "SessionId,Utc")); ExpectReject(invalid, "Reordered scalar header rejected explicitly");
        File.WriteAllText(invalid, text + "\"unterminated"); ExpectReject(invalid, "Malformed quoting rejected");
        File.WriteAllBytes(invalid, new byte[] { 0xff, 0xfe, 0xff }); ExpectReject(invalid, "Invalid UTF-8 rejected");
        File.WriteAllText(invalid, ""); ExpectReject(invalid, "Empty input rejected");
        using (var big = new FileStream(invalid, FileMode.Create, FileAccess.Write)) big.SetLength(SavedSessionReport.MaximumBytes + 1L);
        ExpectReject(invalid, "Oversized file rejected before allocation");
        // Limit case uses genuine exporter rows, not an invented alternative schema.
        var large = new FuelTrimSessionReport(1000, 26000);
        for (int i = 0; i < 25001; i++) large.Event("Test", "row", DateTime.UtcNow);
        large.Export(invalid, "fixture", "", "Not attempted");
        ExpectReject(invalid, "Excess data records rejected without partial display");
        File.WriteAllText(invalid, "not a report"); // UI invalid-load fixture
        string noBom = Path.Combine(root, "no-bom.csv");
        File.WriteAllText(noBom, text, new UTF8Encoding(false));
        Check(SavedSessionReport.Read(noBom).Rows.Count == report.Rows.Count, "BOM-free UTF-8 accepted");
        var expected = new Dictionary<string, string> {
            { "r28-refusal-manual-export.csv", "fc6707de639f6f72c16074e531f8c15a12d66e22598cc57d856a4703b49d2a3e" },
            { "r28-refusal-recovered.csv", "f8261129ccdf51fecf6edeb439487f401e1a5df830f1f0b3b4ac2bef015fd9d5" },
            { "r28-reader-recovered.csv", "8fa7f92d913ad45c9d6d47cd5ed742578d376116647a7646de26e350612dbaa8" }
        };
        foreach (var item in expected)
        {
            string path = Path.Combine(inputs, item.Key);
            Check(Hash(path) == item.Value, "Preserved R28 fixture exact hash: " + item.Key);
            var saved = SavedSessionReport.Read(path);
            Check(saved.Rows.Count == (item.Key == "r28-refusal-recovered.csv" ? 18 : 16), "Preserved report row count: " + item.Key);
            Check(saved.ObservationRows == 0 && saved.DisconnectRows == (item.Key == "r28-refusal-recovered.csv" ? 4 : 2), "Failure/recovery event counts: " + item.Key);
            Check(Hash(path) == item.Value, "Preserved fixture unchanged after review: " + item.Key);
        }
        string normal = Path.Combine(inputs, "managed-normal-session.csv");
        string normalHash = Hash(normal); var normalDoc = SavedSessionReport.Read(normal);
        Check(normalHash == "5d129d81a964f2b43754a8430583164c5207bf1edd7193c77504eb162db5a16b" && normalDoc.ObservationRows == 9 && normalDoc.Rows.Count == 110, "Preserved R28 normal observations reopen exactly");
        Check(Hash(normal) == normalHash, "Normal observation source unchanged");
        string continuity = Path.Combine(inputs, "report-extended-continuity.csv");
        Check(SavedSessionReport.Read(continuity).Rows.Count == 20007, "Existing 20,007-record continuity report opens without truncation");
        Console.WriteLine("PARSER_CHECKS_COMPLETE");
    }
    static void OpenButton()
    {
        IntPtr handle = UI(() => ((Button)GetReview("_openButton")).Handle);
        Invoke(AutomationElement.FromHandle(handle));
    }
    static void FillAndOpen(AutomationElement dialog, string path)
    {
        var edits = dialog.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)).Cast<AutomationElement>().ToArray();
        var edit = edits.FirstOrDefault(e => e.Current.AutomationId == "1148") ??
            edits.FirstOrDefault(e => e.Current.AutomationId == "1001") ??
            edits.FirstOrDefault(e => e.Current.Name.StartsWith("File name", StringComparison.OrdinalIgnoreCase));
        if (edit == null) throw new Exception("Open-dialog filename edit missing");
        Foreground(dialog); edit.SetFocus();
        SendKeys.SendWait("^a"); SendKeys.SendWait(path); SendKeys.SendWait("{TAB}"); Thread.Sleep(150);
        Check(((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == path, "Native file-picker path readback matches source");
        Invoke(Button(dialog, "Open"));
    }
    static void GuiChecks(string root, string inputs)
    {
        string path = Path.Combine(inputs, "r28-refusal-recovered.csv"); string before = Hash(path);
        StartWindow();
        OpenButton(); var picker = WaitWindow("Open J2534Diag saved session report"); FillAndOpen(picker, path);
        Check(Wait(() => UI(() => ((Label)GetReview("_status")).Text.StartsWith("18 of 18")), 10000), "Actual Open dialog loads all preserved R28 records");
        Check(UI(() => ((TextBox)GetReview("_source")).Text.Contains(before)), "Viewer displays exact source SHA-256");
        Check(UI(() => ((DataGridView)GetReview("_grid")).ReadOnly && !((DataGridView)GetReview("_grid")).AllowUserToAddRows), "Saved grid is read-only");
        UI(() => ((TextBox)GetReview("_filter")).Text = "R28_REFUSAL_FIXTURE");
        Check(UI(() => ((DataGridView)GetReview("_grid")).Rows.Count == 1 &&
            Convert.ToInt32(((DataGridView)GetReview("_grid")).Rows[0].Cells[0].Value) == 10), "Failure filter retains exact original source-record 10");
        Check(UI(() => ((TextBox)GetReview("_details")).Text.Contains("Failed: InvalidOperationException:")), "Full selected failure details visible");
        Screenshot("saved-report-failure-filter");
        UI(() => ((TextBox)GetReview("_filter")).Text = "");
        Check(UI(() => ((DataGridView)GetReview("_grid")).Rows.Count == 18), "Clear filter restores original complete order");
        UI(() => Call(fuel, "LoadReport", Path.Combine(inputs, "managed-normal-session.csv")));
        Check(UI(() => ((Label)GetReview("_status")).Text.StartsWith("110 of 110")), "Normal observations shown after separate report load");
        UI(() => ((ComboBox)GetReview("_events")).SelectedItem = "Observation");
        Check(UI(() => ((DataGridView)GetReview("_grid")).Rows.Count == 9), "Exact event selector shows nine saved observations without matching metadata text");
        UI(() => Call(fuel, "LoadReport", path));
        bool rejected = UI(() => { try { Call(fuel, "LoadReport", Path.Combine(root, "invalid.csv")); return false; }
            catch (TargetInvocationException ex) { return ex.InnerException is FormatException; } });
        Check(rejected && UI(() => ((Label)GetReview("_status")).Text.StartsWith("18 of 18")), "Failed import retains prior displayed report");
        OpenButton(); picker = WaitWindow("Open J2534Diag saved session report"); Invoke(Button(picker, "Cancel"));
        Check(Wait(() => Window("Open J2534Diag saved session report") == null, 5000) &&
            UI(() => ((Label)GetReview("_status")).Text.StartsWith("18 of 18")), "Native Open cancellation retains prior report");
        Check(UI(() => {
            object live = Get(Get(main, "fuelTrimForm"), "_sessionReport");
            return !(bool)live.GetType().GetProperty("HasUnexportedChanges").GetValue(live);
        }), "Saved review leaves live report clean");
        AssertProvidersClosed("after-review");
        Screenshot("saved-report-complete");
        Dump("saved-report-tree");
        UI(() => main.BeginInvoke(new Action(() => main.Close())));
        Check(ui.Join(5000) && uiError == null && Window("Session report") == null, "Offline review closes without live unsaved-data prompt");
        StartWindow();
        Check(UI(() => GetReview("_document") == null), "Restart opens a clean separate review page");
        UI(() => Call(fuel, "LoadReport", path));
        Check(UI(() => ((Label)GetReview("_status")).Text.StartsWith("18 of 18")), "Preserved report reopens after restart");
        AssertProvidersClosed("after-restart");
        UI(() => main.BeginInvoke(new Action(() => main.Close())));
        Check(ui.Join(5000) && uiError == null, "Restarted offline review closes cleanly");
        Check(Hash(path) == before, "GUI/restart/filter paths leave original CSV unchanged");
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 3 || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return 2;
        runtime = Path.GetFullPath(args[0]); output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        ExpectedSource = Environment.GetEnvironmentVariable("GITHUB_SHA");
        ExpectedExe = Hash(Path.Combine(runtime, "J2534Diag.exe"));
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            app = typeof(MainForm).Assembly;
            Check(Hash(app.Location) == ExpectedExe && BuildIdentity.SourceCommit == ExpectedSource, "Actual GUI assembly matches exact packaged executable/source");
            string root = Path.Combine(output, "generated"); Directory.CreateDirectory(root);
            ParserChecks(root, Path.GetFullPath(args[2]));
            GuiChecks(root, Path.GetFullPath(args[2]));
            Check(Hash(Path.Combine(runtime, "J2534Diag.exe")) == ExpectedExe, "R29 executable unchanged after acceptance");
            File.WriteAllLines(Path.Combine(output, "verification.txt"), new[] {
                "Source=" + ExpectedSource, "ExecutableSHA256=" + ExpectedExe, "Checks=" + checks,
                "RegisteredProviders=0", "ProviderHandles=0", "Vehicle=False", "InputsUnchanged=True", "Acceptance=PASS" });
            Console.WriteLine("PASS: " + checks + " R29 saved-session parser/production-GUI checkpoints; no provider/vehicle.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex); try { Screenshot("FAILURE"); Dump("failure-tree"); } catch { }
            return 1;
        }
    }
}
