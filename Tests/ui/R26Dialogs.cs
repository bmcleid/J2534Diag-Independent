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

internal static class R26Dialogs
{
    const string ExpectedExe = "e5ba2abf8ee1a2eedc04f088b08d90d44c6ad78b4dbb506e8d825afc95bc3eba";
    const string ExpectedSource = "f0a43d1da720f0fb6261934939bdb1c571c44ef4";
    static string runtime, output; static int checks, currentPid; static Form main, fuel;
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
        UI(() =>
        {
            object vehicle = Get(main, "vehicleTabForm");
            Check(!((Button)Get(vehicle, "btnConnect")).Enabled && !((Button)Get(vehicle, "_disconnectButton")).Enabled,
                "No-provider connection buttons remain disconnected");
            Check(((CheckBox)Get(vehicle, "_captureOnlyCheck")).Checked && ((ComboBox)Get(vehicle, "_receiverSelect")).SelectedIndex == 0,
                "Capture-only/original receiver defaults preserved");
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
    static object Report { get { return Get(fuel, "_sessionReport"); } }
    static long NewCount() { return UI(() => (long)Report.GetType().GetProperty("ObservationsSinceExport").GetValue(Report)); }
    static string Gauge(byte pid)
    {
        return UI(() => { object controls = Get(fuel, "_pidControls"); object control = controls.GetType().GetProperty("Item").GetValue(controls, new object[] { pid }); return (string)control.GetType().GetProperty("ValueText").GetValue(control); });
    }
    static void StartWindow()
    {
        uiError = null; main = null; fuel = null; currentPid = Process.GetCurrentProcess().Id;
        ui = new Thread(() =>
        {
            try
            {
                main = (Form)Activator.CreateInstance(app.GetType("J2534Diag.MainForm", true));
                main.StartPosition = FormStartPosition.Manual; main.Bounds = new Rectangle(8, 8, 990, 710);
                main.Shown += (sender, args) => { manager = Get(main, "j2534Manager"); fuel = (Form)Get(main, "fuelTrimForm"); };
                Application.Run(main);
            }
            catch (Exception ex) { uiError = ex; }
        });
        ui.IsBackground = true; ui.SetApartmentState(ApartmentState.STA); ui.Start();
        Check(Wait(() => main != null && main.IsHandleCreated && fuel != null, 10000), "Exact R26 MainForm started with production OnLoad");
        Check(UI(() => main.Text.Contains("R26 / f0a43d1da720")), "R26 visible build identity matches preserved EXE");
        AssertProvidersClosed("startup");
        UI(() => { var tabs = (TabControl)Get(main, "tabControl1"); tabs.SelectedTab = tabs.TabPages["tabFuelTrim"]; });
        Inject(true);
    }
    static void Inject(bool begin)
    {
        UI(() =>
        {
            if (begin)
            {
                object gate = manager.GetType().GetProperty("ReceiveGate").GetValue(manager);
                Check((bool)Call(gate, "TryAcquire", "FUEL_TRIM"), "Synthetic-only receive lease acquired without channel");
                Set(fuel, "_ownsReceiveChannel", true); Set(fuel, "_cts", new CancellationTokenSource());
                Set(fuel, "_displayGeneration", (int)Get(fuel, "_displayGeneration") + 1);
                Call(Report, "Begin", "OFFLINE UI AUTOMATION / synthetic observations", "TESTVIN0000000001", DateTime.UtcNow);
            }
            int generation = (int)Get(fuel, "_displayGeneration");
            foreach (byte pid in new byte[] { 0x0C, 6, 7, 8, 9, 0x11 })
            {
                byte[] frame = pid == 0x0C ? new byte[] { 0, 0, 7, 0xE8, 4, 0x41, pid, 0x0F, 0xA0 } : new byte[] { 0, 0, 7, 0xE8, 3, 0x41, pid, pid == 0x11 ? (byte)51 : (byte)128 };
                Call(fuel, "QueueScalarSample", frame, 0u, generation);
            }
            Call(fuel, "FlushScalarSamples");
        });
        Check(Wait(() => Gauge(6) == "0" && NewCount() > 0, 2000), "Synthetic scalar callbacks reached actual gauges/report");
    }
    static AutomationElement ClosePrompt()
    {
        UI(() => { main.BeginInvoke(new Action(() => main.Close())); });
        var prompt = WaitWindow("Session report"); Foreground(prompt);
        Check(Wait(() => Name(AutomationElement.FocusedElement).Equals("Yes", StringComparison.OrdinalIgnoreCase), 2000), "Close prompt defaults to Yes");
        return prompt;
    }
    static void MainAlive()
    {
        Check(Wait(() => Window("Session report") == null && Window("Save As") == null && Window("Export") == null && main != null && !main.IsDisposed, 5000), "Application remains open after cancelled/failed close-save");
    }
    static AutomationElement SaveDialog() { return WaitWindow("Save As"); }
    static void FillAndSave(AutomationElement dialog, string path)
    {
        Dump("save-dialog-tree");
        var edits = dialog.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)).Cast<AutomationElement>().ToArray();
        var edit = edits.FirstOrDefault(e => e.Current.AutomationId == "1001") ?? edits.FirstOrDefault(e => e.Current.Name.StartsWith("File name", StringComparison.OrdinalIgnoreCase));
        if (edit == null) throw new Exception("File name edit not found; see save-dialog-tree.txt");
        Foreground(dialog); edit.SetFocus(); ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
        SendKeys.SendWait("{ENTER}");
    }
    static void ExportButton()
    {
        IntPtr handle = UI(() => ((Button)Get(fuel, "_exportButton")).Handle);
        Invoke(AutomationElement.FromHandle(handle));
    }
    static void CheckCsv(string file)
    {
        var rows = new List<string[]>();
        using (var parser = new TextFieldParser(file)) { parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true; while (!parser.EndOfData) rows.Add(parser.ReadFields()); }
        Check(rows.All(r => r.Length == 16), "Actual dialog-saved CSV has sixteen columns");
        Check(rows.Any(r => r[2] == "Observation") && rows.Count(r => r[2] == "PidSummary") == 6, "Actual save includes observations and six PID summaries");
        Check(rows.Skip(1).All(r => r[3].Contains(ExpectedSource) && r[4] == ExpectedExe), "Dialog export provenance matches exact R26 runtime");
        notes.Add(Path.GetFileName(file) + " SHA256=" + Hash(file) + ";dataRows=" + (rows.Count - 1));
    }
    static void Closed()
    {
        Check(ui.Join(5000) && uiError == null, "Actual close completed without exception");
    }
    static void Standalone()
    {
        using (var process = Process.Start(new ProcessStartInfo(Path.Combine(runtime, "J2534Diag.exe")) { WorkingDirectory = runtime }))
        {
            currentPid = process.Id;
            Check(Wait(() => Windows().Any(w => w.Current.Name.Contains("J2534Diag R26 / f0a43d1da720")), 10000), "Preserved standalone R26 executable launched");
            Screenshot("01-standalone-r26"); Modules(process, "standalone");
            var window = Windows().First(w => w.Current.Name.Contains("J2534Diag R26"));
            PostMessage(new IntPtr(window.Current.NativeWindowHandle), 0x0010, IntPtr.Zero, IntPtr.Zero);
            Check(process.WaitForExit(5000) && process.ExitCode == 0, "Clean standalone no-data close exits zero without report prompt");
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 2 || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return 2;
        runtime = Path.GetFullPath(args[0]); output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Check(Hash(Path.Combine(runtime, "J2534Diag.exe")) == ExpectedExe, "Preserved R26 EXE SHA256 verified before launch");
            Standalone();
            app = Assembly.LoadFrom(Path.Combine(runtime, "J2534Diag.exe"));
            Check((string)app.GetType("J2534Diag.BuildIdentity").GetField("SourceCommit").GetRawConstantValue() == ExpectedSource, "Loaded R26 source stamp exact");
            StartWindow();
            IntPtr checkbox = UI(() => { object controls = Get(fuel, "_pidControls"); object control = controls.GetType().GetProperty("Item").GetValue(controls, new object[] { (byte)6 }); return ((CheckBox)Get(control, "chkEnable")).Handle; });
            ((TogglePattern)AutomationElement.FromHandle(checkbox).GetCurrentPattern(TogglePattern.Pattern)).Toggle();
            Check(Wait(() => Gauge(6) == "Disabled", 2000), "Actual checkbox shows Disabled");
            ((TogglePattern)AutomationElement.FromHandle(checkbox).GetCurrentPattern(TogglePattern.Pattern)).Toggle(); Inject(false);
            Check(Gauge(6) == "0", "Re-enabled gauge resumes synthetic values");
            UI(() =>
            {
                var counter = app.GetType("J2534Diag.FuelTrimEvidenceCounter");
                Call(Report, "Account", Enum.Parse(counter, "CoalescedBeforeAcceptance"), 123456789);
                Call(Report, "Account", Enum.Parse(counter, "QueueCapacityDiscarded"), 12345678);
                main.Size = new Size(720, 700);
            });
            Check(Wait(() => UI(() => ((Label)Get(fuel, "_sessionHealth")).Text.Contains("123456789")), 3000), "Large synthetic counters displayed");
            Check(UI(() => { var label = (Label)Get(fuel, "_sessionHealth"); return label.Right <= label.Parent.ClientSize.Width && label.Bottom <= label.Parent.ClientSize.Height; }), "Long health text stays within narrow viewport");
            Screenshot("02-long-counters-and-gauges");
            var prompt = ClosePrompt(); Screenshot("03-actual-close-prompt"); Keys(prompt, "{ESC}"); MainAlive();
            Check(NewCount() > 0, "Escape preserves unexported observations");
            prompt = ClosePrompt(); Keys(prompt, "{ENTER}"); var save = SaveDialog(); Screenshot("04-actual-save-dialog"); Keys(save, "{ESC}"); MainAlive();
            Check(NewCount() > 0, "Cancelled native save does not acknowledge observations");
            ExportButton(); save = SaveDialog(); string first = Path.Combine(output, "actual-export.csv"); FillAndSave(save, first);
            Check(Wait(() => File.Exists(first) && NewCount() == 0 && Window("Save As") == null, 10000), "Actual Export button/native save acknowledges successful snapshot"); CheckCsv(first);
            AssertProvidersClosed("after-export");
            UI(() => { main.BeginInvoke(new Action(() => main.Close())); }); Closed();
            Check(Window("Session report") == null, "Unchanged exported observations do not prompt again");

            StartWindow(); ExportButton(); save = SaveDialog(); string second = Path.Combine(output, "before-new-observations.csv"); FillAndSave(save, second);
            Check(Wait(() => File.Exists(second) && NewCount() == 0, 10000), "Second exact-runtime snapshot saved");
            Inject(false); prompt = ClosePrompt(); Screenshot("05-prompt-after-new-observations");
            Keys(prompt, "{TAB}"); Check(Wait(() => Name(AutomationElement.FocusedElement).Equals("No", StringComparison.OrdinalIgnoreCase), 2000), "Keyboard Tab reaches No after default Yes"); Keys(prompt, "{ENTER}"); Closed(); CheckCsv(second);
            Check(Directory.GetFiles(output, "*.csv").Length == 2, "No closes without fabricating another export");

            StartWindow(); string locked = Path.Combine(output, "locked-target.csv"); File.WriteAllText(locked, "ORIGINAL LOCKED TARGET");
            using (var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                prompt = ClosePrompt(); Keys(prompt, "{ENTER}"); save = SaveDialog(); FillAndSave(save, locked);
                // Native overwrite confirmation can appear before the application attempts atomic replacement.
                Check(Wait(() => Window("Confirm Save As") != null || Window("Export") != null, 10000), "Native overwrite or application save error appeared");
                var overwrite = Window("Confirm Save As"); if (overwrite != null) Invoke(Button(overwrite, "Yes"));
                var error = WaitWindow("Export"); Screenshot("06-actual-save-error"); Dump("save-error-tree"); Invoke(Button(error, "OK")); MainAlive();
                Check(NewCount() > 0 && File.ReadAllText(locked) == "ORIGINAL LOCKED TARGET", "Actual save failure retains memory and original file");
            }
            prompt = ClosePrompt(); Keys(prompt, "{ENTER}"); save = SaveDialog(); FillAndSave(save, locked);
            Check(Wait(() => Window("Confirm Save As") != null, 10000), "Successful retry requests overwrite consent"); Invoke(Button(Window("Confirm Save As"), "Yes")); Closed(); CheckCsv(locked);
            Check(!Directory.GetFiles(output, "*.tmp").Any(), "No temporary export leftovers after failure/retry");
            Check(Hash(Path.Combine(runtime, "J2534Diag.exe")) == ExpectedExe, "R26 EXE unchanged after all UI checks");
            File.WriteAllLines(Path.Combine(output, "verification.txt"), new[] { "RuntimeSHA256=" + ExpectedExe, "Source=" + ExpectedSource, "ProductRebuild=False", "PhysicalAdapter=False", "RegisteredProviders=0", "Checks=" + checks }.Concat(notes));
            Console.WriteLine("PASS: " + checks + " exact-R26 standalone/dialog/keyboard/export checks; no product rebuild/provider/vehicle.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex); try { Screenshot("FAILURE"); Dump("failure-tree"); } catch { }
            return 1;
        }
        finally { File.WriteAllLines(Path.Combine(output, "partial-verification.txt"), notes.Concat(new[] { "ChecksReached=" + checks })); }
    }
}
