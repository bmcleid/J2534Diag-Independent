using System;
using System.IO;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class CanCaptureCsvTests
    {
        private static int _assertions;

        private static void Assert(bool ok, string reason)
        {
            ++_assertions;
            if (!ok) throw new Exception(reason);
        }

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0) return NativeJ2534InteropSmoke.Run(args);
                RoundTripWithTimestampAndEscapedFields();
                ReadLegacyCapture();
                ReadReorderedHeader();
                NativeMetadataCsvRoundTrip();
                ReadPriorFiveColumnCsv();
                RejectMalformedRows();
                RejectUnterminatedQuotes();
                ExclusiveReceiveOwnership();
                ConcurrentReceiveOwnership();
                int pumpAssertions = CanCapturePumpTests.RunAll();
                int j2534Assertions = J2534CanFrameSourceTests.RunAll();
                int nativeAssertions = NativeCanReadBoundaryTests.RunAll();
                int nativeLifecycleAssertions = NativeOwnedCanSessionTests.RunAll();
                int connectionOwnerAssertions = J2534ConnectionOwnershipTests.RunAll();
                int nativeAbiAssertions = NativeJ2534AbiTests.RunAll();
                int diskAssertions = CanDiskCaptureLoggerTests.RunAll();
                int integrityAssertions = CanCaptureIntegrityReportTests.RunAll();
                int atomicSidecarAssertions = AtomicIntegritySidecarTests.RunAll();
                int pendingReportAssertions = PendingIntegritySidecarTests.RunAll();
                int captureOnlyAssertions = CaptureOnlySafetyTests.RunAll();
                int lifecycleAssertions = ChannelLifecyclePolicyTests.RunAll();
                int classifierAssertions = CanFrameClassifierTests.RunAll();
                int nativeSixColumnAssertions = NativeSixColumnCanImportTests.RunAll();
                int provenanceAssertions = OfflineCaptureProvenanceTests.RunAll();
                Console.WriteLine("PASS: " + (_assertions + pumpAssertions + j2534Assertions + nativeAssertions + nativeLifecycleAssertions + connectionOwnerAssertions + nativeAbiAssertions + diskAssertions + integrityAssertions + atomicSidecarAssertions + pendingReportAssertions + captureOnlyAssertions + lifecycleAssertions + classifierAssertions + nativeSixColumnAssertions + provenanceAssertions) + " assertions across sixty-four CAN capture regression tests.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
        }

        private static void RoundTripWithTimestampAndEscapedFields()
        {
            var writer = new StringWriter();
            CanCaptureCsv.Write(writer, new[]
            {
                new CanCaptureRecord {
                    Index=1, ReceivedAtUtc="2026-10-12T14:00:00.1234567Z",
                    ArbId="0x7EA", ArbIdDescription="TCM, response",
                    Data="03-62-F1-90"
                },
                new CanCaptureRecord {
                    Index=2, ReceivedAtUtc="2026-10-12T14:00:00.1234570Z",
                    ArbId="0x7E8", ArbIdDescription="ECM \"quoted\"",
                    Data="03-41-0C-00"
                }
            });
            string csv = writer.ToString();
            Assert(csv.StartsWith(CanCaptureCsv.Header), "Header has missing columns");
            Assert(csv.Contains("\"TCM, response\""), "Comma must be escaped");
            Assert(csv.Contains("\"ECM \"\"quoted\"\"\""), "Quotes must be escaped");
            var records = CanCaptureCsv.Read(new StringReader(csv));
            Assert(records.Count == 2, "Lost duplicate/filtered rows");
            Assert(records[0].Index == 1 && records[1].Index == 2, "Wrong frame order");
            Assert(records[0].ReceivedAtUtc == "2026-10-12T14:00:00.1234567Z", "Timestamp changed");
            Assert(records[0].ArbId == "0x7EA", "Arbitration ID changed");
            Assert(records[0].ArbIdDescription == "TCM, response", "Comma round-trip failed");
            Assert(records[1].ArbIdDescription == "ECM \"quoted\"", "Escaped quote round-trip failed");
        }

        private static void ReadLegacyCapture()
        {
            var csv = "Index,ArbId,Data\n1,\"0x7EA\",\"03-62-11-22\"\n2,\"0x7EA\",\"03-62-11-22\"\n";
            var result = CanCaptureCsv.Read(new StringReader(csv));
            Assert(result.Count == 2, "Legacy duplicates must be retained");
            Assert(result[0].ReceivedAtUtc == "", "Unknown legacy timestamp must remain blank");
            Assert(result[1].Index == 2, "Legacy index wrong");
        }

        private static void ReadReorderedHeader()
        {
            var csv = "\uFEFFData,ArbId Description,Index,ArbId,ReceivedAtUtc\nAA-BB,\"ECM, engine\",7,0x7E8,2026-10-12T14:00:01Z\n";
            var result = CanCaptureCsv.Read(new StringReader(csv));
            Assert(result.Count == 1, "Reordered header count");
            Assert(result[0].ArbId == "0x7E8", "Reordered ID");
            Assert(result[0].ArbIdDescription == "ECM, engine", "Reordered quoted description");
            Assert(result[0].ReceivedAtUtc == "2026-10-12T14:00:01Z", "Reordered timestamp");
        }

        private static void NativeMetadataCsvRoundTrip()
        {
            var source = new[]
            {
                new CanCaptureRecord
                {
                    Index = 9, ReceivedAtUtc = "2026-10-13T16:00:00.0000000Z",
                    ArbId = "0x7EA", ArbIdDescription = "TCM, raw",
                    Data = "03-41-0C-00", J2534TimestampRaw = "4294967295",
                    J2534RxStatusHex = "0x80000100", J2534ReadResult = "TIMEOUT"
                },
                new CanCaptureRecord
                {
                    Index = 10, ReceivedAtUtc = "2026-10-13T16:00:00.0010000Z",
                    ArbId = "0x7E8", Data = "01-00",
                    J2534TimestampRaw = "0", J2534RxStatusHex = "0x00000000",
                    J2534ReadResult = "STATUS_NOERROR"
                }
            };
            var writer = new StringWriter();
            CanCaptureCsv.Write(writer, source);
            string csv = writer.ToString();
            Assert(csv.StartsWith(CanCaptureCsv.Header), "Native CSV header invalid");
            var records = CanCaptureCsv.Read(new StringReader(csv));
            Assert(records.Count == 2, "Native CSV row count changed");
            Assert(records[0].J2534TimestampRaw == "4294967295", "Native timestamp truncated");
            Assert(records[0].J2534RxStatusHex == "0x80000100", "High receive flag bit changed");
            Assert(records[0].J2534ReadResult == "TIMEOUT", "Receive timeout-with-data lost");
            Assert(records[0].ArbIdDescription == "TCM, raw", "Quoted description changed");
            Assert(records[1].J2534TimestampRaw == "0", "Native timestamp zero lost");
            Assert(records[1].J2534RxStatusHex == "0x00000000", "Receive flags zero lost");
            Assert(records[1].J2534ReadResult == "STATUS_NOERROR", "Read result success changed");
        }

        private static void ReadPriorFiveColumnCsv()
        {
            var csv = "Index,ReceivedAtUtc,ArbId,ArbId Description,Data\n" +
                      "1,2026-10-12T14:00:00Z,0x7EA,TCM,03-41-0C-00\n";
            var records = CanCaptureCsv.Read(new StringReader(csv));
            Assert(records.Count == 1, "Prior five-column CSV rejected");
            Assert(records[0].ReceivedAtUtc == "2026-10-12T14:00:00Z", "Prior host timestamp lost");
            Assert(records[0].J2534TimestampRaw == "", "Missing native timestamp invented");
            Assert(records[0].J2534RxStatusHex == "", "Missing receive flags invented");
            Assert(records[0].J2534ReadResult == "", "Missing read result invented");
        }

        private static void RejectMalformedRows()
        {
            try
            {
                CanCaptureCsv.Read(new StringReader("Index,ArbId,Data\nX,0x7EA,01-02\n"));
                throw new Exception("Did not reject invalid index");
            }
            catch (FormatException) { }
            ++_assertions;
        }

        private static void RejectUnterminatedQuotes()
        {
            try
            {
                CanCaptureCsv.Read(new StringReader("Index,ArbId,Data\n1,0x7EA,\"broken\n"));
                throw new Exception("Did not reject invalid quoted field");
            }
            catch (FormatException) { }
            ++_assertions;
        }
        private static void ExclusiveReceiveOwnership()
        {
            var gate = new ReceiveChannelGate();
            Assert(gate.TryAcquire("CAN"), "First reader must acquire channel");
            Assert(gate.CurrentOwner == "CAN", "Gate must retain owner");
            Assert(!gate.TryAcquire("MISFIRE"), "Second reader must be blocked");
            try
            {
                gate.Release("MISFIRE");
                throw new Exception("Wrong owner release must throw");
            }
            catch (InvalidOperationException) { }
            ++_assertions;
            Assert(gate.CurrentOwner == "CAN", "Wrong release must not clear owner");
            gate.Release("CAN");
            Assert(gate.TryAcquire("MISFIRE"), "Release must allow another reader");
            gate.Release("MISFIRE");
            Assert(gate.CurrentOwner == null, "Gate should be empty after release");
        }

        private static void ConcurrentReceiveOwnership()
        {
            var gate = new ReceiveChannelGate();
            int accepted = 0;
            var threads = new System.Threading.Thread[24];
            var start = new System.Threading.ManualResetEventSlim(false);
            for (int i = 0; i < threads.Length; i++)
            {
                int local = i;
                threads[i] = new System.Threading.Thread(() =>
                {
                    start.Wait();
                    if (gate.TryAcquire("Reader" + local))
                        System.Threading.Interlocked.Increment(ref accepted);
                });
                threads[i].Start();
            }
            start.Set();
            foreach (var thread in threads) thread.Join();
            Assert(accepted == 1, "Exactly one simultaneous reader should acquire the channel");
        }

    }
}
