using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class NativeGuiPreviewTests
    {
        private static int checks;
        private static void Check(bool ok, string msg)
        {
            ++checks;
            if (!ok) throw new Exception(msg);
        }

        private sealed class FakeSource : ICanFrameBatchSource, ICanReadResultSource
        {
            public int Calls, Requested, Timeout;
            public bool Overflow;
            public string LastReadResult { get; private set; }
            public IReadOnlyList<CanReceivedFrame> ReadBatch(int max, int timeoutMs)
            {
                ++Calls;
                Requested = max;
                Timeout = timeoutMs;
                LastReadResult = Overflow ? "BUFFER_OVERFLOW" : "TIMEOUT";
                var list = new[]
                {
                    new CanReceivedFrame {
                        Data = new byte[] { 0, 0, 1, 0xF1, 0x01 },
                        TimestampRaw = 801, RxStatusRaw = 0,
                        ReadResult = LastReadResult
                    },
                    new CanReceivedFrame {
                        Data = new byte[] { 0, 0, 1, 0xF1, 0x02 },
                        TimestampRaw = 802, RxStatusRaw = 0,
                        ReadResult = LastReadResult
                    }
                };
                if (Overflow)
                    throw new CanReceiveBufferOverflowException("Synthetic 0x12",
                        new InvalidOperationException("no vehicle"), list);
                return list;
            }
        }

        public static int RunAll()
        {
            NativePumpUsesOneAppSizeTimeoutAndSameDiskWriter();
            PartialOverflowStillIncompleteWithDiskData();
            RejectMissingOrWrongBitnessDllBeforeLoading();
            return checks;
        }

        private static void NativePumpUsesOneAppSizeTimeoutAndSameDiskWriter()
        {
            var source = new FakeSource();
            var sink = new StringWriter();
            var logger = new CanDiskCaptureLogger(sink, 16);
            var pump = new CanCapturePump(source, int.MaxValue,
                () => new DateTime(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc),
                null, 100, 250);
            using (var cancel = new CancellationTokenSource())
            {
                var summary = pump.Run(cancel.Token, rec =>
                {
                    Check(logger.TryEnqueue(rec), "New native frame cannot use existing disk writer");
                    cancel.Cancel();
                });
                Check(summary.StoredFrames == 2 && summary.DriverBufferOverflowEvents == 0,
                    "Native success did not preserve frames");
            }
            Check(source.Requested == 100 && source.Timeout == 250 && source.Calls == 1,
                "Native pump did not use 100/250 read strategy");
            Check(logger.CompleteAndWait(5000), "Native writer did not close");
            var status = logger.GetStatus();
            Check(status.Accepted == 2 && status.Written == 2 &&
                status.ApplicationWriteComplete, "Native disk data missing");
            Check(sink.ToString().Contains("0x1F1") &&
                sink.ToString().Contains("802"), "CSV native values missing");
        }

        private static void PartialOverflowStillIncompleteWithDiskData()
        {
            var source = new FakeSource { Overflow = true };
            var sink = new StringWriter();
            var logger = new CanDiskCaptureLogger(sink, 16);
            var pump = new CanCapturePump(source, int.MaxValue,
                () => new DateTime(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc),
                null, 100, 250);
            bool threw = false;
            try
            {
                pump.Run(CancellationToken.None, rec =>
                {
                    Check(logger.TryEnqueue(rec), "Partial 0x12 frame could not be queued");
                });
            }
            catch (CanReceiveBufferOverflowException) { threw = true; }
            Check(threw && pump.LastSummary.StoredFrames == 2 &&
                pump.LastSummary.DriverBufferOverflowEvents == 1,
                "Native 0x12 was hidden or partial frames discarded");
            Check(logger.CompleteAndWait(5000), "Overflow writer did not close");
            var status = logger.GetStatus();
            Check(status.Accepted == 2 && status.Written == 2, "Overflow rows not persisted");
            var report = new StringWriter();
            CanCaptureIntegrityReport.Write(report, "synthetic-native.csv", status,
                2, 0, 0, false, "Native 0x12 BUFFER_OVERFLOW",
                new DateTime(2026, 10, 26, 18, 0, 1, DateTimeKind.Utc),
                pump.LastSummary);
            Check(report.ToString().Contains("ApplicationWriteComplete=False") &&
                report.ToString().Contains("VehicleBusLossless=UNVERIFIED") &&
                report.ToString().Contains("J2534BufferOverflowEvents=1") &&
                report.ToString().Contains("J2534OverflowFramesAcceptedByPump=2"),
                "Native overflow and lost-data caveat missing from integrity sidecar");
        }

        private static void RejectMissingOrWrongBitnessDllBeforeLoading()
        {
            bool missing = false;
            try { NativeJ2534DriverCheck.Inspect(
                Path.Combine(Path.GetTempPath(), "nonexistent-srx-j2534.dll")); }
            catch (FileNotFoundException) { missing = true; }
            Check(missing, "Missing driver not rejected before connect");

            string temp = Path.Combine(Path.GetTempPath(),
                "srx-x86-native-check-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                var fake = new byte[512];
                fake[0] = (byte)'M'; fake[1] = (byte)'Z';
                fake[0x3c] = 0x80; // PE offset
                fake[0x80] = (byte)'P'; fake[0x81] = (byte)'E';
                fake[0x84] = 0x4c; fake[0x85] = 0x01; // 0x14c x86
                File.WriteAllBytes(temp, fake);
                bool rejected = false;
                try { NativeJ2534DriverCheck.Inspect(temp); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "32-bit DLL allowed into x64 native preview");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
