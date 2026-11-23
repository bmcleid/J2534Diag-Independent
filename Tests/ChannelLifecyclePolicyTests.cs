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
            BoundedFuelPollingHandlesReplySilenceAndCancellation();
            CoupledFailureCancelsPeerAndRetainsLeaseUntilJoined();
            MustNotDisconnectWhileCaptureOwnsChannel();
            WaitsForCooperatingMonitorBeforeDisconnect();
            RetainsRunningMonitorAfterTimeout();
            UnstartedMonitorCanBeStopped();
            SourceUsesSameGuardForCloseAndReconnect();
            TabNavigationRespectsPendingCaptureShutdown();
            return _checks;
        }



        private static void BoundedFuelPollingHandlesReplySilenceAndCancellation()
        {
            using (var gate = new FuelTrimPollingGate(5, 1, 3))
            using (var cts = new CancellationTokenSource())
            {
                int sent = 0;
                gate.Run(() => new byte[] { 6, 7 }, pid => sent++, cts.Token);
                Check(sent == 3, "Silence did not stop at configured miss limit");
                Check(!gate.Observe(6), "Completed request remained pending");
            }
            using (var gate = new FuelTrimPollingGate(5, 1, 3))
            using (var cts = new CancellationTokenSource())
            {
                int sent = 0;
                gate.Run(() => new byte[] { 6 }, pid =>
                {
                    sent++;
                    if (sent == 2) Check(gate.Observe(pid), "Synchronous reply lost before wait");
                    if (sent == 5) cts.Cancel();
                }, cts.Token);
                Check(sent == 5, "Valid reply did not reset consecutive miss count");
            }
            using (var gate = new FuelTrimPollingGate(5, 1, 2))
            using (var cts = new CancellationTokenSource())
            {
                int sent = 0;
                gate.Run(() => new byte[] { 6 }, pid =>
                {
                    sent++;
                    Check(!gate.Observe(7), "Wrong PID completed pending request");
                }, cts.Token);
                Check(sent == 2, "Wrong-PID replies prevented quiet stop");
            }
            using (var gate = new FuelTrimPollingGate(5, 1, 2))
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                int sent = 0;
                gate.Run(() => new byte[] { 6 }, pid => sent++, cts.Token);
                Check(sent == 0, "Already-cancelled session transmitted");
            }
            using (var gate = new FuelTrimPollingGate(5, 1, 2))
            using (var cts = new CancellationTokenSource())
            {
                int sent = 0;
                gate.Run(() => new byte[0], pid => sent++, cts.Token);
                Check(sent == 0, "No enabled channels still transmitted");
                var injected = new InvalidOperationException("send failure");
                Exception caught = null;
                try { gate.Run(() => new byte[] { 6 }, pid => { throw injected; }, cts.Token); }
                catch (Exception ex) { caught = ex; }
                Check(ReferenceEquals(caught, injected), "Send failure identity lost");
                Check(!gate.Observe(6), "Failed send retained pending request");
            }
            using (var gate = new FuelTrimPollingGate(2000, 1, 2))
            using (var cts = new CancellationTokenSource())
            using (var sentEvent = new ManualResetEventSlim(false))
            {
                int sent = 0;
                var worker = new Thread(() => gate.Run(() => new byte[] { 6 }, pid =>
                {
                    Interlocked.Increment(ref sent); sentEvent.Set();
                }, cts.Token)) { IsBackground = true };
                worker.Start();
                try
                {
                    Check(sentEvent.Wait(2000), "Cancellable waiter never sent");
                    cts.Cancel();
                    Check(worker.Join(1000), "Cancellation did not interrupt response wait");
                    Check(sent == 1, "Cancellation permitted another request");
                }
                finally { cts.Cancel(); if (!worker.Join(3000)) throw new Exception("Waiter cleanup failed"); }
            }
            string root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            string form = File.ReadAllText(Path.Combine(root, "Forms", "FuelTrimForm.cs"));
            Check(form.Contains("sessionPollGate.Run(") &&
                !form.Contains("Thread.Sleep(2);"), "Form bypasses bounded poll gate");
            Check(form.Contains("if (input.Protocol == \"Mode01\") _pollReplyGate?.Observe(input.Pid);"),
                "Poller wakeup not gated to validated Mode01 scalar");
            Check(form.IndexOf("_pollReplyGate?.Dispose();", StringComparison.Ordinal) >
                form.IndexOf("bool monitorStopped =", StringComparison.Ordinal),
                "Reply gate disposed before worker join");
        }

        private static void CoupledFailureCancelsPeerAndRetainsLeaseUntilJoined()
        {
            using (var cts = new CancellationTokenSource())
            using (var entered = new ManualResetEventSlim(false))
            using (var sawCancellation = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                var gate = new ReceiveChannelGate();
                Check(gate.TryAcquire("FUEL_TRIM"), "Test lease unavailable");
                var peer = new Thread(() => ChannelLifecyclePolicy.RunCoupledWorker(cts, () =>
                {
                    entered.Set();
                    cts.Token.WaitHandle.WaitOne();
                    sawCancellation.Set();
                    release.Wait();
                }, null)) { IsBackground = true };
                peer.Start();
                try
                {
                    Check(entered.Wait(2000), "Peer did not start");
                    var injected = new InvalidOperationException("injected read failure");
                    Exception reported = null;
                    ChannelLifecyclePolicy.RunCoupledWorker(cts, () => { throw injected; },
                        ex => reported = ex);
                    Check(ReferenceEquals(reported, injected), "Original failure not preserved");
                    Check(cts.IsCancellationRequested, "Failed reader did not cancel session");
                    Check(sawCancellation.Wait(2000), "Polling peer did not observe cancellation");
                    Check(!ChannelLifecyclePolicy.RequestStopAndWait(peer, cts, 10),
                        "Blocked peer was declared stopped");
                    Check(gate.CurrentOwner == "FUEL_TRIM", "Lease released before peer stopped");
                    using (var replacement = new CancellationTokenSource())
                    {
                        Check(!replacement.IsCancellationRequested, "New session inherited cancellation");
                        ChannelLifecyclePolicy.RunCoupledWorker(replacement, () => { }, null);
                        Check(replacement.IsCancellationRequested, "Normal terminal exit did not cancel peer");
                    }
                }
                finally
                {
                    cts.Cancel();
                    release.Set();
                    if (!peer.Join(2000)) throw new Exception("Test peer failed cleanup");
                }
                Check(ChannelLifecyclePolicy.RequestStopAndWait(peer, cts, 2000),
                    "Stopped peer could not be retired");
                gate.Release("FUEL_TRIM");
                Check(gate.CurrentOwner == null, "Lease not releasable after join");
            }
            string root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            string form = File.ReadAllText(Path.Combine(root, "Forms", "FuelTrimForm.cs"));
            Check(form.Split(new[] { "RunCoupledWorker(sessionCancellation" },
                StringSplitOptions.None).Length == 3, "Both form workers must share cancellation wrapper");
            Check(form.Contains("if (_ownsReceiveChannel) return;") &&
                form.Contains("StopListening();"), "Restart does not retain pending session");
            Check(form.Contains("_cts == null || _cts.IsCancellationRequested) return;"),
                "Cancelled session still applies queued samples");
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

        private static void TabNavigationRespectsPendingCaptureShutdown()
        {
            string root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            string main = File.ReadAllText(Path.Combine(root, "Forms", "MainForm.cs"));
            int start = main.IndexOf("private void TabControl_SelectingWithCaptureGuard(",
                StringComparison.Ordinal);
            int end = start < 0 ? -1 : main.IndexOf("private void tabControl1_SelectedIndexChanged(",
                start, StringComparison.Ordinal);
            Check(main.Contains("tabControl1.Selecting += TabControl_SelectingWithCaptureGuard;"),
                "Main form did not subscribe to cancellable tab selection event");
            Check(start >= 0 && end > start,
                "Missing pre-selection lifecycle handler");
            if (start >= 0 && end > start)
            {
                string handler = main.Substring(start, end - start);
                Check(handler.Contains("tabCanListener") &&
                      handler.Contains("!canListenerForm.StopListening()") &&
                      handler.Contains("e.Cancel = true;"),
                    "CAN tab can be abandoned despite failed receiver shutdown");
            }
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
            int vinWait = manager.IndexOf("_monitorThread.Join(3000);", StringComparison.Ordinal);
            int cleanupFinally = vinWait < 0 ? -1 :
                manager.IndexOf("finally", vinWait, StringComparison.Ordinal);
            int stopMonitor = cleanupFinally < 0 ? -1 :
                manager.IndexOf("StopVinMonitorSafely();", cleanupFinally, StringComparison.Ordinal);
            int removeFilter = stopMonitor < 0 ? -1 :
                manager.IndexOf("Channel.StopMsgFilter(filterId);", stopMonitor, StringComparison.Ordinal);
            Check(vinWait >= 0 && cleanupFinally > vinWait &&
                  stopMonitor > cleanupFinally && removeFilter > stopMonitor,
                "Active VIN discovery no longer waits for replies or securely joins its reader");
        }
    }
}
