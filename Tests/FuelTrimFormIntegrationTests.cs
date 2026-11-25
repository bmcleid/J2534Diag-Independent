using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;

namespace J2534Diag.Tests
{
    // Offline form tests plus explicitly gated disposable virtual-driver integration.
    // Provider discovery is bypassed; no physical driver is selected.
    internal static class FuelTrimFormIntegrationTests
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode,
            SetLastError = true, EntryPoint = "WaitNamedPipeW")]
        private static extern bool WaitNamedPipe(string name, uint timeout);
        private static int _checks;
        private static void Check(bool ok, string message)
        {
            _checks++;
            if (!ok) throw new Exception(message);
        }
        private static FieldInfo Field(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }
        private static object Get(object target, string name) { return Field(target, name).GetValue(target); }
        private static void Set(object target, string name, object value) { Field(target, name).SetValue(target, value); }
        private static object Call(object target, string name, params object[] args)
        {
            return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic).Invoke(target, args);
        }
        private static string Value(Form form, byte pid)
        {
            object controls = Get(form, "_pidControls");
            object control = controls.GetType().GetProperty("Item").GetValue(controls, new object[] { pid });
            return (string)control.GetType().GetProperty("ValueText").GetValue(control);
        }
        private static bool PumpUntil(Func<bool> condition, int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            do
            {
                Application.DoEvents();
                if (condition()) return true;
                Thread.Sleep(5);
            } while (clock.ElapsedMilliseconds < milliseconds);
            return false;
        }
        private static byte[] Reply(byte pid)
        {
            if (pid == 0x0C) return new byte[] { 0, 0, 7, 0xE8, 4, 0x41, pid, 0x0F, 0xA0 };
            byte value = pid == 0x11 ? (byte)51 : pid == 8 ? (byte)134 :
                pid == 9 ? (byte)126 : (byte)128;
            return new byte[] { 0, 0, 7, 0xE8, 3, 0x41, pid, value };
        }

        public static int RunManaged(string[] args)
        {
            bool brokenPipe = args.Length > 0 && args[0] == "--ci-managed-broken-pipe";
            bool missingVin = args.Length > 0 && args[0] == "--ci-managed-vin-failure";
            bool legacyFailure = args.Length > 0 && args[0] == "--ci-managed-vin-legacy";
            bool vinBootstrap = brokenPipe || missingVin || legacyFailure || (args.Length > 0 && args[0] == "--ci-managed-vin");
            if (args.Length != 2 || (!vinBootstrap && args[0] != "--ci-managed-sim") ||
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
                Environment.GetEnvironmentVariable("SRX_MANAGED_SIM_CI") != "ALLOW")
                throw new InvalidOperationException("Explicit disposable managed CI gate required");
            string dll = Path.GetFullPath(args[1]);
            if (!dll.Replace('\\', '/').EndsWith("/Tests/managed-sim/upstream/PassThruShim/x64/Release/PassThruShim64.dll",
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Wrong virtual driver path");
            string digest;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(dll))
                digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (digest != Environment.GetEnvironmentVariable("SRX_MANAGED_SIM_DLL_SHA256"))
                throw new InvalidOperationException("Virtual DLL hash mismatch");
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            var assembly = Assembly.LoadFrom(legacyFailure ? Environment.GetEnvironmentVariable("SRX_BASELINE_APP") :
                Path.Combine(root, "bin", "Release", "J2534Diag.exe"));
            var managerType = assembly.GetType("J2534Diag.J2534Manager", true);
            object manager = FormatterServices.GetUninitializedObject(managerType);
            object receiveGate = Activator.CreateInstance(assembly.GetType("J2534Diag.ReceiveChannelGate", true));
            managerType.GetField("<ReceiveGate>k__BackingField", BindingFlags.Instance |
                BindingFlags.NonPublic).SetValue(manager, receiveGate);
            Form form = null;
            try
            {
                const string syntheticVin = "TESTVIN1234567890";
                if (vinBootstrap)
                {
                    object parser = Activator.CreateInstance(assembly.GetType("J2534Diag.IsoTpParser", true));
                    var frameEvent = parser.GetType().GetEvent("FrameReceived");
                    var callback = managerType.GetMethod("IsoTpParser_FrameReceived",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    frameEvent.AddEventHandler(parser, Delegate.CreateDelegate(frameEvent.EventHandlerType, manager, callback));
                    Set(manager, "_isoTpParser", parser);
                    if (missingVin || legacyFailure)
                    {
                        var elapsed = Stopwatch.StartNew();
                        Exception startup = null;
                        try { Call(manager, "Initialize", dll); }
                        catch (TargetInvocationException ex) { startup = ex.InnerException; }
                        if (legacyFailure)
                        {
                            Check((bool)managerType.GetProperty("HasConnectionOrSelection").GetValue(manager),
                                "Historical failed/empty VIN startup ownership leak not reproduced");
                            Console.WriteLine("PASS: historical R16 unavailable-VIN startup retained resources, defect reproduced; exception=" +
                                (startup == null ? "none" : startup.GetType().Name));
                            return 0;
                        }
                        Check(startup is InvalidOperationException && startup.Message.StartsWith("No complete VIN"),
                            "Absent VIN did not end in the explicit identification failure: " + startup);
                        Check(elapsed.ElapsedMilliseconds >= 8500 && elapsed.ElapsedMilliseconds < 15000,
                            "VIN fallback attempts not bounded to three original response windows");
                        Check(managerType.GetProperty("Vin").GetValue(manager) == null, "Absent VIN fabricated identity");
                        Check(managerType.GetProperty("Channel").GetValue(manager) == null &&
                            managerType.GetProperty("Device").GetValue(manager) == null &&
                            managerType.GetProperty("Api").GetValue(manager) == null, "Failed VIN startup left native handles open");
                        Check(Get(manager, "_monitorThread") == null && Get(manager, "_cts") == null,
                            "Failed VIN startup retained worker/CTS");
                        Check(!(bool)managerType.GetProperty("HasConnectionOrSelection").GetValue(manager) &&
                            !(bool)Get(manager, "_managedLeaseHeld"), "Failed VIN startup retained global ownership");
                        Check(receiveGate.GetType().GetProperty("CurrentOwner").GetValue(receiveGate) == null,
                            "Failed VIN startup retained receive lease");
                        Console.WriteLine("PASS: " + _checks + " actual managed unavailable-VIN cleanup assertions; PROFILE=" + Environment.GetEnvironmentVariable("SRX_MANAGED_PROFILE"));
                        return 0;
                    }
                    Call(manager, "Initialize", dll);
                    Check((string)managerType.GetProperty("Vin").GetValue(manager) == syntheticVin,
                        "Normal startup did not reassemble exact synthetic Mode09 VIN");
                    Check(managerType.GetProperty("VinMode").GetValue(manager).ToString() == "ObdMode09" &&
                        managerType.GetProperty("BitMode").GetValue(manager).ToString() == "BITS_11",
                        "Normal startup unexpectedly fell through to UDS/29-bit");
                    Check(!(bool)managerType.GetProperty("CaptureOnlyMode").GetValue(manager),
                        "Normal startup did not select active diagnostic mode");
                    Check(Get(manager, "_monitorThread") == null && Get(manager, "_cts") == null,
                        "VIN monitor not retired before FuelTrims takes receive ownership");
                }
                else
                {
                    Call(manager, "InitializeCaptureOnly", dll,
                        Enum.ToObject(assembly.GetType("J2534Diag.BitType", true), 0));
                    // R19 fixture: permit scalars without VIN bootstrap.
                    managerType.GetField("<CaptureOnlyMode>k__BackingField", BindingFlags.Instance |
                        BindingFlags.NonPublic).SetValue(manager, false);
                }
                Check(managerType.GetProperty("Channel").GetValue(manager) != null,
                    "Real managed wrapper did not open virtual CAN channel");
                form = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.FuelTrimForm", true),
                    new object[] { manager });
                IntPtr handle = form.Handle;
                Call(form, "StartListening");
                Check(PumpUntil(() => Value(form, 0x0C) == "1000" && Value(form, 0x11) == "20",
                    6000), "Managed driver/form workers did not display synthetic RPM/throttle");
                foreach (byte pid in new byte[] { 6, 7, 8, 9 })
                {
                    double value;
                    Check(double.TryParse(Value(form, pid), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value),
                        "Managed scalar not numeric: " + pid.ToString("X2"));
                }
                Check(((ComboBox)Get(form, "_responseSource")).Items.Count == 1,
                    "Managed source selector did not isolate one synthetic ECM");
                Check((bool)Get(form, "_ownsReceiveChannel"), "Actual form did not retain receive ownership");
                if (brokenPipe)
                {
                    File.WriteAllText(Environment.GetEnvironmentVariable("SRX_MANAGED_BREAK_FILE"), "break virtual IPC only");
                    Check(PumpUntil(() => ((CancellationTokenSource)Get(form, "_cts")).IsCancellationRequested,
                        6000), "Interrupted virtual driver did not cancel peers");
                    Call(form, "StopListening");
                    Check(Get(form, "_monitorThread") == null && Get(form, "_pollingThread") == null,
                        "Faulted virtual workers not joined");
                    Check((bool)Get(form, "_ownsReceiveChannel"), "Broken filter cleanup falsely released ownership");
                    Exception refusal = null;
                    try { Call(manager, "Disconnect"); } catch (TargetInvocationException ex) { refusal = ex.InnerException; }
                    Check(refusal is InvalidOperationException, "Disconnect bypassed retained receive lease after driver fault");
                    Check(((string)managerType.GetProperty("LastDisconnectOutcome").GetValue(manager)).StartsWith("Failed:"),
                        "Disconnect error not preserved");
                    string faultExport = Path.Combine(root, "diagnostics", "managed-broken-pipe-session.csv");
                    Call(form, "ExportSessionTo", faultExport);
                    string evidence = File.ReadAllText(faultExport);
                    Check(evidence.Contains("CleanupError") && (evidence.Contains("WorkerError") || evidence.Contains("ReceiveError")) &&
                        evidence.Contains("DisconnectOutcome=Failed:"), "Fault export falsely reports successful cleanup");
                    Console.WriteLine("PASS: " + _checks + " actual broken-driver cancellation/retained-ownership/export checks. Virtual test-process exit releases IPC; no physical driver.");
                    return 0;
                }
                // Repeated start must not create a second poller/reader.
                object originalCancellation = Get(form, "_cts");
                Call(form, "StartListening");
                Check(ReferenceEquals(originalCancellation, Get(form, "_cts")), "Duplicate Start replaced a running session");
                using (var main = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.MainForm", true)))
                {
                    Set(main, "j2534Manager", manager); Set(main, "fuelTrimForm", form);
                    var tabs = (TabControl)Get(main, "tabControl1");
                    for (int repeat = 0; repeat < 3; repeat++)
                    {
                        tabs.SelectedTab = tabs.TabPages["tabVehicle"];
                        Call(main, "tabControl1_SelectedIndexChanged", null, EventArgs.Empty);
                        Check(!(bool)Get(form, "_ownsReceiveChannel") && Get(form, "_cts") == null,
                            "Tab departure retained previous workers/lease");
                        tabs.SelectedTab = tabs.TabPages["tabFuelTrim"];
                        Call(main, "tabControl1_SelectedIndexChanged", null, EventArgs.Empty);
                        Check(PumpUntil(() => Value(form, 0x0C) == "1000", 6000), "Tab return did not resume observations");
                        Check(Get(form, "_monitorThread") is Thread && ((Thread)Get(form, "_monitorThread")).IsAlive &&
                            Get(form, "_pollingThread") is Thread && ((Thread)Get(form, "_pollingThread")).IsAlive,
                            "Restarted session lacks its worker pair");
                    }
                }
                File.WriteAllText(Environment.GetEnvironmentVariable("SRX_MANAGED_SILENCE_FILE"), "silence");
                Check(PumpUntil(() => ((CancellationTokenSource)Get(form, "_cts")).IsCancellationRequested,
                    6000), "Absent virtual replies did not cancel actual managed polling");
                Check(PumpUntil(() => Value(form, 0x0C) == "Unknown / stale", 1000),
                    "Managed quiet-stop retained live RPM");
                Call(form, "StopListening");
                Check(!(bool)Get(form, "_ownsReceiveChannel"), "Managed form retained receive lease after stop");
                Check(Get(form, "_cts") == null && Get(form, "_pollReplyGate") == null,
                    "Managed form session objects not retired");
                Call(manager, "Disconnect");
                Check(managerType.GetProperty("Channel").GetValue(manager) == null &&
                    !(bool)managerType.GetProperty("HasConnectionOrSelection").GetValue(manager),
                    "Managed driver channel/device/API ownership not closed");
                Check((string)managerType.GetProperty("LastDisconnectOutcome").GetValue(manager) == "Succeeded",
                    "Successful disconnect not recorded");
                string normalExport = Path.Combine(root, "diagnostics", "managed-normal-session.csv");
                Call(form, "ExportSessionTo", normalExport);
                string exported = File.ReadAllText(normalExport);
                Check(exported.Contains("DisconnectOutcome=Succeeded") && exported.Contains("SessionStop") &&
                    exported.Contains("Observation") && exported.Contains("R22") && exported.Contains("TESTVIN1234567890"),
                    "Actual form export lost identity, observations or stop/disconnect outcome");
                if (vinBootstrap)
                {
                    // Pinned shim makes one immediate CreateFile attempt; its server recreates
                    // the single pipe asynchronously after close. Wait without opening a client.
                    Check(PumpUntil(() => WaitNamedPipe(@"\\.\pipe\GmEcuSim.PassThru", 50), 5000),
                        "Virtual host did not rearm its pipe after disconnect");
                    Call(manager, "Initialize", dll);
                    Check((string)managerType.GetProperty("Vin").GetValue(manager) == syntheticVin,
                        "Normal reconnect failed to recover synthetic VIN");
                    Check(Get(manager, "_monitorThread") == null && Get(manager, "_cts") == null,
                        "Reconnected VIN worker retained receive state");
                    Call(manager, "Disconnect");
                    Check(managerType.GetProperty("Channel").GetValue(manager) == null &&
                        !(bool)managerType.GetProperty("HasConnectionOrSelection").GetValue(manager),
                        "Reconnected managed handles not closed");
                }
                Console.WriteLine("PASS: " + _checks +
                    " actual managed virtual-driver/form assertions; VIN_BOOTSTRAP=" + vinBootstrap + "; NO PROVIDER REGISTRATION/VEHICLE.");
                return 0;
            }
            finally
            {
                if (brokenPipe)
                {
                    // Expected safe retention cannot be force-closed after IPC death.
                    // This isolated CI process exits after joined workers; OS retires virtual handles.
                    form?.Dispose();
                }
                else
                {
                    if (form != null) { Call(form, "StopListening"); form.Dispose(); }
                    Call(manager, "Disconnect");
                }
            }
        }

        public static int RunAll()
        {
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            var assembly = Assembly.LoadFrom(Path.Combine(root, "bin", "Release", "J2534Diag.exe"));
            Type managerType = assembly.GetType("J2534Diag.J2534Manager", true);
            // Avoid constructor: it enumerates installed J2534 providers.
            object manager = FormatterServices.GetUninitializedObject(managerType);
            object receiveGate = Activator.CreateInstance(assembly.GetType("J2534Diag.ReceiveChannelGate", true));
            managerType.GetField("<ReceiveGate>k__BackingField", BindingFlags.Instance |
                BindingFlags.NonPublic).SetValue(manager, receiveGate);
            using (var form = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.FuelTrimForm", true),
                new object[] { manager }))
            {
                IntPtr handle = form.Handle;
                Check(handle != IntPtr.Zero, "Actual form handle not created");
                Check(Value(form, 6) == "Unknown / stale", "Initial missing value fabricated");
                var grid1 = (DataGridView)Get(form, "dgvTrimGridBank1");
                var grid2 = (DataGridView)Get(form, "dgvTrimGridBank2");
                // Override the hosted desktop's default maximum tracking size;
                // a requested 1360px form can otherwise be clamped to its 1024px screen.
                form.MaximumSize = new System.Drawing.Size(2000, 1400);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = System.Drawing.Point.Empty;
                form.Show();
                form.ClientSize = new System.Drawing.Size(1360, 640);
                Application.DoEvents(); Call(form, "LayoutFuelPanels"); Application.DoEvents();
                var panel = (Panel)Get(form, "pnlGrids");
                Check(((TableLayoutPanel)Get(form, "tlpRight")).RowCount == 3, "Grid placed outside declared row count");
                Console.WriteLine("LAYOUT wide client=" + form.ClientSize + " panel=" + panel.ClientSize +
                    " grid1=" + grid1.Bounds + " grid2=" + grid2.Bounds);
                Check(panel.ClientSize.Width >= 1000, "Hosted test did not provide a wide viewport");
                Check(grid1.Top == grid2.Top && grid2.Left > grid1.Right, "Wide layout banks not side by side");
                Check(grid1.Bottom <= panel.ClientSize.Height && grid2.Bottom <= panel.ClientSize.Height,
                    "Wide grids clipped outside their viewport");
                grid1.FirstDisplayedScrollingRowIndex = 9; grid2.FirstDisplayedScrollingRowIndex = 9;
                Check(grid1.GetRowDisplayRectangle(9, false).Height > 0 && grid2.GetRowDisplayRectangle(9, false).Height > 0,
                    "Bottom history row not accessible through grid scrolling");
                Check(form.Text.Contains("R22") && ((Button)Get(form, "_exportButton")).Visible,
                    "Build identifier or export button not visible");
                if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                      bitmap.Save(Path.Combine(root, "diagnostics", "fuel-layout-wide.png")); }
                form.ClientSize = new System.Drawing.Size(800, 600); Application.DoEvents(); Call(form, "LayoutFuelPanels"); Application.DoEvents();
                Console.WriteLine("LAYOUT narrow client=" + form.ClientSize + " panel=" + panel.ClientSize +
                    " grid1=" + grid1.Bounds + " grid2=" + grid2.Bounds);
                Check(grid2.Top > grid1.Bottom && panel.AutoScrollMinSize.Height > panel.ClientSize.Height,
                    "Narrow banks did not stack with outer scrolling");
                panel.ScrollControlIntoView(grid2); Application.DoEvents();
                Check(grid2.Bounds.IntersectsWith(panel.ClientRectangle), "Second narrow bank cannot scroll into view");
                if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                      bitmap.Save(Path.Combine(root, "diagnostics", "fuel-layout-narrow.png")); }
                form.Hide();
                Check(string.IsNullOrEmpty(Convert.ToString(grid1.Rows[7].Cells[2].Value)), "Initial grid not blank");
                object gate = Activator.CreateInstance(assembly.GetType("J2534Diag.FuelTrimPollingGate", true),
                    new object[] { 50, 10, 6 });
                using (var cts = new CancellationTokenSource())
                {
                    Set(form, "_cts", cts); Set(form, "_pollReplyGate", gate);
                    Set(form, "_ownsReceiveChannel", true); Set(form, "_displayGeneration", 1);
                    Check((bool)Call(receiveGate, "TryAcquire", "FUEL_TRIM"), "Cannot acquire synthetic session");
                    var pids = new byte[] { 0x0C, 6, 7, 8, 9, 0x11 };
                    var definitions = (IEnumerable)Get(form, "ObdPids");
                    int freezeBank1 = 0, silence = 0, sent = 0;
                    Exception workerFailure = null;
                    Action<byte> send = pid =>
                    {
                        Interlocked.Increment(ref sent);
                        object definition = null;
                        foreach (object candidate in definitions)
                            if ((byte)candidate.GetType().GetProperty("Pid").GetValue(candidate) == pid)
                                definition = candidate;
                        var request = (byte[])Call(form, "BuildObdPidRequest", definition,
                            Enum.ToObject(assembly.GetType("J2534Diag.BitType", true), 0));
                        if (request.Length != 12 || request[4] != 2 || request[5] != 1 || request[6] != pid)
                            throw new Exception("Real request builder changed Mode01 shape");
                        if (Volatile.Read(ref silence) != 0 ||
                            (pid == 6 && Volatile.Read(ref freezeBank1) != 0)) return;
                        Call(form, "QueueScalarSample", Reply(pid), 0u, 1);
                    };
                    Action work = () => Call(gate, "Run", (Func<byte[]>)(() => pids), send, cts.Token);
                    var worker = new Thread(() => assembly.GetType("J2534Diag.ChannelLifecyclePolicy", true)
                        .GetMethod("RunCoupledWorker").Invoke(null, new object[] {
                            cts, work, (Action<Exception>)(ex => workerFailure = ex) })) { IsBackground = true };
                    Set(form, "_pollingThread", worker);
                    worker.Start();
                    try
                    {
                        Check(PumpUntil(() => Value(form, 0x0C) == "1000" && Value(form, 6) == "0" &&
                            Convert.ToString(grid2.Rows[7].Cells[2].Value) == "3", 4000),
                            "Synthetic requests/replies did not reach actual form values and both grids");
                        Check(Convert.ToString(grid1.Rows[7].Cells[2].Value) == "0", "Measured zero bank missing");
                        Check(Value(form, 0x11) == "20", "Actual throttle formula/display mismatch");
                        Check(((ComboBox)Get(form, "_responseSource")).Items.Count == 1, "Source selection not populated");
                        Volatile.Write(ref freezeBank1, 1);
                        Check(PumpUntil(() => Value(form, 6) == "Unknown / stale", 4000), "Actual timer did not expire one missing channel");
                        Check(Value(form, 0x0C) == "1000", "Healthy RPM expired with missing bank1 channel");
                        Check(Convert.ToString(grid1.Rows[7].Cells[2].Value) == "0", "Expiry erased historical grid");
                        Volatile.Write(ref silence, 1);
                        Check(PumpUntil(() => !worker.IsAlive, 3000), "Silence did not terminate actual polling gate");
                        Check(cts.IsCancellationRequested, "Quiet terminal exit did not cancel session");
                        Check(PumpUntil(() => Value(form, 0x0C) == "Unknown / stale", 1000), "Cancelled session retained live values");
                        Check(workerFailure == null, "Synthetic worker failed: " + workerFailure);
                        Check(sent >= 6, "Full request cycle not exercised");
                        Call(form, "StartListening");
                        Check(!(bool)Get(form, "_ownsReceiveChannel"), "Restart did not retire old lease");
                        Check(Get(form, "_cts") == null && Get(form, "_pollReplyGate") == null,
                            "Retired session resources retained");
                        // No channel exists: StartListening must stop after cleanup without a driver call.
                        Check(Call(receiveGate, "get_CurrentOwner") == null, "Receive lease remains held");
                        Set(form, "_ownsReceiveChannel", true);
                        using (var fresh = new CancellationTokenSource())
                        {
                            Set(form, "_cts", fresh); Set(form, "_displayGeneration", 2);
                            Call(form, "QueueScalarSample", Reply(0x0C), 0u, 1);
                            Application.DoEvents();
                            Check(Value(form, 0x0C) == "Unknown / stale", "Old generation leaked into new session");
                            Call(form, "QueueScalarSample", Reply(0x0C), 0u, 2);
                            Check(PumpUntil(() => Value(form, 0x0C) == "1000", 1000), "Fresh generation did not resume display");
                            Set(form, "_ownsReceiveChannel", false); Set(form, "_cts", null);
                        }
                    }
                    finally
                    {
                        try { cts.Cancel(); } catch (ObjectDisposedException) { }
                        if (!worker.Join(3000)) throw new Exception("Synthetic polling worker failed cleanup");
                        if ((bool)Get(form, "_ownsReceiveChannel"))
                        {
                            Set(form, "_ownsReceiveChannel", false); Set(form, "_cts", null);
                            ((IDisposable)gate).Dispose();
                        }
                    }
                }
            }
            Console.WriteLine("PASS: " + _checks + " actual FuelTrimForm integration assertions; synthetic callbacks; NO DRIVER/VEHICLE.");
            return _checks;
        }
    }
}



