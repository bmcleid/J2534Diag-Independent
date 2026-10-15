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
            ReportNormalEmptyReadsWithoutInventingFrames();
            ReportHardwareOverflowAsExplicitIncomplete();
            IgnoredControlMessagesAreNotInventedBusLoss();
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

        private static void ReportNormalEmptyReadsWithoutInventingFrames()
        {
            var reads = new CanCaptureSummary
            {
                SuccessfulReadCalls = 4,
                TimedOutReadCalls = 120,
                EmptyBufferReadCalls = 18
            };
            var writer = new StringWriter();
            CanCaptureIntegrityReport.Write(writer, "test.csv", Clean(),
                300, 0, 0, false, null, EndTime, reads);
            string result = writer.ToString();
            Check(result.Contains("ReadHealthObserved=True"), "Read health not marked available");
            Check(result.Contains("J2534ReadStatusSuccess=4"), "Successful read count lost");
            Check(result.Contains("J2534ReadStatusTimeout=120"), "Empty timeout read count lost");
            Check(result.Contains("J2534ReadStatusBufferEmpty=18"), "Empty-buffer read count lost");
            Check(result.Contains("J2534BufferOverflowEvents=0"), "Invented hardware overflow");
            Check(result.Contains("J2534OverflowFramesLost=UNKNOWN"), "Loss estimate invented");
            Check(result.Contains("ApplicationWriteComplete=True"),
                "Normal idle timeouts incorrectly failed application-level accounting");

            var unavailable = new StringWriter();
            CanCaptureIntegrityReport.Write(unavailable, "legacy.csv", Clean(),
                300, 0, 0, false, null, EndTime, null);
            Check(unavailable.ToString().Contains("ReadHealthObserved=False"), "Missing read telemetry was synthesized");
            Check(unavailable.ToString().Contains("J2534ReadStatusTimeout=" + Environment.NewLine),
                "Unavailable result should be blank, not zero");
        }

        private static void ReportHardwareOverflowAsExplicitIncomplete()
        {
            var reads = new CanCaptureSummary
            {
                SuccessfulReadCalls = 2,
                DriverReadErrors = 1,
                DriverBufferOverflowEvents = 1
            };
            var writer = new StringWriter();
            CanCaptureIntegrityReport.Write(writer, "test.csv", Clean(),
                300, 0, 0, false, "J2534 read reported BUFFER_OVERFLOW", EndTime, reads);
            string result = writer.ToString();
            Check(result.Contains("J2534DriverReadErrors=1"), "Receive errors not reported");
            Check(result.Contains("J2534BufferOverflowEvents=1"), "Explicit overflow event omitted");
            Check(result.Contains("ApplicationWriteComplete=False"), "Overflow incorrectly considered complete");
            Check(result.Contains("VehicleBusLossless=UNVERIFIED"), "Bus completeness was overclaimed");
            Check(result.Contains("J2534OverflowFramesLost=UNKNOWN"), "Invented number of lost CAN frames");

            var unrecognized = new CanCaptureSummary { OtherStatusReadCalls = 2 };
            var unknown = new StringWriter();
            CanCaptureIntegrityReport.Write(unknown, "test.csv", Clean(),
                1, 0, 0, false, null, EndTime, unrecognized);
            Check(unknown.ToString().Contains("ApplicationWriteComplete=False"),
                "Unrecognized read result incorrectly considered healthy");
        }

        private static void IgnoredControlMessagesAreNotInventedBusLoss()
        {
            var readSummary = new CanCaptureSummary
            {
                IgnoredIndicationRecords = 9,
                SuccessfulReadCalls = 10
            };
            var writer = new StringWriter();
            CanCaptureIntegrityReport.Write(writer, "standalone.csv", Clean(),
                300, 0, 0, false, null, EndTime, readSummary);
            string text = writer.ToString();
            Check(text.Contains("J2534IndicationRecordsIgnored=9"),
                "Ignored J2534 indication count missing from report");
            Check(text.Contains("InvalidReceivedFrames=0"),
                "J2534 indications were mislabelled as invalid CAN payload");
            Check(text.Contains("ApplicationWriteComplete=True"),
                "Expected status/indication data wrongly failed disk completeness");
            Check(text.Contains("VehicleBusLossless=UNVERIFIED"),
                "Application bookkeeping incorrectly asserted vehicle CAN completeness");
            Check(text.Contains("RejectedRecordContentsRetained=NO"),
                "Raw rejection evidence limitation is not disclosed");

            var malformed = new CanCaptureSummary
            {
                RejectedMissingOrShortRecords = 1,
                RejectedOversizeRecords = 2,
                RejectedArbitrationIds = 3
            };
            var incomplete = new StringWriter();
            CanCaptureIntegrityReport.Write(incomplete, "invalid.csv", Clean(),
                300, 6, 0, false, null, EndTime, malformed);
            string bad = incomplete.ToString();
            Check(bad.Contains("InvalidShortOrNullCanRecords=1") &&
                bad.Contains("InvalidOversizeClassicalCanRecords=2") &&
                bad.Contains("InvalidArbitrationIdRecords=3"),
                "Malformed CAN reasons were merged or lost");
            Check(bad.Contains("ApplicationWriteComplete=False"),
                "Malformed CAN input incorrectly labelled complete");
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
