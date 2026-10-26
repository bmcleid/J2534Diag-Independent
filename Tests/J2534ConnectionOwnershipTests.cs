using System;
using System.Collections.Generic;
using System.Threading;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class J2534ConnectionOwnershipTests
    {
        private static int checks;
        private static void Check(bool ok, string why)
        {
            ++checks;
            if (!ok) throw new Exception(why);
        }

        private sealed class FakeCanApi : INativeJ2534CanApi
        {
            public int Calls;
            public bool FailClose;
            public uint Open(out uint device) { ++Calls; device = 11; return 0; }
            public uint ConnectCan500k(uint device, out uint channel)
            { ++Calls; channel = 22; return 0; }
            public uint StartWildcardPassFilter(uint channel, out uint filter)
            { ++Calls; filter = 33; return 0; }
            public NativeCanReadResult ReadCan(uint channel, int max, int timeout)
            { ++Calls; return new NativeCanReadResult(0x10, 0, null); }
            public uint StopFilter(uint channel, uint filter) { ++Calls; return 0; }
            public uint Disconnect(uint channel) { ++Calls; return 0; }
            public uint Close(uint device) { ++Calls; return FailClose ? 7U : 0U; }
        }

        public static int RunAll()
        {
            DenyNativeWhileManagedOwns();
            DenyManagedWhileNativeOwns();
            RetainAcrossManagedReconnect();
            RetainAfterFailedNativeClose();
            RejectForeignRelease();
            ExactlyOneRacingOwner();
            return checks;
        }

        private static void DenyNativeWhileManagedOwns()
        {
            var managed = new object();
            Check(J2534ConnectionOwnership.TryAcquire(managed, "Managed"),
                "Managed owner could not acquire");
            try
            {
                var api = new FakeCanApi();
                using (var native = new NativeOwnedCanSession(api, () => true))
                {
                    bool denied = false;
                    try { native.Start(true); }
                    catch (InvalidOperationException) { denied = true; }
                    Check(denied && api.Calls == 0,
                        "Native opened device while managed held the shared lease");
                }
            }
            finally { J2534ConnectionOwnership.Release(managed); }
        }

        private static void DenyManagedWhileNativeOwns()
        {
            var managed = new object();
            using (var native = new NativeOwnedCanSession(new FakeCanApi(), () => true))
            {
                native.Start(true);
                Check(J2534ConnectionOwnership.CurrentOwner == "Native J2534 receiver",
                    "Native lease missing");
                Check(!J2534ConnectionOwnership.TryAcquire(managed, "Managed"),
                    "Managed opened over native session");
                native.Stop();
            }
            Check(J2534ConnectionOwnership.TryAcquire(managed, "Managed"),
                "Native clean close did not release connection");
            J2534ConnectionOwnership.Release(managed);
        }

        private static void RetainAcrossManagedReconnect()
        {
            var managed = new object();
            var native = new object();
            Check(J2534ConnectionOwnership.TryAcquire(managed, "Managed"),
                "Managed acquisition failed");
            try
            {
                Check(J2534ConnectionOwnership.TryAcquire(managed, "Managed"),
                    "Managed reconnect did not retain its lease");
                Check(!J2534ConnectionOwnership.TryAcquire(native, "Native"),
                    "Native acquired during managed reconnect");
            }
            finally { J2534ConnectionOwnership.Release(managed); }
        }

        private static void RetainAfterFailedNativeClose()
        {
            var api = new FakeCanApi { FailClose = true };
            var managed = new object();
            using (var native = new NativeOwnedCanSession(api, () => true))
            {
                native.Start(true);
                bool failed = false;
                try { native.Stop(); }
                catch (InvalidOperationException) { failed = true; }
                Check(failed && !J2534ConnectionOwnership.TryAcquire(managed, "Managed"),
                    "Failed native close released shared lease");
                api.FailClose = false;
                native.Stop();
                Check(J2534ConnectionOwnership.TryAcquire(managed, "Managed"),
                    "Successful retry did not free native connection");
                J2534ConnectionOwnership.Release(managed);
            }
        }

        private static void RejectForeignRelease()
        {
            var owner = new object();
            Check(J2534ConnectionOwnership.TryAcquire(owner, "Managed"),
                "Owner unavailable");
            try
            {
                bool denied = false;
                try { J2534ConnectionOwnership.Release(new object()); }
                catch (InvalidOperationException) { denied = true; }
                Check(denied && J2534ConnectionOwnership.CurrentOwner == "Managed",
                    "Foreign token released J2534 connection");
            }
            finally { J2534ConnectionOwnership.Release(owner); }
        }

        private static void ExactlyOneRacingOwner()
        {
            var first = new object();
            var second = new object();
            bool a = false, b = false;
            using (var ready = new ManualResetEventSlim(false))
            {
                var t1 = new Thread(() => { ready.Wait(); a = J2534ConnectionOwnership.TryAcquire(first, "Managed"); });
                var t2 = new Thread(() => { ready.Wait(); b = J2534ConnectionOwnership.TryAcquire(second, "Native"); });
                t1.Start(); t2.Start(); ready.Set();
                Check(t1.Join(3000) && t2.Join(3000),
                    "Connection owner deadlocked");
            }
            Check(a != b, "Both owners won or neither won race");
            J2534ConnectionOwnership.Release(a ? first : second);
            Check(J2534ConnectionOwnership.CurrentOwner == null, "Lease leaked after race");
        }
    }
}
