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
    // Loads the real app, but never runs driver discovery or any driver call.
    // Synthetic callback transport enters the real scalar receive/UI boundary.
    internal static class FuelTrimFormIntegrationTests
    {
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
