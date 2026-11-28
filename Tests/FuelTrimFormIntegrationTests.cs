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

        private static void AuditQueueAndIdleExport(Assembly assembly, Type managerType)
        {
            object manager = FormatterServices.GetUninitializedObject(managerType);
            object receiveGate = Activator.CreateInstance(assembly.GetType("J2534Diag.ReceiveChannelGate", true));
            managerType.GetField("<ReceiveGate>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, receiveGate);
            string export = Path.Combine(Path.GetTempPath(), "J2534Diag-audit-" + Guid.NewGuid().ToString("N") + ".csv");
            using (var form = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.FuelTrimForm", true), new object[] { manager }))
            using (var cts = new CancellationTokenSource())
            {
                IntPtr handle = form.Handle;
                object report = Get(form, "_sessionReport");
                Type counterType = assembly.GetType("J2534Diag.FuelTrimEvidenceCounter", true);
                Func<string, long> count = name => (long)Call(report, "Counter", Enum.Parse(counterType, name));
                Func<long> accepted = () => (long)report.GetType().GetProperty("AcceptedObservations").GetValue(report);
                try
                {
                    Call(form, "ExportSessionTo", export);
                    string idle = File.ReadAllText(export);
                    Check(idle.Contains("SessionState=Idle") && !idle.Contains("\"Observation\""), "Pre-session export fabricated observations or activity");
                    Check(idle.Split(new[] { "\"PidSummary\"" }, StringSplitOptions.None).Length == 7, "Idle export lacks six zero summaries");
                    Call(report, "Begin", "bounded source audit", "", DateTime.UtcNow);
                    Set(form, "_ownsReceiveChannel", true); Set(form, "_cts", cts); Set(form, "_displayGeneration", 1);
                    byte[] pids = { 0x0C, 6, 7, 8, 9, 0x11 };
                    for (int source = 0; source < 17; source++)
                        foreach (byte pid in pids)
                        {
                            byte[] frame = Reply(pid);
                            frame[0] = 0x18; frame[1] = 0xDA; frame[2] = 0xF1; frame[3] = (byte)source;
                            Call(form, "QueueScalarSample", frame, 0x100u, 1);
                        }
                    Check(count("EligibleScalarFrames") == 102 && count("QueueCapacityDiscarded") == 6,
                        "Queue capacity did not account exactly six of 102 distinct scalar callbacks");
                    Call(form, "FlushScalarSamples");
                    var selector = (ComboBox)Get(form, "_responseSource");
                    Check(accepted() == 96 && selector.Items.Count == 16, "16-source boundary changed accepted observations");
                    selector.SelectedIndex = 1;
                    Call(form, "btnClearFuelTrims_Click", null, EventArgs.Empty);
                    Check(accepted() == 96, "Source selection or Clear Charts erased accepted observations");
                    byte[] seventeenth = Reply(6); seventeenth[0] = 0x18; seventeenth[1] = 0xDA;
                    seventeenth[2] = 0xF1; seventeenth[3] = 16;
                    Call(form, "QueueScalarSample", seventeenth, 0x100u, 1); Call(form, "FlushScalarSamples");
                    Check(count("DecodeOrStateRejected") == 1 && accepted() == 96 && selector.Items.Count == 16,
                        "Source-cap rejection hidden or evicted an accepted source");
                    Call(form, "QueueScalarSample", new byte[0], 0u, 1);
                    Check(count("NonScalarOrInvalidIgnored") == 1, "Invalid input not counted separately");
                    Call(form, "ExportSessionTo", export);
                    string audit = File.ReadAllText(export);
                    Check(audit.Contains("SourceSelected") && audit.Contains("ChartsCleared") && audit.Contains("QueueCapacityDiscarded=6"),
                        "Actual-form export omitted display events or queue-capacity evidence");

                    // Force a callback to wait after its first active-session check.
                    object sync = Get(form, "_sampleQueueLock"); Exception failure = null;
                    var worker = new Thread(() => { try { Call(form, "QueueScalarSample", Reply(6), 0u, 1); } catch (Exception ex) { failure = ex; } });
                    long eligible = count("EligibleScalarFrames"), inactive = count("InactiveOrGenerationDiscarded");
                    Monitor.Enter(sync);
                    try
                    {
                        worker.Start();
                        Check(SpinWait.SpinUntil(() => count("EligibleScalarFrames") == eligible + 1 &&
                            (worker.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 2000),
                            "Callback did not reach the contended queue lock");
                        Set(form, "_displayGeneration", 2); cts.Cancel();
                        Call(form, "DiscardPendingSamples", Enum.Parse(counterType, "InactiveOrGenerationDiscarded"));
                    }
                    finally { Monitor.Exit(sync); }
                    Check(worker.Join(2000) && failure == null, "Contended callback did not retire: " + failure);
                    Check(((IDictionary)Get(form, "_pendingSamples")).Count == 0 &&
                        count("InactiveOrGenerationDiscarded") == inactive + 1 && accepted() == 96,
                        "Retired callback entered the queue after stop's drain or was counted twice");
                    Call(report, "SetStatus", "No enabled PIDs", "all disabled", DateTime.UtcNow);
                    Check(PumpUntil(() => ((Label)Get(form, "_sessionHealth")).Text.Contains("No enabled PIDs"), 1000),
                        "Freshness timer overwrote the disabled-PID stop reason");
                    Call(form, "ExportSessionTo", export);
                    Check(File.ReadAllText(export).Contains("SessionState=No enabled PIDs"), "Export lost disabled-PID stop reason");
                }
                finally
                {
                    Set(form, "_ownsReceiveChannel", false); Set(form, "_cts", null);
                    if (File.Exists(export)) File.Delete(export);
                }
            }
        }

        private static void AuditRefusedReconnect(Assembly assembly, Type managerType)
        {
            object manager = FormatterServices.GetUninitializedObject(managerType);
            object gate = Activator.CreateInstance(assembly.GetType("J2534Diag.ReceiveChannelGate", true));
            managerType.GetField("<ReceiveGate>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, gate);
            managerType.GetField("<Vin>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, "TESTVIN0000000001");
            Check((bool)Call(gate, "TryAcquire", "FUEL_TRIM"), "Cannot acquire retained-session fixture");
            try
            {
                try { Call(manager, "Disconnect"); } catch (TargetInvocationException ex) { Check(ex.InnerException is InvalidOperationException, "Wrong disconnect refusal"); }
                string failed = (string)managerType.GetProperty("LastDisconnectOutcome").GetValue(manager);
                Check(failed.StartsWith("Failed:"), "Prior failed disconnect missing");
                foreach (string method in new[] { "Initialize", "InitializeCaptureOnly" })
                {
                    Exception refusal = null;
                    try
                    {
                        if (method == "Initialize") Call(manager, method, "not-a-real-provider.dll");
                        else Call(manager, method, "not-a-real-provider.dll", Enum.ToObject(assembly.GetType("J2534Diag.BitType", true), 0));
                    }
                    catch (TargetInvocationException ex) { refusal = ex.InnerException; }
                    Check(refusal is InvalidOperationException || refusal is AggregateException, "Reconnect did not stop at retained receive lease");
                    Check((string)managerType.GetProperty("LastDisconnectOutcome").GetValue(manager) == failed,
                        "Refused " + method + " erased the retained connection's disconnect failure");
                    Check((string)managerType.GetProperty("Vin").GetValue(manager) == "TESTVIN0000000001" &&
                        (string)Call(gate, "get_CurrentOwner") == "FUEL_TRIM", "Refused reconnect changed retained connection context");
                    Check(managerType.GetProperty("Channel").GetValue(manager) == null && managerType.GetProperty("Api").GetValue(manager) == null,
                        "Refused reconnect touched a driver before the ownership gate");
                }
            }
            finally { Call(gate, "Release", "FUEL_TRIM"); Call(manager, "Disconnect"); }
            Check((string)managerType.GetProperty("LastDisconnectOutcome").GetValue(manager) == "Succeeded" &&
                !(bool)managerType.GetProperty("HasConnectionOrSelection").GetValue(manager), "Retry after lease retirement failed to clean up");
            Call(manager, "Disconnect");
            Check((string)managerType.GetProperty("LastDisconnectOutcome").GetValue(manager) == "Succeeded", "Repeated idle disconnect changed success outcome");
        }

        private static void AuditCloseAndControls(Assembly assembly, Type managerType)
        {
            object manager = FormatterServices.GetUninitializedObject(managerType);
            object gate = Activator.CreateInstance(assembly.GetType("J2534Diag.ReceiveChannelGate", true));
            managerType.GetField("<ReceiveGate>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, gate);
            string file = Path.Combine(Path.GetTempPath(), "J2534Diag-close-" + Guid.NewGuid().ToString("N") + ".csv");
            using (var form = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.FuelTrimForm", true), new object[] { manager }))
            using (var cts = new CancellationTokenSource())
            using (var main = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.MainForm", true)))
            using (var vehicle = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.VehicleTabForm", true), new object[] { main, manager }))
            {
                IntPtr handle = form.Handle;
                object report = Get(form, "_sessionReport");
                Func<long> unsaved = () => (long)report.GetType().GetProperty("ObservationsSinceExport").GetValue(report);
                try
                {
                    Call(report, "Begin", "close fixture", "", DateTime.UtcNow);
                    Set(form, "_ownsReceiveChannel", true); Set(form, "_cts", cts); Set(form, "_displayGeneration", 1);
                    Call(form, "QueueScalarSample", Reply(6), 0u, 1); Call(form, "FlushScalarSamples");
                    Set(form, "_ownsReceiveChannel", false); Set(form, "_cts", null);
                    Check(unsaved() == 1, "Accepted observation not marked unexported");
                    object definition = null;
                    foreach (object candidate in (IEnumerable)Get(form, "ObdPids"))
                        if ((byte)candidate.GetType().GetProperty("Pid").GetValue(candidate) == 6) definition = candidate;
                    definition.GetType().GetProperty("Enabled").SetValue(definition, false);
                    Call(form, "RefreshFuelDisplay", false);
                    Check(Value(form, 6) == "Disabled" && unsaved() == 1, "Disabled gauge confused with stale or removed report evidence");
                    definition.GetType().GetProperty("Enabled").SetValue(definition, true);
                    Call(form, "RefreshFuelDisplay", false);
                    Check(Value(form, 6) == "0", "Re-enabled fresh observation lost");
                    int saves = 0;
                    Func<bool> save = () => { saves++; Call(form, "ExportSessionTo", file); return true; };
                    Set(form, "_saveUnsavedReport", save);
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Cancel));
                    Check(!(bool)Call(form, "ConfirmReportBeforeClose") && saves == 0 && unsaved() == 1, "Cancel close discarded evidence");
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Yes));
                    Set(form, "_saveUnsavedReport", (Func<bool>)(() => false));
                    Check(!(bool)Call(form, "ConfirmReportBeforeClose") && unsaved() == 1, "Cancelled save allowed close");
                    File.WriteAllText(file, "original");
                    using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        Set(form, "_saveUnsavedReport", (Func<bool>)(() => { try { Call(form, "ExportSessionTo", file); return true; } catch (TargetInvocationException) { return false; } }));
                        Check(!(bool)Call(form, "ConfirmReportBeforeClose") && unsaved() == 1, "Failed save allowed close or acknowledged evidence");
                    }
                    Check(File.ReadAllText(file) == "original", "Close-save failure damaged prior target");
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.No));
                    Check((bool)Call(form, "ConfirmReportBeforeClose") && unsaved() == 1, "Explicit discard pretended to export");
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Yes)); Set(form, "_saveUnsavedReport", save);
                    Check((bool)Call(form, "ConfirmReportBeforeClose") && unsaved() == 0 && saves == 1 && File.ReadAllText(file).Contains("Observation"), "Save-before-close did not preserve report");
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => { throw new Exception("Unexpected clean-report prompt"); }));
                    Check((bool)Call(form, "ConfirmReportBeforeClose"), "Exported report prompted again");
                    Call(report, "Event", "Disconnect", "Failed: event-only fixture", DateTime.UtcNow);
                    Check(unsaved() == 0, "Event-only fixture unexpectedly created an observation");
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Cancel));
                    Check(!(bool)Call(form, "ConfirmReportBeforeClose"), "Event-only Cancel allowed close");
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.No));
                    Check((bool)Call(form, "ConfirmReportBeforeClose") &&
                        (bool)report.GetType().GetProperty("HasUnexportedChanges").GetValue(report), "Event-only No pretended to save");
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Yes));
                    Set(form, "_saveUnsavedReport", (Func<bool>)(() => false));
                    Check(!(bool)Call(form, "ConfirmReportBeforeClose"), "Event-only cancelled save allowed close");
                    Set(form, "_saveUnsavedReport", save);
                    Check((bool)Call(form, "ConfirmReportBeforeClose") && File.ReadAllText(file).Contains("Failed: event-only fixture") &&
                        !(bool)report.GetType().GetProperty("HasUnexportedChanges").GetValue(report), "Event-only save failed to preserve changes");
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => { throw new Exception("Unexpected event-only clean prompt"); }));
                    Check((bool)Call(form, "ConfirmReportBeforeClose"), "Unchanged event-only export prompted again");
                    Call(report, "Observe", "source", (byte)6, 1.0, 100L, DateTime.UtcNow, 100L);
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Cancel));
                    RetainLogicalConnection(assembly, manager);
                    Set(main, "j2534Manager", manager); Set(main, "fuelTrimForm", form);
                    var cancel = new FormClosingEventArgs(CloseReason.UserClosing, false); Call(main, "OnFormClosing", cancel);
                    Check(cancel.Cancel && unsaved() == 1 && managerType.GetProperty("LastDisconnectOutcome").GetValue(manager) == null,
                        "Parent close bypassed report cancellation or disposed manager");
                    Set(form, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Yes));
                    var saved = new FormClosingEventArgs(CloseReason.UserClosing, false); Call(main, "OnFormClosing", saved);
                    Check(!saved.Cancel && unsaved() == 0 && (string)managerType.GetProperty("LastDisconnectOutcome").GetValue(manager) == "Succeeded" &&
                        File.ReadAllText(file).Contains("DisconnectOutcome=Succeeded") && File.ReadAllText(file).Contains("Disconnect") &&
                        !(bool)report.GetType().GetProperty("HasUnexportedChanges").GetValue(report),
                        "Parent close failed to capture final disconnect before export");

                    var connect = (Button)Get(vehicle, "btnConnect"); var disconnect = (Button)Get(vehicle, "_disconnectButton");
                    var capture = (CheckBox)Get(vehicle, "_captureOnlyCheck"); var width = (ComboBox)Get(vehicle, "_canWidthSelect");
                    var receiver = (ComboBox)Get(vehicle, "_receiverSelect");
                    connect.Text = "Connecting ..."; Call(vehicle, "RefreshConnectionControls");
                    Check(connect.Text == "Connect" && !connect.Enabled && !disconnect.Enabled, "No-provider/cancelled attempt left false connecting state");
                    Type listType = managerType.GetProperty("Devices").PropertyType;
                    var devices = (IList)Activator.CreateInstance(listType); Type item = listType.GetGenericArguments()[0];
                    devices.Add(item.IsValueType ? Activator.CreateInstance(item) : FormatterServices.GetUninitializedObject(item));
                    managerType.GetField("<Devices>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, devices);
                    Call(vehicle, "RefreshConnectionControls");
                    Check(connect.Enabled && capture.Enabled && width.Enabled && receiver.Enabled, "Disconnected controls not restored");
                    Set(manager, "_managedLeaseHeld", true); connect.Text = "Connecting ..."; Call(vehicle, "RefreshConnectionControls");
                    Check(!connect.Enabled && disconnect.Enabled && !capture.Enabled && !width.Enabled && !receiver.Enabled &&
                        !((ComboBox)Get(vehicle, "cbDevices")).Enabled && connect.Text.Contains("Connection retained"), "Retained connection permits mode/receiver changes or false Connect");
                    Set(manager, "_managedLeaseHeld", false); receiver.SelectedIndex = 1; Call(vehicle, "RefreshConnectionControls");
                    Check(capture.Checked && !capture.Enabled && !width.Enabled && connect.Enabled, "Prepared-native selection exposed active/width controls");
                    receiver.SelectedIndex = 0; Call(vehicle, "RefreshConnectionControls");
                    Check(capture.Enabled && width.Enabled && connect.Text == "Connect", "Managed disconnected controls not restored after receiver switch");
                }
                finally
                {
                    Set(manager, "_managedLeaseHeld", false); Set(form, "_ownsReceiveChannel", false); Set(form, "_cts", null);
                    if (File.Exists(file)) File.Delete(file);
                }
            }
        }

        private static void RetainLogicalConnection(Assembly assembly, object manager)
        {
            var ownership = assembly.GetType("J2534Diag.J2534ConnectionOwnership", true);
            Check((bool)ownership.GetMethod("TryAcquire").Invoke(null, new object[] { manager, "Offline close fixture" }),
                "Logical connection lease not acquired");
            Set(manager, "_managedLeaseHeld", true);
        }

        public static int RunCloseBaseline(string executable)
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("CI-only baseline");
            using (var sha = System.Security.Cryptography.SHA256.Create())
                Check(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(executable))).Replace("-", "").ToLowerInvariant() ==
                    "cf5d699d92e7f1d8969b6f1b873c9169b906765eb0aff85222fe218b541755e9", "Wrong R27 baseline EXE");
            var assembly = Assembly.LoadFrom(Path.GetFullPath(executable)); var type = assembly.GetType("J2534Diag.J2534Manager", true);
            object manager = FormatterServices.GetUninitializedObject(type);
            type.GetField("<ReceiveGate>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager,
                Activator.CreateInstance(assembly.GetType("J2534Diag.ReceiveChannelGate", true)));
            RetainLogicalConnection(assembly, manager); // Logical fixture only: no API/device/channel/provider.
            string file = Path.Combine(Path.GetFullPath("diagnostics"), "r27-close-baseline.csv");
            using (var main = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.MainForm", true)))
            using (var fuel = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.FuelTrimForm", true), new object[] { manager }))
            {
                object report = Get(fuel, "_sessionReport"); Call(report, "Begin", "offline close baseline", "", DateTime.UtcNow);
                Set(main, "j2534Manager", manager); Set(main, "fuelTrimForm", fuel);
                Set(fuel, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Yes));
                Set(fuel, "_saveUnsavedReport", (Func<bool>)(() => { Call(fuel, "ExportSessionTo", file); return true; }));
                var closing = new FormClosingEventArgs(CloseReason.UserClosing, false); Call(main, "OnFormClosing", closing);
                Check(!closing.Cancel && (string)type.GetProperty("LastDisconnectOutcome").GetValue(manager) == "Succeeded", "R27 fixture did not finish disconnect");
                string text = File.ReadAllText(file);
                Check(!text.Contains("\"Disconnect\"") && text.Contains("DisconnectOutcome=Not attempted"), "R27 missing-final-outcome baseline did not reproduce");
                Check((bool)report.GetType().GetProperty("HasUnexportedChanges").GetValue(report), "R27 later disconnect did not remain unsaved");
                Check(type.GetProperty("Api").GetValue(manager) == null && type.GetProperty("Device").GetValue(manager) == null &&
                    type.GetProperty("Channel").GetValue(manager) == null, "Baseline unexpectedly opened provider handles");
            }
            Console.WriteLine("PASS: " + _checks + " exact-R27 close-before-disconnect defect reproduction checks; no provider/vehicle.");
            return 0;
        }

        private static void AuditCloseOrdering(Assembly assembly, Type type)
        {
            object manager = FormatterServices.GetUninitializedObject(type);
            object gate = Activator.CreateInstance(assembly.GetType("J2534Diag.ReceiveChannelGate", true));
            type.GetField("<ReceiveGate>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, gate);
            string file = Path.Combine(Path.GetTempPath(), "J2534Diag-final-close-" + Guid.NewGuid().ToString("N") + ".csv");
            using (var main = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.MainForm", true)))
            using (var fuel = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.FuelTrimForm", true), new object[] { manager }))
            using (var vehicle = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.VehicleTabForm", true), new object[] { main, manager }))
            {
                object report = Get(fuel, "_sessionReport");
                Func<bool> dirty = () => (bool)report.GetType().GetProperty("HasUnexportedChanges").GetValue(report);
                Set(main, "j2534Manager", manager); Set(main, "fuelTrimForm", fuel); Set(main, "vehicleTabForm", vehicle);
                Type devicesType = type.GetProperty("Devices").PropertyType; var devices = (IList)Activator.CreateInstance(devicesType);
                Type deviceType = devicesType.GetGenericArguments()[0]; devices.Add(deviceType.IsValueType ? Activator.CreateInstance(deviceType) : FormatterServices.GetUninitializedObject(deviceType));
                type.GetField("<Devices>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, devices);
                int errors = 0, saves = 0; Set(main, "_reportCloseFailure", (Action<Exception>)(ex => errors++));
                Action actualClose = (Action)Get(main, "_disconnectForClose");
                Func<FormClosingEventArgs> close = () => { var e = new FormClosingEventArgs(CloseReason.UserClosing, false); Call(main, "OnFormClosing", e); return e; };
                Func<bool> save = () => { saves++; try { Call(fuel, "ExportSessionTo", file); return true; } catch (TargetInvocationException) { return false; } };
                try
                {
                    RetainLogicalConnection(assembly, manager);
                    Call(vehicle, "RefreshConnectionControls");
                    Check(!dirty(), "Empty logical connection fixture unexpectedly dirty");
                    Set(fuel, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Cancel));
                    Set(fuel, "_saveUnsavedReport", save);
                    Check(close().Cancel && (bool)Get(manager, "_managedLeaseHeld") && saves == 0, "Cancel disconnected clean retained connection");
                    Set(fuel, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Yes));
                    Set(fuel, "_saveUnsavedReport", (Func<bool>)(() => false));
                    Check(close().Cancel && (bool)Get(manager, "_managedLeaseHeld") && type.GetProperty("LastDisconnectOutcome").GetValue(manager) == null,
                        "Cancelled save destination disconnected connection");
                    Call(report, "Begin", "offline final-close fixture", "", DateTime.UtcNow);
                    File.WriteAllText(file, "ORIGINAL"); Set(fuel, "_saveUnsavedReport", save);
                    using (var hold = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        Check(close().Cancel && !(bool)Get(manager, "_managedLeaseHeld") && dirty() &&
                            (string)type.GetProperty("LastDisconnectOutcome").GetValue(manager) == "Succeeded", "Post-disconnect save failure lost evidence or retained false connection");
                    Check(File.ReadAllText(file) == "ORIGINAL", "Failed close save damaged existing file");
                    Check(((Button)Get(vehicle, "btnConnect")).Enabled && !((Button)Get(vehicle, "_disconnectButton")).Enabled &&
                        ((CheckBox)Get(vehicle, "_captureOnlyCheck")).Enabled, "Post-disconnect save failure left retained-connection controls locked");
                    Check(!close().Cancel && !dirty(), "Retry did not save retained disconnect events");
                    string text = File.ReadAllText(file);
                    Check(text.Contains("DisconnectOutcome=Succeeded") && text.Contains("\"Disconnect\"") && text.Contains("\"Pending\"") && text.Contains("\"Succeeded\""),
                        "Final close export omitted disconnect outcome/events");
                    Check(text.Split(new[] { "\"Disconnect\"" }, StringSplitOptions.None).Length == 3,
                        "Retry duplicated disconnect events after connection already closed");
                    Set(fuel, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => { throw new Exception("Clean repeated close unexpectedly prompted"); }));
                    Check(!close().Cancel && !dirty(), "Repeated close dirtied clean disconnected report");
                    int before = saves; RetainLogicalConnection(assembly, manager);
                    Set(fuel, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.No));
                    Check(!close().Cancel && saves == before && dirty() && File.ReadAllText(file) == text, "No failed to disconnect or fabricated export");
                    RetainLogicalConnection(assembly, manager);
                    Set(main, "_disconnectForClose", (Action)(() => { Call(report, "Event", "Disconnect", "Failed: injected close", DateTime.UtcNow); throw new IOException("injected disconnect refusal"); }));
                    Check(close().Cancel && errors == 1 && (bool)Get(manager, "_managedLeaseHeld") && dirty(), "Disconnect refusal escaped parent close or lost retained state");
                    Set(fuel, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Yes));
                    Check(close().Cancel && errors == 1 && (bool)Get(manager, "_managedLeaseHeld") && dirty(),
                        "Yes-disconnect refusal allowed close or lost retained evidence");
                    // Failed close hook must not leak into an ordinary manual export.
                    Call(fuel, "ExportSessionTo", file);
                    Check(File.ReadAllText(file).Contains("Failed: injected close"), "Manual export could not preserve failed-close evidence");
                    int attempts = 0; Set(main, "_disconnectForClose", (Action)(() => attempts++));
                    Check((bool)Call(gate, "TryAcquire", "CLOSE_PENDING_FIXTURE"), "Pending receive fixture not acquired");
                    Check(close().Cancel && attempts == 0 && errors == 2, "Pending reader reached disconnect/export");
                    Call(gate, "Release", "CLOSE_PENDING_FIXTURE");
                    Set(main, "_disconnectForClose", actualClose);
                    Set(fuel, "_chooseUnsavedReportAction", (Func<DialogResult>)(() => DialogResult.Yes)); Set(fuel, "_saveUnsavedReport", save);
                    Check(!close().Cancel && !dirty() && errors == 2, "Final retry did not recover from refused/pending close");
                    if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                        File.Copy(file, Path.Combine(Path.GetFullPath("diagnostics"), "r28-final-close.csv"), true);
                }
                finally { if ((string)gate.GetType().GetProperty("CurrentOwner").GetValue(gate) == "CLOSE_PENDING_FIXTURE") Call(gate, "Release", "CLOSE_PENDING_FIXTURE"); if ((bool)Get(manager, "_managedLeaseHeld")) Call(manager, "Disconnect"); if (File.Exists(file)) File.Delete(file); }
            }
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
                    Check(((Label)Get(form, "_sessionHealth")).Text.Contains("Driver error"), "Broken driver health reason hidden");
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
                    RetainLogicalConnection(assembly, manager);
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
                Check(((Label)Get(form, "_sessionHealth")).Text.Contains("Unanswered requests"), "Quiet stop health reason hidden");
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
                    exported.Contains("Observation") && exported.Contains("R29") && exported.Contains("TESTVIN1234567890"),
                    "Actual form export lost identity, observations or stop/disconnect outcome");
                if (vinBootstrap)
                {
                    // Pinned shim makes one immediate CreateFile attempt; its server recreates
                    // the single pipe asynchronously after close. Wait without opening a client.
                    Check(PumpUntil(() => WaitNamedPipe(@"\\.\pipe\GmEcuSim.PassThru", 50), 5000),
                        "Virtual host did not rearm its pipe after disconnect");
                    Call(manager, "Initialize", dll);
                    Check((string)managerType.GetProperty("LastDisconnectOutcome").GetValue(manager) == "Not attempted for current connection",
                        "Successful reconnect retained previous disconnect outcome");
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
            Console.WriteLine("R26 validation: connection controls, unsaved observations and close/export choices.");
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
                Check(panel.Parent == form && ((Control)Get(form, "flpPids")).Parent == form &&
                    ((Control)Get(form, "flpActions")).Parent == form, "Three viewport regions not attached to actual form");
                Console.WriteLine("LAYOUT wide client=" + form.ClientSize + " panel=" + panel.ClientSize +
                    " grid1=" + grid1.Bounds + " grid2=" + grid2.Bounds);
                Check(panel.ClientSize.Width == form.ClientSize.Width - 12, "Grid viewport did not follow actual form width");
                Check(panel.ClientSize.Width >= 1000, "Hosted test did not provide a wide viewport");
                Check(grid1.Top == grid2.Top && grid2.Left > grid1.Right, "Wide layout banks not side by side");
                Check(grid1.Bottom <= panel.ClientSize.Height && grid2.Bottom <= panel.ClientSize.Height,
                    "Wide grids clipped outside their viewport");
                grid1.FirstDisplayedScrollingRowIndex = 9; grid2.FirstDisplayedScrollingRowIndex = 9;
                Check(grid1.GetRowDisplayRectangle(9, false).Height > 0 && grid2.GetRowDisplayRectangle(9, false).Height > 0,
                    "Bottom history row not accessible through grid scrolling");
                Check(form.Text.Contains("R29") && ((Button)Get(form, "_exportButton")).Visible,
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
                var health = (Label)Get(form, "_sessionHealth");
                var actions = (FlowLayoutPanel)Get(form, "flpActions");
                Check(health.Bottom <= actions.ClientSize.Height && health.Right <= actions.ClientSize.Width,
                    "Narrow session health clipped in action viewport");
                var originalFont = form.Font;
                using (var enlarged = new System.Drawing.Font(originalFont.FontFamily, originalFont.Size * 1.25f))
                {
                    form.Font = enlarged; Call(form, "LayoutFuelPanels"); Application.DoEvents();
                    Check(health.Bottom <= actions.ClientSize.Height && health.Right <= actions.ClientSize.Width,
                        "125-percent font session health clipped");
                    form.Font = originalFont; Call(form, "LayoutFuelPanels"); Application.DoEvents();
                }
                form.Hide();
                Check(string.IsNullOrEmpty(Convert.ToString(grid1.Rows[7].Cells[2].Value)), "Initial grid not blank");
                object gate = Activator.CreateInstance(assembly.GetType("J2534Diag.FuelTrimPollingGate", true),
                    new object[] { 50, 10, 6 });
                using (var cts = new CancellationTokenSource())
                {
                    Set(form, "_cts", cts); Set(form, "_pollReplyGate", gate);
                    Set(form, "_ownsReceiveChannel", true); Set(form, "_displayGeneration", 1);
                    Check((bool)Call(receiveGate, "TryAcquire", "FUEL_TRIM"), "Cannot acquire synthetic session");
                    object report = Get(form, "_sessionReport");
                    Call(report, "Begin", "offline form fixture", "", DateTime.UtcNow);
                    for (int i = 0; i < 5; i++) Call(form, "QueueScalarSample", Reply(6), 0u, 1);
                    Call(form, "FlushScalarSamples");
                    Check((long)report.GetType().GetProperty("AcceptedObservations").GetValue(report) == 1,
                        "Coalesced form samples did not yield exactly one accepted observation");
                    string queueExport = Path.Combine(Path.GetTempPath(), "J2534Diag-queue-" + Guid.NewGuid().ToString("N") + ".csv");
                    try
                    {
                        Call(form, "ExportSessionTo", queueExport);
                        Check(File.ReadAllText(queueExport).Contains("CoalescedBeforeAcceptance=4"), "Form queue did not account replaced samples");
                    }
                    finally { File.Delete(queueExport); }
                    long acceptedBeforeClear = (long)report.GetType().GetProperty("AcceptedObservations").GetValue(report);
                    Call(form, "btnClearFuelTrims_Click", null, EventArgs.Empty);
                    Check((long)report.GetType().GetProperty("AcceptedObservations").GetValue(report) == acceptedBeforeClear,
                        "Clear Charts erased accepted report observations");

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
                        long beforeCancelledCallback = (long)report.GetType().GetProperty("AcceptedObservations").GetValue(report);
                        Call(form, "QueueScalarSample", Reply(6), 0u, 1);
                        Call(form, "FlushScalarSamples");
                        Check((long)report.GetType().GetProperty("AcceptedObservations").GetValue(report) == beforeCancelledCallback,
                            "Cancelled callback became an accepted observation");
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
                            object retiredGate = Activator.CreateInstance(assembly.GetType("J2534Diag.FuelTrimPollingGate", true), new object[] { 5, 1, 1 });
                            Set(form, "_pollReplyGate", retiredGate);
                            Call(retiredGate, "Run", (Func<byte[]>)(() => new byte[] { 0x0C }),
                                (Action<byte>)(pid => Call(form, "QueueScalarSample", Reply(pid), 0u, 1)), fresh.Token);
                            Check((string)retiredGate.GetType().GetProperty("LastStopReason").GetValue(retiredGate) == "Unanswered requests",
                                "Retired callback acknowledged new session request");
                            Call(retiredGate, "Dispose"); Set(form, "_pollReplyGate", null);
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
            AuditQueueAndIdleExport(assembly, managerType);
            AuditRefusedReconnect(assembly, managerType);
            AuditCloseAndControls(assembly, managerType);
            AuditCloseOrdering(assembly, managerType);
            Console.WriteLine("PASS: " + _checks + " actual FuelTrimForm integration assertions; synthetic callbacks; NO DRIVER/VEHICLE.");
            return _checks;
        }
    }
}
