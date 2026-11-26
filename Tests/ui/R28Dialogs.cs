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

internal static class R28Dialogs
{
    const string ExpectedExe = "632714d1350ddae4b2c64756231d37babfc3ddec3dfce88506f6d1083faf9160";
    const string ExpectedSource = "03d8a2a611953ff912f79a2e0d3af056b3194a1f";
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
    static object Report { get { return Get(fuel, "_sessionReport"); } }
    static bool Dirty() { return UI(() => (bool)Report.GetType().GetProperty("HasUnexportedChanges").GetValue(Report)); }
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
        Check(Wait(() => main != null && main.IsHandleCreated && fuel != null, 10000), "Exact R28 MainForm started with production OnLoad");
        Check(UI(() => main.Text.Contains("R28 / 03d8a2a61195")), "R28 visible build identity matches preserved EXE");
        AssertProvidersClosed("startup");
        UI(() => { var tabs = (TabControl)Get(main, "tabControl1"); tabs.SelectedTab = tabs.TabPages["tabFuelTrim"]; });
        Check(!Dirty() && NewCount() == 0, "Untouched production report is clean with zero observations");
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
        Foreground(dialog); edit.SetFocus();
        var value = (ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern);
        // UIA's displayed Value can differ from the common dialog's committed
        // filename. Drive real text input, commit the edit by leaving focus,
        // and still require the accessible value to match.
        SendKeys.SendWait("^a");
        SendKeys.SendWait(path);
        SendKeys.SendWait("{TAB}");
        Thread.Sleep(150);
        Check(Wait(() => value.Current.Value == path, 2000), "Native filename edit reads back the requested absolute path");
        File.AppendAllText(Path.Combine(output, "save-targets.txt"), "Requested=" + path + "; Edit=" + value.Current.Value + Environment.NewLine);
        Screenshot("save-target-" + checks);
        // Activate the dialog's Save command explicitly after the filename
        // readback, rather than relying on Enter while the edit owns focus.
        Invoke(Button(dialog, "Save"));
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
        Check(!rows.Any(r => r[2] == "Observation") && rows.Count(r => r[2] == "PidSummary") == 6, "Actual save has zero fabricated observations and six PID summaries");
        Check(rows.Skip(1).All(r => r[3].Contains(ExpectedSource) && r[4] == ExpectedExe), "Dialog export provenance matches exact R28 runtime");
        notes.Add(Path.GetFileName(file) + " SHA256=" + Hash(file) + ";dataRows=" + (rows.Count - 1));
    }
    static void Closed()
    {
        Check(ui.Join(5000) && uiError == null, "Actual close completed without exception");
    }

    static void WatchExports()
    {
        foreach (var folder in new[] { Path.GetDirectoryName(output), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) }.Distinct())
        {
            if (!Directory.Exists(folder)) continue;
            var watcher = new FileSystemWatcher(folder, "*.csv") { IncludeSubdirectories = true };
            FileSystemEventHandler record = (sender, e) => {
                lock (fileWatchLock) File.AppendAllText(Path.Combine(output, "csv-file-events.txt"), e.ChangeType + " | " + e.FullPath + Environment.NewLine);
            };
            watcher.Created += record; watcher.Changed += record;
            watcher.Renamed += (sender, e) => record(sender, e);
            watcher.EnableRaisingEvents = true; fileWatchers.Add(watcher);
        }
    }
    static void SaveState(string expected)
    {
        File.WriteAllLines(Path.Combine(output, "save-state.txt"), new[] {
            "Expected=" + expected, "Exists=" + File.Exists(expected),
            "NewObservations=" + NewCount(), "SaveDialogPresent=" + (Window("Save As") != null),
            "CurrentDirectory=" + Environment.CurrentDirectory
        }.Concat(Directory.GetFiles(output).Select(p => "DiagnosticFile=" + p)));
    }

    static bool Retained() { return (bool)manager.GetType().GetProperty("HasConnectionOrSelection").GetValue(manager); }
    static string Outcome() { return (string)manager.GetType().GetProperty("LastDisconnectOutcome").GetValue(manager); }
    static void RetainConnection()
    {
        UI(() => {
            var owner = app.GetType("J2534Diag.J2534ConnectionOwnership", true);
            Check((bool)owner.GetMethod("TryAcquire").Invoke(null, new object[] { manager, "OFFLINE R28 native-dialog logical fixture" }), "Valid logical lease acquired without provider handles");
            Set(manager, "_managedLeaseHeld", true);
            Call(Get(main, "vehicleTabForm"), "RefreshConnectionControls");
        });
        Check(Retained() && !Dirty(), "Clean retained logical connection requires close prompt");
        AssertProvidersClosed("logical-retained");
    }
    static void BeginReport() { UI(() => Call(Report, "Begin", "OFFLINE R28 close-dialog fixture", "", DateTime.UtcNow)); }
    static void AssertClosedHandles(string name)
    {
        foreach (string field in new[] { "Api", "Device", "Channel" })
            Check(manager.GetType().GetProperty(field).GetValue(manager) == null, "Closed fixture has no " + field);
        Check(manager.GetType().GetProperty("NativeSession").GetValue(manager) == null && Get(manager, "_nativeApi") == null,
            "Closed fixture has no native session/API");
        Modules(Process.GetCurrentProcess(), name);
    }
    static void CheckFinalCsv(string file)
    {
        var rows = new List<string[]>();
        using (var parser = new TextFieldParser(file)) { parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true; while (!parser.EndOfData) rows.Add(parser.ReadFields()); }
        Check(rows.Where(r => r[2] == "Disconnect").Select(r => r[15]).SequenceEqual(new[] { "Pending", "Succeeded" }),
            "Final native-dialog CSV contains one exact disconnect pair, no second disconnect on retry");
        Check(rows.Any(r => r[2] == "ExportMetadata" && r[15].Contains("DisconnectOutcome=Succeeded")), "Final native-dialog CSV records successful disconnect outcome");
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 2 || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return 2;
        runtime = Path.GetFullPath(args[0]); output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Check(Hash(Path.Combine(runtime, "J2534Diag.exe")) == ExpectedExe, "Exact delivered R28 EXE verified");
            WatchExports();
            app = Assembly.LoadFrom(Path.Combine(runtime, "J2534Diag.exe"));
            Check((string)app.GetType("J2534Diag.BuildIdentity").GetField("SourceCommit").GetRawConstantValue() == ExpectedSource, "R28 compiled source exact");
            StartWindow();
            UI(() => { main.BeginInvoke(new Action(() => main.Close())); }); Closed();
            Check(Window("Session report") == null, "Untouched disconnected app closes without prompt");

            StartWindow(); RetainConnection();
            var prompt = ClosePrompt(); Screenshot("01-retained-close-prompt"); Dump("retained-prompt-tree");
            Invoke(Button(prompt, "Cancel")); MainAlive();
            Check(Retained() && Outcome() == "Not attempted", "Actual Cancel retained logical connection with no disconnect");
            prompt = ClosePrompt(); Invoke(Button(prompt, "Yes")); var save = SaveDialog();
            Check(Retained() && Outcome() == "Not attempted", "Native destination dialog appears before any disconnect");
            Screenshot("02-destination-before-disconnect"); Invoke(Button(save, "Cancel")); MainAlive();
            Check(Retained() && Outcome() == "Not attempted", "Native destination cancellation leaves connection alone");
            BeginReport();
            prompt = ClosePrompt(); Invoke(Button(prompt, "Yes")); save = SaveDialog();
            Check(Retained(), "Successful save destination selection still precedes disconnect");
            string success = Path.Combine(output, "r28-close-success.csv"); FillAndSave(save, success); Closed();
            CheckCsv(success); CheckFinalCsv(success);
            Check(!Retained() && Outcome() == "Succeeded", "Successful close retires logical connection");
            AssertClosedHandles("success");

            StartWindow(); RetainConnection(); BeginReport();
            string locked = Path.Combine(output, "r28-close-retry.csv"); File.WriteAllText(locked, "ORIGINAL R28 TARGET");
            using (var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                prompt = ClosePrompt(); Invoke(Button(prompt, "Yes")); save = SaveDialog();
                Check(Retained(), "Locked target destination precedes disconnect");
                FillAndSave(save, locked);
                Check(Wait(() => Window("Confirm Save As") != null || Window("Export") != null, 10000), "Native overwrite/error reached");
                var overwrite = Window("Confirm Save As"); if (overwrite != null) Invoke(Button(overwrite, "Yes"));
                var error = WaitWindow("Export"); Thread.Sleep(250); Screenshot("03-post-disconnect-save-error"); Dump("save-error-tree");
                Check(!Retained() && Outcome() == "Succeeded", "Disconnect succeeded before native file-write error");
                Invoke(Button(error, "OK")); MainAlive();
                Check(Dirty() && NewCount() == 0 && File.ReadAllText(locked) == "ORIGINAL R28 TARGET", "Failed save preserves original and in-memory report");
                UI(() => {
                    object vehicle = Get(main, "vehicleTabForm");
                    Check(!((Button)Get(vehicle, "_disconnectButton")).Enabled &&
                        ((Button)Get(vehicle, "btnConnect")).Text == "Connect" &&
                        ((CheckBox)Get(vehicle, "_captureOnlyCheck")).Enabled &&
                        ((ComboBox)Get(vehicle, "_receiverSelect")).Enabled &&
                        ((ComboBox)Get(vehicle, "cbDevices")).Enabled,
                        "Vehicle controls refreshed after disconnect then failed save");
                    Check(!((Button)Get(vehicle, "btnConnect")).Enabled, "Connect remains disabled because provider inventory is empty");
                    var tabs = (TabControl)Get(main, "tabControl1"); tabs.SelectedTab = tabs.TabPages["tabVehicle"];
                });
                Screenshot("04-refreshed-vehicle-controls"); AssertProvidersClosed("save-failure");
            }
            // Save retry uses the real native dialog after the connection already retired.
            prompt = ClosePrompt(); Invoke(Button(prompt, "Yes")); save = SaveDialog();
            Check(!Retained() && Outcome() == "Succeeded", "Retry destination has no retained connection");
            FillAndSave(save, locked); var consent = WaitWindow("Confirm Save As"); Invoke(Button(consent, "Yes"));
            Closed(); CheckCsv(locked); CheckFinalCsv(locked); AssertClosedHandles("retry");
            Check(!Directory.GetFiles(output, "*.tmp").Any(), "Failure/retry leaves no export temporaries");

            string priorHash = Hash(success);
            StartWindow(); RetainConnection(); BeginReport();
            prompt = ClosePrompt(); Invoke(Button(prompt, "No")); Closed();
            Check(!Retained() && Outcome() == "Succeeded", "Actual No disconnects and closes");
            Check(Hash(success) == priorHash && Directory.GetFiles(output, "*.csv").Length == 2,
                "No leaves prior export unchanged and creates no CSV");
            AssertClosedHandles("discard");
            Check(Hash(Path.Combine(runtime, "J2534Diag.exe")) == ExpectedExe, "Delivered R28 EXE unchanged after native dialogs");
            File.WriteAllLines(Path.Combine(output, "verification.txt"), new[] { "RuntimeSHA256=" + ExpectedExe, "Source=" + ExpectedSource, "ProductRebuild=False", "PhysicalAdapter=False", "RegisteredProviders=0", "Checks=" + checks, "AcceptedObservations=0" }.Concat(notes));
            Console.WriteLine("PASS: " + checks + " exact-R28 close-order native-dialog checkpoints; zero observations/provider/vehicle.");
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
