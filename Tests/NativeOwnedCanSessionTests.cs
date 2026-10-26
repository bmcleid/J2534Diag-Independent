using System;
using System.Collections.Generic;
using System.Linq;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class NativeOwnedCanSessionTests
    {
        private static int _checks;
        private static void Check(bool ok, string why)
        {
            _checks++;
            if (!ok) throw new Exception(why);
        }

        private sealed class Fake : INativeJ2534CanApi
        {
            public readonly List<string> Calls = new List<string>();
            public string FailAt;
            public NativeCanReadResult NextRead = new NativeCanReadResult(0x10, 0, null);
            public uint Open(out uint device)
            {
                Calls.Add("Open"); device = 44;
                return FailAt == "Open" ? 3U : 0U;
            }
            public uint ConnectCan500k(uint device, out uint channel)
            {
                Calls.Add("Connect:" + device); channel = 55;
                return FailAt == "Connect" ? 3U : 0U;
            }
            public uint StartWildcardPassFilter(uint channel, out uint filter)
            {
                Calls.Add("Filter:" + channel); filter = 66;
                return FailAt == "Filter" ? 3U : 0U;
            }
            public NativeCanReadResult ReadCan(uint channel, int maximum, int timeout)
            {
                Calls.Add("Read:" + channel + ":" + maximum + ":" + timeout);
                return NextRead;
            }
            public uint StopFilter(uint channel, uint filter)
            {
                Calls.Add("StopFilter:" + channel + ":" + filter);
                return FailAt == "StopFilter" ? 3U : 0U;
            }
            public uint Disconnect(uint channel)
            {
                Calls.Add("Disconnect:" + channel);
                return FailAt == "Disconnect" ? 3U : 0U;
            }
            public uint Close(uint device)
            {
                Calls.Add("Close:" + device);
                return FailAt == "Close" ? 3U : 0U;
            }
        }

        public static int RunAll()
        {
            RefuseWithoutConsentOrExclusiveManagedRelease();
            OpenReadAndCleanupExactOrder();
            OpenErrorDoesNotContinue();
            ConnectFailureClosesOnlyDevice();
            FilterFailureClosesChannelAndDevice();
            RefuseConcurrentNativeSession();
            CleanupFailureRetainsGateForRetry();
            StopFilterErrorStillAttemptsOtherCleanup();
            return _checks;
        }

        private static void RefuseWithoutConsentOrExclusiveManagedRelease()
        {
            var fake = new Fake();
            bool managedInactive = false;
            using (var session = new NativeOwnedCanSession(fake, () => managedInactive))
            {
                try { session.Start(false); throw new Exception("Consent bypassed"); }
                catch (InvalidOperationException ex)
                {
                    Check(ex.Message.Contains("consent"), "Consent denial not enforced");
                }
                try { session.Start(true); throw new Exception("Managed ownership bypassed"); }
                catch (InvalidOperationException ex)
                {
                    Check(ex.Message.Contains("Managed"), "Managed channel ownership not checked");
                }
                Check(fake.Calls.Count == 0, "Native API called without mandatory guards");
                managedInactive = true;
                try { session.ReadNative(100, 5); throw new Exception("Read while closed"); }
                catch (InvalidOperationException) { _checks++; }
            }
        }

        private static void OpenReadAndCleanupExactOrder()
        {
            var fake = new Fake();
            using (var session = new NativeOwnedCanSession(fake, () => true))
            {
                session.Start(true);
                Check(session.IsReceiving, "Started channel not marked active");
                var response = session.ReadNative(100, 5);
                Check(response.Status == 0x10 && response.Count == 0,
                    "Native read status/count lost");
                session.Stop();
                Check(!session.IsReceiving, "Channel active after clean Stop");
                session.Stop(); // idempotent.
                Check(fake.Calls.SequenceEqual(new[]{
                    "Open", "Connect:44", "Filter:55", "Read:55:100:5",
                    "StopFilter:55:66","Disconnect:55","Close:44"
                }), "Wrong native call order or duplicated cleanup");
            }
        }

        private static void OpenErrorDoesNotContinue()
        {
            var fake = new Fake { FailAt = "Open" };
            using (var session = new NativeOwnedCanSession(fake, () => true))
            {
                try { session.Start(true); throw new Exception("Open error accepted"); }
                catch (InvalidOperationException ex)
                {
                    Check(ex.Message.Contains("PassThruOpen"), "Open fault not surfaced");
                }
                Check(fake.Calls.SequenceEqual(new[]{"Open"}),
                    "Connection continued after a failed PassThruOpen");
            }
        }

        private static void ConnectFailureClosesOnlyDevice()
        {
            var fake = new Fake { FailAt = "Connect" };
            using (var session = new NativeOwnedCanSession(fake, () => true))
            {
                try { session.Start(true); throw new Exception("Connect error accepted"); }
                catch (InvalidOperationException ex)
                {
                    Check(ex.Message.Contains("PassThruConnect"),"Connect error suppressed");
                }
                Check(fake.Calls.SequenceEqual(new[]{"Open","Connect:44","Close:44"}),
                    "Connect failure did not clean up device independently");
            }
        }

        private static void FilterFailureClosesChannelAndDevice()
        {
            var fake = new Fake { FailAt = "Filter" };
            using (var session = new NativeOwnedCanSession(fake, () => true))
            {
                try { session.Start(true); throw new Exception("Filter error accepted"); }
                catch (InvalidOperationException ex)
                {
                    Check(ex.Message.Contains("PassThruStartMsgFilter"), "Filter error masked");
                }
                Check(fake.Calls.SequenceEqual(new[]{
                    "Open","Connect:44","Filter:55","Disconnect:55","Close:44"
                }), "Failed filter not safely cleaned up");
            }
        }

        private static void RefuseConcurrentNativeSession()
        {
            var primary = new NativeOwnedCanSession(new Fake(), () => true);
            var secondApi = new Fake();
            using (var secondary = new NativeOwnedCanSession(secondApi, () => true))
            {
                try
                {
                    primary.Start(true);
                    try { secondary.Start(true); throw new Exception("Concurrent native owner accepted"); }
                    catch (InvalidOperationException ex)
                    {
                        Check(ex.Message.Contains("owns"), "Process-wide native ownership missing");
                    }
                    Check(secondApi.Calls.Count == 0,
                        "Second native owner called device API despite gate");
                }
                finally { primary.Dispose(); }
                secondary.Start(true);
                Check(secondary.IsReceiving, "Gate not released after clean primary close");
            }
        }

        private static void CleanupFailureRetainsGateForRetry()
        {
            var failing = new Fake { FailAt = "Close" };
            var session = new NativeOwnedCanSession(failing, () => true);
            var secondApi = new Fake();
            try
            {
                session.Start(true);
                try { session.Stop(); throw new Exception("Close failure hidden"); }
                catch (InvalidOperationException ex)
                {
                    Check(ex.Message.Contains("PassThruClose"),
                        "Failed device close not reported");
                }
                using (var secondary = new NativeOwnedCanSession(secondApi, () => true))
                {
                    try { secondary.Start(true); throw new Exception("Failed close released gate"); }
                    catch (InvalidOperationException ex)
                    {
                        Check(ex.Message.Contains("owns"), "Unsafe gate release on close failure");
                    }
                }
                failing.FailAt = null;
                session.Stop();
                Check(failing.Calls.Count(x => x == "Close:44") == 2 &&
                    failing.Calls.Count(x => x == "Disconnect:55") == 1,
                    "Retry repeated already-successful cleanup or skipped failed close");
            }
            finally { session.Dispose(); }
        }

        private static void StopFilterErrorStillAttemptsOtherCleanup()
        {
            var fake = new Fake { FailAt = "StopFilter" };
            var session = new NativeOwnedCanSession(fake, () => true);
            try
            {
                session.Start(true);
                try { session.Stop(); throw new Exception("StopFilter failure ignored"); }
                catch (InvalidOperationException ex)
                {
                    Check(ex.Message.Contains("PassThruStopMsgFilter"),
                        "StopFilter failure disappeared");
                }
                Check(fake.Calls.Contains("Disconnect:55") &&
                    fake.Calls.Contains("Close:44"),
                    "A failed filter stop skipped remaining cleanup calls");
                fake.FailAt = null;
                session.Stop();
                Check(fake.Calls.Count(x => x == "StopFilter:55:66") == 1,
                    "Successfully retired filter retried after channel disconnect");
            }
            finally { session.Dispose(); }
        }
    }
}
