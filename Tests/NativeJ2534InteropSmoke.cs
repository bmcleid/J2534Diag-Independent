using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using J2534Diag;

namespace J2534Diag.Tests
{
    // An optional CI-only integration test. Normal J2534Diag startup NEVER
    // invokes this class; only --ci-native-sim on a GitHub-hosted runner does.
    internal static class NativeJ2534InteropSmoke
    {
        private static int checks;
        private static void Check(bool ok, string reason)
        {
            checks++;
            if (!ok) throw new Exception(reason);
        }
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode,
            EntryPoint="LoadLibraryW", SetLastError=true)]
        private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr handle, string name);
        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint SyntheticIdentity();

        public static int Run(string[] args)
        {
            try
            {
                if (args.Length != 2 || args[0] != "--ci-native-sim" ||
                    Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
                    throw new InvalidOperationException("Native simulator permitted only by explicit CI-only command.");
                if (!Environment.Is64BitProcess)
                    throw new InvalidOperationException("Simulator requires a 64-bit test process.");
                string path = Path.GetFullPath(args[1]);
                if (Path.GetFileName(path) != "mock-j2534.dll" || !File.Exists(path))
                    throw new FileNotFoundException("Expected compiled synthetic J2534 DLL only.", path);

                // Prove this is our synthetic DLL, not a TOPDON driver,
                // before invoking any J2534 PassThru* export.
                IntPtr dll = LoadLibrary(path);
                if (dll == IntPtr.Zero)
                    throw new InvalidOperationException("Cannot load synthetic DLL.");
                try
                {
                    IntPtr address = GetProcAddress(dll, "SrxSimulatorIdentity");
                    if (address == IntPtr.Zero)
                        throw new InvalidOperationException("Synthetic identity marker missing.");
                    var identify = (SyntheticIdentity)Marshal.GetDelegateForFunctionPointer(
                        address, typeof(SyntheticIdentity));
                    if (identify() != 0x53525853)
                        throw new InvalidOperationException("Synthetic identity value wrong.");
                }
                finally { FreeLibrary(dll); }

                foreach (string scenario in new[]{"success","timeout","overflow",
                    "zero","stale","badcount","empty"})
                    ReceiveScenario(path,scenario);
                foreach (string scenario in new[]{"openfail","connectfail","filterfail"})
                    FailureToStart(path,scenario);
                CleanupWithRetry(path);
                StopFilterFailure(path);
                Console.WriteLine("PASS: " + checks +
                    " isolated real-DLL StdCall J2534 synthetic assertions; NO TOPDON/VEHICLE.");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("FAIL: CI synthetic native J2534 API contract: " + e);
                return 1;
            }
            finally { Environment.SetEnvironmentVariable("SRX_NATIVE_SIM_CASE",null); }
        }

        private static void ReceiveScenario(string path, string scenario)
        {
            Environment.SetEnvironmentVariable("SRX_NATIVE_SIM_CASE",scenario);
            using (var api = new NativeJ2534StdCallApi(path))
            using (var session = new NativeOwnedCanSession(api, () => true))
            {
                session.Start(true);
                var boundary = new NativeCanReadBoundary(session);
                if (scenario == "success" || scenario == "timeout" || scenario == "empty")
                {
                    var received = boundary.ReadBatch(100,5);
                    int expected = scenario=="success" ? 2 : scenario=="timeout" ? 1 : 0;
                    Check(received.Count==expected,scenario+" actual native count wrong");
                    if (expected>0)
                    {
                        Check(received[0].TimestampRaw==201 &&
                            received[0].Data.Length==5 &&
                            received[0].Data[4]==0xA1,scenario+" native message ABI wrong");
                        Check(received[0].ReadResult == (scenario=="timeout" ?
                            "TIMEOUT":"STATUS_NOERROR"),scenario+" read status lost");
                    }
                }
                else
                {
                    var pump = new CanCapturePump(boundary,10000,
                        () => new DateTime(2026,10,26,17,0,0,DateTimeKind.Utc),null);
                    var saved = new List<CanCaptureRecord>();
                    bool threw=false;
                    try { pump.Run(System.Threading.CancellationToken.None,saved.Add); }
                    catch (CanReceiveBufferOverflowException) { threw=true; }
                    Check(threw && pump.LastSummary.DriverBufferOverflowEvents==1,
                        scenario+" true native 0x12 not propagated");
                    int expected = scenario=="overflow" ? 2 : 0;
                    Check(saved.Count==expected &&
                        pump.LastSummary.OverflowFramesAcceptedByPump==expected,
                        scenario+" pNumMsgs returned frame count not trusted correctly");
                    if (expected>0)
                        Check(saved[1].J2534TimestampRaw=="202" &&
                            saved[1].J2534ReadResult=="BUFFER_OVERFLOW" &&
                            saved[1].Data=="A2",
                            "Partial native overflow message metadata was lost");
                }
                session.Stop();
                Check(!session.IsReceiving,scenario+" native channel remained active");
            }
        }

        private static void FailureToStart(string path, string scenario)
        {
            Environment.SetEnvironmentVariable("SRX_NATIVE_SIM_CASE",scenario);
            using (var api = new NativeJ2534StdCallApi(path))
            using (var session = new NativeOwnedCanSession(api, () => true))
            {
                bool threw = false;
                try { session.Start(true); }
                catch (InvalidOperationException) { threw = true; }
                Check(threw && !session.IsReceiving,
                    scenario+" native startup failure or cleanup was concealed");
            }
        }

        private static void CleanupWithRetry(string path)
        {
            Environment.SetEnvironmentVariable("SRX_NATIVE_SIM_CASE","success");
            using (var api = new NativeJ2534StdCallApi(path))
            using (var session = new NativeOwnedCanSession(api, () => true))
            {
                session.Start(true);
                Environment.SetEnvironmentVariable("SRX_NATIVE_SIM_CASE","closefail");
                bool threw=false;
                try {session.Stop();}
                catch (InvalidOperationException){threw=true;}
                Check(threw,"Native Close failure incorrectly claimed successful cleanup");
                Environment.SetEnvironmentVariable("SRX_NATIVE_SIM_CASE","success");
                session.Stop();
                Check(!session.IsReceiving,"Native Close cleanup retry failed");
            }
        }

        private static void StopFilterFailure(string path)
        {
            Environment.SetEnvironmentVariable("SRX_NATIVE_SIM_CASE","success");
            using (var api = new NativeJ2534StdCallApi(path))
            using (var session = new NativeOwnedCanSession(api, () => true))
            {
                session.Start(true);
                Environment.SetEnvironmentVariable("SRX_NATIVE_SIM_CASE","stopfilterfail");
                bool threw=false;
                try {session.Stop();}
                catch (InvalidOperationException){threw=true;}
                Check(threw,"Native StopFilter failure was not reported");
                Environment.SetEnvironmentVariable("SRX_NATIVE_SIM_CASE","success");
                session.Stop();
            }
        }
    }
}
