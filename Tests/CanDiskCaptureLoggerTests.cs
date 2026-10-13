using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class CanDiskCaptureLoggerTests
    {
        private static int _checks;

        private static void Check(bool ok, string reason)
        {
            ++_checks;
            if (!ok) throw new Exception(reason);
        }

        private static CanCaptureRecord Record(long index)
        {
            return new CanCaptureRecord
            {
                Index = index,
                ReceivedAtUtc = "2026-10-13T17:00:00.0000000Z",
                ArbId = index % 2 == 0 ? "0x7EA" : "0x7E8",
                ArbIdDescription = "Simulated; not vehicle evidence",
                Data = "03-41-0C-00",
                J2534TimestampRaw = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                J2534RxStatusHex = "0x00000000",
                J2534ReadResult = "STATUS_NOERROR"
            };
        }

        private sealed class BlockHeaderWriter : StringWriter
        {
            public readonly ManualResetEventSlim EnteredHeader = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim ReleaseHeader = new ManualResetEventSlim(false);

            public override void WriteLine(string value)
            {
                if (value == CanCaptureCsv.Header)
                {
                    EnteredHeader.Set();
                    if (!ReleaseHeader.Wait(10000))
                        throw new TimeoutException("Test did not release header write.");
                }
                base.WriteLine(value);
            }
        }

        private sealed class FailingRecordWriter : StringWriter
        {
            public override void WriteLine(string value)
            {
                if (value != CanCaptureCsv.Header)
                    throw new IOException("Simulated disk full");
                base.WriteLine(value);
            }
        }

        private sealed class FailingFlushWriter : StringWriter
        {
            public override void Flush()
            {
                throw new IOException("Simulated final flush failure");
            }
        }

        public static int RunAll()
        {
            StreamRowsToFileAndReadBack();
            MoreThanTenThousandFramesReachDiskWithoutLargeUIHistory();
            QueueFullIsReportedWithoutBlockingCapture();
            DiskWriteFailureIsNotCalledComplete();
            FlushFailureIsNotCalledComplete();
            CannotEnqueueAfterFinish();
            RetriedFinishAndNoOverwrite();
            return _checks;
        }

        private sealed class SyntheticCanSource : ICanFrameBatchSource
        {
            public int Remaining;
            public IReadOnlyList<CanReceivedFrame> ReadBatch(int requestedMaximum, int timeoutMilliseconds)
            {
                int length = Math.Min(Remaining, requestedMaximum);
                var frames = new CanReceivedFrame[length];
                for (int i = 0; i < length; i++)
                {
                    frames[i] = new CanReceivedFrame
                    {
                        Data = new byte[] { 0x00, 0x00, 0x07, 0xEA, 0x03, 0x62, 0xF1, 0x90 },
                        TimestampRaw = (uint)(Remaining - i),
                        RxStatusRaw = 0,
                        ReadResult = "STATUS_NOERROR"
                    };
                }
                Remaining -= length;
                return frames;
            }
        }

        private static void MoreThanTenThousandFramesReachDiskWithoutLargeUIHistory()
        {
            const int frameCount = 12025;
            var source = new SyntheticCanSource { Remaining = frameCount };
            var output = new StringWriter();
            // Deliberately size this bounded test queue above the input count:
            // verifies streaming beyond the old 10k cap without an accidental
            // scheduler-dependent queue-drop failure in a CI environment.
            var logger = new CanDiskCaptureLogger(output, 13000);
            var pump = new CanCapturePump(source, frameCount,
                () => new DateTime(2026, 10, 13, 17, 0, 0, DateTimeKind.Utc),
                id => "");
            var capture = pump.Run(CancellationToken.None, row =>
            {
                if (!logger.TryEnqueue(row))
                    throw new IOException("Unexpected queue overflow in synthetic large-capture test.");
            });
            Check(capture.StoredFrames == frameCount, "Pump stopped at old 10,000 record cap");
            Check(source.Remaining == 0, "Source frames were not fully consumed");
            Check(logger.CompleteAndWait(10000), "Large-capture disk writer timeout");
            var status = logger.GetStatus();
            Check(status.Accepted == frameCount && status.Written == frameCount, "Accepted records were lost in disk writer");
            Check(status.ApplicationWriteComplete, "Clean large-capture writer reported incomplete");
            var records = CanCaptureCsv.Read(new StringReader(output.ToString()));
            Check(records.Count == frameCount, "Large on-disk CSV did not retain all 12,025 rows");
            Check(records[0].Index == 1 && records[frameCount - 1].Index == frameCount,
                "Capture sequence discontinuity over 10,000 rows");
        }

        private static void StreamRowsToFileAndReadBack()
        {
            var folder = Path.Combine(Path.GetTempPath(), "J2534Diag-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "capture.csv");
            try
            {
                var logger = new CanDiskCaptureLogger(path, 128);
                int accepted = 0;
                for (int i = 1; i <= 3000; ++i)
                {
                    if (logger.TryEnqueue(Record(i))) accepted++;
                }
                Check(logger.CompleteAndWait(10000), "Disk writer did not stop");
                var status = logger.GetStatus();
                Check(status.Closed, "Disk stream not closed");
                Check(status.WriterError == null, "Unexpected filesystem failure");
                Check(status.Accepted == accepted, "Accepted count mismatch");
                Check(status.Accepted + status.QueueFullDrops == 3000, "Unaccounted input records");
                Check(status.Written == accepted, "Accepted rows not flushed at stop");
                Check(status.AcceptedNotWritten == 0, "Flushed rows reported lost");
                Check(status.ApplicationWriteComplete == (status.QueueFullDrops == 0), "Completeness flag incorrect");
                using (var reader = new StreamReader(path))
                {
                    var rows = CanCaptureCsv.Read(reader);
                    Check(rows.Count == accepted, "On-disk row count differs from written rows");
                    for (int i = 0; i < rows.Count; ++i)
                    {
                        Check(rows[i].Index >= 1, "Invalid serialized sequence index");
                        if (i > 0) Check(rows[i].Index > rows[i - 1].Index, "Rows reordered");
                    }
                    Check(rows[0].J2534TimestampRaw != "", "Native metadata not persisted");
                }
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void QueueFullIsReportedWithoutBlockingCapture()
        {
            var writer = new BlockHeaderWriter();
            var logger = new CanDiskCaptureLogger(writer, 2);
            try
            {
                Check(writer.EnteredHeader.Wait(3000), "Writer thread did not begin header write");
                Check(logger.TryEnqueue(Record(1)), "Queue first add failed");
                Check(logger.TryEnqueue(Record(2)), "Queue second add failed");
                Check(!logger.TryEnqueue(Record(3)), "Full queue did not reject promptly");
            }
            finally
            {
                writer.ReleaseHeader.Set();
            }
            Check(logger.CompleteAndWait(3000), "Blocked writer did not stop");
            var status = logger.GetStatus();
            Check(status.QueueFullDrops == 1, "Dropped count not recorded");
            Check(status.Accepted == 2 && status.Written == 2, "Accepted rows not drained");
            Check(status.AcceptedNotWritten == 0, "Drained rows incorrectly marked lost");
            Check(!status.ApplicationWriteComplete, "Overflow incorrectly claimed complete");
        }

        private static void DiskWriteFailureIsNotCalledComplete()
        {
            var writer = new FailingRecordWriter();
            var logger = new CanDiskCaptureLogger(writer, 16);
            Check(logger.TryEnqueue(Record(1)), "First record enqueue failed");
            Check(logger.TryEnqueue(Record(2)), "Second record enqueue failed");
            Check(logger.CompleteAndWait(3000), "Erroring writer failed to exit");
            var status = logger.GetStatus();
            Check(status.Closed, "Failed writer did not close");
            Check(status.WriterError.Contains("Simulated disk full"), "Write failure swallowed");
            Check(status.AcceptedNotWritten == 2, "Unwritten accepted rows not identified");
            Check(!status.ApplicationWriteComplete, "Disk write error incorrectly called complete");
        }

        private static void FlushFailureIsNotCalledComplete()
        {
            var writer = new FailingFlushWriter();
            var logger = new CanDiskCaptureLogger(writer, 16);
            Check(logger.TryEnqueue(Record(1)), "Enqueue failed");
            Check(logger.CompleteAndWait(3000), "Flush error writer failed to exit");
            var status = logger.GetStatus();
            Check(status.WriterError.Contains("Simulated final flush failure"), "Flush error lost");
            Check(status.Written == 1, "Writer row counter unexpectedly changed");
            Check(!status.ApplicationWriteComplete, "Flush failure incorrectly called complete");
        }

        private static void CannotEnqueueAfterFinish()
        {
            var logger = new CanDiskCaptureLogger(new StringWriter(), 8);
            Check(logger.TryEnqueue(Record(1)), "Initial enqueue failed");
            Check(logger.CompleteAndWait(3000), "Finish timed out");
            Check(!logger.TryEnqueue(Record(2)), "Accepted row after finish");
            var status = logger.GetStatus();
            Check(status.RejectedAfterFailure == 1, "Post-stop reject missing");
            Check(!status.ApplicationWriteComplete, "Unexpected post-stop record called complete");
        }

        private static void RetriedFinishAndNoOverwrite()
        {
            var writer = new BlockHeaderWriter();
            var logger = new CanDiskCaptureLogger(writer, 2);
            Check(writer.EnteredHeader.Wait(3000), "Blocked writer was not started");
            Check(!logger.CompleteAndWait(1), "Finish falsely succeeded while blocked");
            Check(!logger.GetStatus().Closed, "Blocked writer marked closed");
            writer.ReleaseHeader.Set();
            Check(logger.CompleteAndWait(3000), "Retried finish failed");
            Check(logger.GetStatus().Closed, "Writer did not close on retry");

            var folder = Path.Combine(Path.GetTempPath(), "J2534Diag-exclusive-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "capture.csv");
            try
            {
                File.WriteAllText(path, "keep-existing");
                try
                {
                    new CanDiskCaptureLogger(path);
                    throw new Exception("Logger overwrote an existing capture file");
                }
                catch (IOException) { ++_checks; }
                Check(File.ReadAllText(path) == "keep-existing", "Pre-existing file modified");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
