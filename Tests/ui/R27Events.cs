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

internal static class R27Events
{
    const string ExpectedExe = "cf5d699d92e7f1d8969b6f1b873c9169b906765eb0aff85222fe218b541755e9";
    const string ExpectedSource = "6d8b7541d91f687e28d24cfa91d8f7e4ed7d12db";
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
        Check(Wait(() => main != null && main.IsHandleCreated && fuel != null, 10000), "Exact R27 MainForm started with production OnLoad");
        Check(UI(() => main.Text.Contains("R27 / 6d8b7541d91f")), "R27 visible build identity matches preserved EXE");
        AssertProvidersClosed("startup");
        UI(() => { var tabs = (TabControl)Get(main, "tabControl1"); tabs.SelectedTab = tabs.TabPages["tabFuelTrim"]; });
        Check(!Dirty() && NewCount() == 0, "Untouched production report is clean with zero observations");
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
        Check(rows.Skip(1).All(r => r[3].Contains(ExpectedSource) && r[4] == ExpectedExe), "Dialog export provenance matches exact R27 runtime");
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

    static void Standalone()
    {
        using (var process = Process.Start(new ProcessStartInfo(Path.Combine(runtime, "J2534Diag.exe")) { WorkingDirectory = runtime }))
        {
            currentPid = process.Id;
            Check(Wait(() => Windows().Any(w => w.Current.Name.Contains("J2534Diag R27 / 6d8b7541d91f")), 10000), "Preserved standalone R27 executable launched");
            Screenshot("01-standalone-r26"); Modules(process, "standalone");
            var window = Windows().First(w => w.Current.Name.Contains("J2534Diag R27"));
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
            Check(Hash(Path.Combine(runtime, "J2534Diag.exe")) == ExpectedExe, "Exact delivered R27 EXE verified");
            WatchExports();
            app = Assembly.LoadFrom(Path.Combine(runtime, "J2534Diag.exe"));
            Check((string)app.GetType("J2534Diag.BuildIdentity").GetField("SourceCommit").GetRawConstantValue() == ExpectedSource, "R27 compiled source exact");
            StartWindow();
            UI(() => { main.BeginInvoke(new Action(() => main.Close())); }); Closed();
            Check(Window("Session report") == null, "Untouched zero-data app closes without a report prompt");

            StartWindow();
            UI(() => { Call(Report, "Begin", "OFFLINE event-only fixture", "TESTVIN0000000001", DateTime.UtcNow);
                Call(Report, "SetStatus", "Unanswered requests", "No synthetic scalar replies", DateTime.UtcNow); });
            Check(Dirty() && NewCount() == 0, "No-response event-only session is dirty with zero observations");
            Check(Wait(() => UI(() => ((Label)Get(fuel, "_sessionHealth")).Text.Contains("Unsaved report: Yes")), 3000), "Health shows unsaved report with zero observations");
            Screenshot("01-event-only-wide");
            UI(() => main.Size = new Size(720, 700));
            Check(UI(() => { var l = (Label)Get(fuel, "_sessionHealth"); return l.Right <= l.Parent.ClientSize.Width && l.Bottom <= l.Parent.ClientSize.Height; }), "Event-only health fits narrow viewport");
            Screenshot("02-event-only-narrow");
            var prompt = ClosePrompt();
            Check(prompt.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().Any(e => e.Current.Name.Contains("0 new observations") && e.Current.Name.Contains("unsaved observations, events or counter changes")), "Real R27 prompt explains event-only evidence with zero observations");
            Thread.Sleep(150); Screenshot("03-real-event-only-prompt"); Dump("event-only-prompt-tree");
            Keys(prompt, "{ESC}"); MainAlive(); Check(Dirty() && NewCount() == 0, "Event-only Escape retains dirty report");
            prompt = ClosePrompt(); Keys(prompt, "{ENTER}"); var save = SaveDialog(); Keys(save, "{ESC}"); MainAlive();
            Check(Dirty(), "Event-only cancelled native save remains dirty");
            string locked = Path.Combine(output, "event-only-retry.csv"); File.WriteAllText(locked, "ORIGINAL EVENT TARGET");
            using (var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                prompt = ClosePrompt(); Keys(prompt, "{ENTER}"); save = SaveDialog(); FillAndSave(save, locked);
                Check(Wait(() => Window("Confirm Save As") != null || Window("Export") != null, 10000), "Event-only overwrite/error reached");
                var overwrite = Window("Confirm Save As"); if (overwrite != null) Invoke(Button(overwrite, "Yes"));
                var error = WaitWindow("Export"); Button(error, "OK"); Thread.Sleep(150); Screenshot("04-event-only-save-error"); Dump("event-only-error-tree");
                Invoke(Button(error, "OK")); MainAlive();
                Check(Dirty() && NewCount() == 0 && File.ReadAllText(locked) == "ORIGINAL EVENT TARGET", "Real event-only failed save preserves original and report");
            }
            prompt = ClosePrompt(); Keys(prompt, "{ENTER}"); save = SaveDialog(); FillAndSave(save, locked);
            var consent = WaitWindow("Confirm Save As"); Invoke(Button(consent, "Yes")); Closed(); CheckCsv(locked);
            Check(File.ReadAllText(locked).Contains("Unanswered requests"), "Retried CSV retains no-response event");

            StartWindow(); UI(() => Call(Report, "Event", "Disconnect", "Synthetic successful explicit disconnect", DateTime.UtcNow));
            ExportButton(); save = SaveDialog(); string first = Path.Combine(output, "disconnect-only.csv"); FillAndSave(save, first);
            Check(Wait(() => File.Exists(first) && !Dirty(), 10000), "Actual native save acknowledges disconnect-only report"); CheckCsv(first);
            string savedHash = Hash(first);
            UI(() => Call(Report, "Event", "Disconnect", "Synthetic later failed disconnect", DateTime.UtcNow));
            Check(Dirty() && NewCount() == 0, "Later event-only change reopens dirty report");
            prompt = ClosePrompt(); Keys(prompt, "{TAB}");
            Check(Wait(() => Name(AutomationElement.FocusedElement).Equals("No", StringComparison.OrdinalIgnoreCase), 2000), "Event-only Tab reaches No");
            Keys(prompt, "{ENTER}"); Closed(); Check(Hash(first) == savedHash, "Event-only No closes without replacing prior export");

            StartWindow();
            UI(() => Call(Report, "Account", Enum.Parse(app.GetType("J2534Diag.FuelTrimEvidenceCounter"), "QueueCapacityDiscarded"), 12345678));
            Check(Dirty() && NewCount() == 0, "Counter-only change is dirty without a started observation session");
            UI(() => main.Size = new Size(720, 700));
            Check(Wait(() => UI(() => ((Label)Get(fuel, "_sessionHealth")).Text.Contains("12345678") && ((Label)Get(fuel, "_sessionHealth")).Text.Contains("Unsaved report: Yes")), 3000), "Counter-only health updated");
            Check(UI(() => { var l = (Label)Get(fuel, "_sessionHealth"); return l.Right <= l.Parent.ClientSize.Width && l.Bottom <= l.Parent.ClientSize.Height; }), "Counter-only health fits narrow viewport");
            Screenshot("05-counter-only-health");
            prompt = ClosePrompt(); Keys(prompt, "{ESC}"); MainAlive(); Check(Dirty(), "Counter-only Cancel retains evidence");
            prompt = ClosePrompt(); Keys(prompt, "{ENTER}"); save = SaveDialog(); string counterFile = Path.Combine(output, "counter-only.csv"); FillAndSave(save, counterFile);
            Closed(); CheckCsv(counterFile);
            Check(File.ReadAllText(counterFile).Contains("QueueCapacityDiscarded=12345678"), "Counter-only CSV preserves exact counter");
            Check(!Directory.GetFiles(output, "*.tmp").Any(), "Event-only failure/retry left no export temporaries");
            Check(Hash(Path.Combine(runtime, "J2534Diag.exe")) == ExpectedExe, "Delivered R27 EXE unchanged after UI sequence");
            File.WriteAllLines(Path.Combine(output, "verification.txt"), new[] { "RuntimeSHA256=" + ExpectedExe, "Source=" + ExpectedSource, "ProductRebuild=False", "PhysicalAdapter=False", "RegisteredProviders=0", "Checks=" + checks, "AcceptedObservations=0" }.Concat(notes));
            Console.WriteLine("PASS: " + checks + " exact-R27 event-only native-dialog checkpoints; zero observations/provider/vehicle.");
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
