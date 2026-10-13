using System;
using System.Globalization;
using System.IO;

namespace J2534Diag
{
    /// <summary>
    /// Sidecar accounting is intentionally conservative: it can establish that
    /// this process accounted for queued records, NOT all frames on the CAN bus.
    /// </summary>
    public static class CanCaptureIntegrityReport
    {
        public static void Write(
            TextWriter writer,
            string captureFile,
            CanDiskLogStatus status,
            long processedFrames,
            long invalidFrames,
            long sourceOverReturnedFrames,
            bool captureLimitReached,
            string receiveError,
            DateTime finishedAtUtc)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (status == null) throw new ArgumentNullException(nameof(status));
            if (finishedAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Report completion time must be UTC.", nameof(finishedAtUtc));

            bool applicationComplete = status.ApplicationWriteComplete &&
                string.IsNullOrEmpty(receiveError) && invalidFrames == 0 &&
                sourceOverReturnedFrames == 0 && !captureLimitReached;

            WriteField(writer, "Format", "J2534Diag-Capture-Integrity-v1");
            WriteField(writer, "CaptureFile", captureFile);
            WriteField(writer, "FinishedAtUtc", finishedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            WriteField(writer, "FramesProcessedByPump", processedFrames);
            WriteField(writer, "FramesQueuedForDisk", status.Accepted);
            WriteField(writer, "RowsWrittenToWriter", status.Written);
            WriteField(writer, "QueueFullDrops", status.QueueFullDrops);
            WriteField(writer, "RejectedAfterWriterFailure", status.RejectedAfterFailure);
            WriteField(writer, "AcceptedNotWritten", status.AcceptedNotWritten);
            WriteField(writer, "DiskWriterClosed", status.Closed);
            WriteField(writer, "DiskWriterError", status.WriterError);
            WriteField(writer, "InvalidReceivedFrames", invalidFrames);
            WriteField(writer, "SourceOverReturnedFrames", sourceOverReturnedFrames);
            WriteField(writer, "CaptureLimitReached", captureLimitReached);
            WriteField(writer, "ReceiveError", receiveError);
            WriteField(writer, "ApplicationWriteComplete", applicationComplete);
            WriteField(writer, "VehicleBusLossless", "UNVERIFIED");
            WriteField(writer, "TimestampClock", "J2534NativeUnverified;HostUTCSeparate");
        }

        private static void WriteField(TextWriter writer, string key, object value)
        {
            string result = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            writer.WriteLine(key + "=" + result.Replace('\r', ' ').Replace('\n', ' '));
        }
    }
}
