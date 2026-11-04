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
            DateTime finishedAtUtc,
            CanCaptureSummary readSummary = null)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (status == null) throw new ArgumentNullException(nameof(status));
            if (finishedAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Report completion time must be UTC.", nameof(finishedAtUtc));

            bool applicationComplete = status.ApplicationWriteComplete &&
                string.IsNullOrEmpty(receiveError) && invalidFrames == 0 &&
                sourceOverReturnedFrames == 0 && !captureLimitReached &&
                (readSummary == null ||
                 (readSummary.DriverBufferOverflowEvents == 0 &&
                  readSummary.DriverReadErrors == 0 &&
                  readSummary.OtherStatusReadCalls == 0 &&
                  readSummary.ReadCallsWithoutStatus == 0));

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
            WriteField(writer, "AcceptedDiskWritesComplete", status.ApplicationWriteComplete);
            WriteField(writer, "ApplicationWriteComplete", applicationComplete);
            WriteField(writer, "VehicleBusLossless", "UNVERIFIED");
            WriteField(writer, "TimestampClock", "J2534NativeUnverified;HostUTCSeparate");
            WriteField(writer, "ReadHealthObserved", readSummary != null);
            WriteField(writer, "J2534ReadStatusSuccess", readSummary?.SuccessfulReadCalls);
            WriteField(writer, "J2534ReadStatusTimeout", readSummary?.TimedOutReadCalls);
            WriteField(writer, "J2534ReadStatusBufferEmpty", readSummary?.EmptyBufferReadCalls);
            WriteField(writer, "J2534ReadStatusOther", readSummary?.OtherStatusReadCalls);
            WriteField(writer, "J2534ReadStatusUnavailable", readSummary?.ReadCallsWithoutStatus);
            WriteField(writer, "J2534DriverReadErrors", readSummary?.DriverReadErrors);
            WriteField(writer, "J2534BufferOverflowEvents", readSummary?.DriverBufferOverflowEvents);
            WriteField(writer, "J2534OverflowFramesLost", "UNKNOWN");
            WriteField(writer, "J2534OverflowFramesProvidedBySource", readSummary?.OverflowFramesProvidedBySource);
            WriteField(writer, "J2534OverflowFramesAcceptedByPump", readSummary?.OverflowFramesAcceptedByPump);
            WriteField(writer, "J2534OverflowReturnCountMeaning", "SOURCE_PROVIDED_NOT_INDEPENDENTLY_VERIFIED_NATIVE_COUNT");
            // Stopwatch ticks are host monotonic durations, never native J2534
            // timestamps or a substitute for bus frame timing.
            WriteField(writer, "HostTimingFrequencyTicksPerSecond", readSummary?.TimingFrequency);
            WriteField(writer, "HostReadElapsedTicksTotal", readSummary?.ReadCallElapsedTicksTotal);
            WriteField(writer, "HostReadElapsedTicksMax", readSummary?.ReadCallElapsedTicksMax);
            WriteField(writer, "HostProcessingElapsedTicksTotal", readSummary?.ProcessingElapsedTicksTotal);
            WriteField(writer, "HostProcessingElapsedTicksMax", readSummary?.ProcessingElapsedTicksMax);
            WriteField(writer, "HostTimingMeaning", "MONOTONIC_HOST_DURATION_NOT_VEHICLE_TIMESTAMP");
            WriteField(writer, "HostChannelOpenToFilterStartTicks", readSummary?.ChannelToFilterElapsedTicks);
            WriteField(writer, "HostFilterInstallElapsedTicks", readSummary?.FilterInstallElapsedTicks);
            WriteField(writer, "HostFilterReadyToFirstReadTicks", readSummary?.FilterToFirstReadElapsedTicks);
            WriteField(writer, "HostStartupTimingLimit", "HOST_INTERVALS_NOT_DRIVER_QUEUE_FILL_OR_CAN_TIME");
            // A J2534 indication is not an accepted CAN data frame and
            // is not, by itself, evidence of lost vehicle bus traffic.
            WriteField(writer, "J2534IndicationRecordsIgnored", readSummary?.IgnoredIndicationRecords);
            WriteField(writer, "InvalidShortOrNullCanRecords", readSummary?.RejectedMissingOrShortRecords);
            WriteField(writer, "InvalidOversizeClassicalCanRecords", readSummary?.RejectedOversizeRecords);
            WriteField(writer, "InvalidArbitrationIdRecords", readSummary?.RejectedArbitrationIds);
            WriteField(writer, "J2534IndicationMeaning", "CONTROL_NOT_BUS_FRAME");
            var evidence = readSummary?.RejectionEvidence;
            int retained = evidence?.Count ?? 0;
            long observed = invalidFrames + (readSummary?.IgnoredIndicationRecords ?? 0);
            bool missingContents = false;
            if (evidence != null)
                foreach (var record in evidence)
                {
                    var frame = record.Frame;
                    if (frame?.Data == null || (frame.NativeDataSize.HasValue &&
                        frame.NativeDataSize.Value != frame.Data.Length))
                        missingContents = true;
                }
            WriteField(writer, "RejectedRecordContentsRetained",
                retained == 0 ? "NO" : retained == observed && !missingContents ? "YES" : "PARTIAL");
            WriteField(writer, "RejectedRecordEvidenceLimit", CanCaptureSummary.RejectionEvidenceLimit);
            WriteField(writer, "RejectedRecordEvidenceCount", retained);
            WriteField(writer, "RejectedRecordEvidenceOmitted", Math.Max(
                readSummary?.RejectionEvidenceOmitted ?? 0, observed - retained));
            for (int i = 0; i < retained; i++)
            {
                var record = evidence[i];
                var frame = record.Frame;
                string prefix = "RejectedRecord" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".";
                WriteField(writer, prefix + "BatchOrdinal", record.BatchOrdinal);
                WriteField(writer, prefix + "SlotIndex", record.SlotIndex);
                WriteField(writer, prefix + "Reason", record.Reason);
                WriteField(writer, prefix + "ReadResult", frame?.ReadResult);
                WriteField(writer, prefix + "TimestampRaw", frame?.TimestampRaw);
                WriteField(writer, prefix + "RxStatusRaw", frame?.RxStatusRaw);
                WriteField(writer, prefix + "NativeProtocolId", frame?.NativeProtocolId);
                WriteField(writer, prefix + "NativeTxFlags", frame?.NativeTxFlags);
                WriteField(writer, prefix + "NativeDataSize", frame?.NativeDataSize);
                WriteField(writer, prefix + "NativeExtraDataIndex", frame?.NativeExtraDataIndex);
                WriteField(writer, prefix + "CopiedDataLength", frame?.Data?.Length);
                WriteField(writer, prefix + "DataHex", frame?.Data == null ? null : BitConverter.ToString(frame.Data));
            }
        }

        private static void WriteField(TextWriter writer, string key, object value)
        {
            string result = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            writer.WriteLine(key + "=" + result.Replace('\r', ' ').Replace('\n', ' '));
        }
    }
}
