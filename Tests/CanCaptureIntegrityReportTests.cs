using System;
using System.IO;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class CanCaptureIntegrityReportTests
    {
        private static int _checks;
        private static void Check(bool condition, string explanation)
        {
            ++_checks;
            if (!condition) throw new Exception(explanation);
        }

        public static int RunAll()
        {
            CleanFlushStillDoesNotProveBusLosslessness();
            DroppedQueueAndWriterErrorAreVisible();
            ReceiveErrorsAndMalformedRowsMakeItIncomplete();
            NonUtcReportTimeRejected();
            return _checks;
        }

        private static readonly DateTime EndTime =
            new DateTime(2026, 10, 13, 18, 0, 0, DateTimeKind.Utc);

        private static string Render(CanDiskLogStatus status, long invalid = 0,
            long overReturned = 0, bool limit = false, string driverError = null)
        {
            var writer = new StringWriter();
            CanCaptureIntegrityReport.Write(writer, @"C:\Captures\sample.csv", status,
                status.Written, invalid, overReturned, limit, driverError, EndTime);
            return writer.ToString();
        }

        private static CanDiskLogStatus Clean()
        {
            return new CanDiskLogStatus { Accepted = 300, Written = 300, Closed = true };
        }

        private static void CleanFlushStillDoesNotProveBusLosslessness()
        {
            var report = Render(Clean());
            Check(report.Contains("ApplicationWriteComplete=True"), "Clean application flush not reported");
            Check(report.Contains("VehicleBusLossless=UNVERIFIED"), "Missing mandatory hardware uncertainty");
            Check(report.Contains("RowsWrittenToWriter=300"), "Persisted row count missing");
            Check(report.Contains("QueueFullDrops=0"), "Dropped row count missing");
            Check(report.Contains("TimestampClock=J2534NativeUnverified;HostUTCSeparate"),
                "Timestamp timebase was overstated");
        }

        private static void DroppedQueueAndWriterErrorAreVisible()
        {
            var status = new CanDiskLogStatus
            {
                Accepted = 5, Written = 3, AcceptedNotWritten = 2,
                QueueFullDrops = 1, RejectedAfterFailure = 1, Closed = true,
                WriterError = "Disk full\nmore detail"
            };
            string report = Render(status);
            Check(report.Contains("ApplicationWriteComplete=False"), "Incomplete file incorrectly marked clean");
            Check(report.Contains("QueueFullDrops=1"), "Queue overflow not reported");
            Check(report.Contains("AcceptedNotWritten=2"), "Lost writer rows not reported");
            Check(report.Contains("DiskWriterError=Disk full more detail"), "Writer failure not flattened");
        }

        private static void ReceiveErrorsAndMalformedRowsMakeItIncomplete()
        {
            var report = Render(Clean(), invalid: 2, overReturned: 3, limit: true,
                driverError: "Driver went away\r\nAdapter reset");
            Check(report.Contains("InvalidReceivedFrames=2"), "Invalid record count missing");
            Check(report.Contains("SourceOverReturnedFrames=3"), "Over-return missing");
            Check(report.Contains("CaptureLimitReached=True"), "Capacity issue not reported");
            Check(report.Contains("ReceiveError=Driver went away  Adapter reset"), "Driver error not flattened");
            Check(report.Contains("ApplicationWriteComplete=False"), "Unreliable session marked complete");
        }

        private static void NonUtcReportTimeRejected()
        {
            try
            {
                CanCaptureIntegrityReport.Write(new StringWriter(), "sample.csv", Clean(),
                    1, 0, 0, false, "", new DateTime(2026, 10, 13, 14, 0, 0, DateTimeKind.Local));
                throw new Exception("Report accepted a non-UTC completion time");
            }
            catch (ArgumentException) { ++_checks; }
        }
    }
}
