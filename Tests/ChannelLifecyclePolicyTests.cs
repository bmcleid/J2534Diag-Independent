using System;
using System.IO;
using System.Threading;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class ChannelLifecyclePolicyTests
    {
        private static int _checks;
        private static void Check(bool ok, string message)
        {
            _checks++;
            if (!ok) throw new Exception(message);
        }

        public static int RunAll()
        {
            MustNotDisconnectWhileCaptureOwnsChannel();
            WaitsForCooperatingMonitorBeforeDisconnect();
            RetainsRunningMonitorAfterTimeout();
            UnstartedMonitorCanBeStopped();
            SourceUsesSameGuardForCloseAndReconnect();
            return _checks;
        }

        private static void MustNotDisconnectWhileCaptureOwnsChannel()
        {
            var gate = new ReceiveChannelGate();
            ChannelLifecyclePolicy.RequireUnowned(gate);
            Check(gate.TryAcquire("CAN_CAPTURE"), "Could not acquire test lease");
            try
            {
                ChannelLifecyclePolicy.RequireUnowned(gate);
                throw new Exception("Disconnect was permitted while CAN reader owned channel");
            }
            catch (InvalidOperationException ex)
            {
                Check(ex.Message.Contains("CAN_CAPTURE"), "Ownership refusal omitted reader identity");
            }
            Check(gate.CurrentOwner == "CAN_CAPTURE", "Refused disconnect accidentally released lease");
            gate.Release("CAN_CAPTURE");
            ChannelLifecyclePolicy.RequireUnowned(gate);
            Check(gate.CurrentOwner == null, "Lease still held after proper release");
        }

        private static void WaitsForCooperatingMonitorBeforeDisconnect()
        {
            using (var cts = new CancellationTokenSource())
            using (var entered = new ManualResetEventSlim(false))
            {
                var worker = new Thread(() =>
                {
                    entered.Set();
                    while (!cts.IsCancellationRequested)
                        Thread.Sleep(1);
                }) { IsBackground = true };
                worker.Start();
                Check(entered.Wait(1000), "Monitor worker did not start");
                Check(ChannelLifecyclePolicy.RequestStopAndWait(worker, cts, 2000),
                    "Cooperating VIN monitor did not join");
                Check(cts.IsCancellationRequested && !worker.IsAlive,
                    "Monitor was not cancelled and terminated before channel close");
            }
        }

        private static void RetainsRunningMonitorAfterTimeout()
        {
            using (var cts = new CancellationTokenSource())
            using (var entered = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                var worker = new Thread(() => { entered.Set(); release.Wait(); })
                { IsBackground = true };
                worker.Start();
                try
                {
                    Check(entered.Wait(1000), "Blocked worker did not start");
                    Check(!ChannelLifecyclePolicy.RequestStopAndWait(worker, cts, 20),
                        "Still-running monitor was incorrectly reported as stopped");
                    Check(worker.IsAlive, "Monitor unexpectedly terminated before release");
                }
                finally
                {
                    release.Set();
                }
                Check(ChannelLifecyclePolicy.RequestStopAndWait(worker, cts, 2000),
                    "Stop retry did not join worker after release");
            }
        }

        private static void UnstartedMonitorCanBeStopped()
        {
            using (var cts = new CancellationTokenSource())
            {
                var worker = new Thread(() => { });
                Check(ChannelLifecyclePolicy.RequestStopAndWait(worker, cts, 50),
                    "Unstarted background monitor incorrectly failed shutdown");
                Check(cts.IsCancellationRequested, "Unstarted worker did not cancel pending token");
            }
            Check(ChannelLifecyclePolicy.RequestStopAndWait(null, null, 50),
                "Missing monitor must not block a valid disconnect");
        }

        private static void SourceUsesSameGuardForCloseAndReconnect()
        {
            string root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            string manager = File.ReadAllText(Path.Combine(root, "Services", "J2534Manager.cs"));
            string vehicle = File.ReadAllText(Path.Combine(root, "Forms", "VehicleTabForm.cs"));
            string main = File.ReadAllText(Path.Combine(root, "Forms", "MainForm.cs"));
            Check(manager.Contains("public void Disconnect()") &&
                  manager.Contains("StopVinMonitorSafely();") &&
                  manager.Contains("ChannelLifecyclePolicy.RequireUnowned(ReceiveGate);"),
                "J2534 manager disconnect no longer enforces channel lease");
            int cap = manager.IndexOf("public void InitializeCaptureOnly(", StringComparison.Ordinal);
            int active = manager.IndexOf("private void InitializeChannel(", cap + 1, StringComparison.Ordinal);
            string captureCode = manager.Substring(cap, active - cap);
            Check(captureCode.Contains("StopVinMonitorSafely();") &&
                  captureCode.Contains("CloseChannelAndDevice();"),
                "Capture-only reconnect bypasses old worker shutdown");
            Check(vehicle.Contains("_disconnectButton.Click += DisconnectButton_Click;") &&
                  vehicle.Contains("j2534Manager.Disconnect();"),
                "Vehicle UI lacks explicit guarded disconnect");
            Check(main.Contains("selectedVehicleControl.SetVehicle(false, \"\", \"\");"),
                "Disconnect does not clear stale vehicle connection banner");
            // The VIN listener must be permitted its original response window,
            // then cancelled and joined before pass filters are removed.
            Check(manager.Contains("_monitorThread.Join(3000);") &&
                  manager.Contains("finally") &&
                  manager.Contains("StopVinMonitorSafely();\n                Channel.StopMsgFilter(filterId);"),
                "Active VIN discovery no longer waits for replies or securely joins its reader");
        }
    }
}
