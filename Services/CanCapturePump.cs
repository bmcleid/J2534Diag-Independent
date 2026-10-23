using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Threading;

namespace J2534Diag
{
    /// <summary>
    /// A deliberately narrow receive-only abstraction. A fake can drive exactly
    /// the same capture algorithm as the Windows J2534 reader, without hardware.
    /// Does not transmit, change filters, or manage an adapter connection.
    /// </summary>
    public sealed class CanReceivedFrame
    {
        public byte[] Data { get; set; }
        // J2534 native values: null means unknown, while zero is a real value.
        public uint? TimestampRaw { get; set; }
        public uint? RxStatusRaw { get; set; }
        // Unmodified GetMessages ResultCode enum name, if supplied.
        public string ReadResult { get; set; }
    }

    public interface ICanFrameBatchSource
    {
        IReadOnlyList<CanReceivedFrame> ReadBatch(int requestedMaximum, int timeoutMilliseconds);
    }

    /// <summary>
    /// Optional status channel for J2534 read results. The status refers to
    /// the most recently completed ReadBatch call on the same receive thread.
    /// Unknown/unsupported sources leave this interface unimplemented.
    /// </summary>
    public interface ICanReadResultSource
    {
        string LastReadResult { get; }
    }

    /// <summary>
    /// Raised only for an explicitly reported J2534 receive-buffer overflow.
    /// This means loss has been reported, not an estimated frame count.
    /// </summary>
    public sealed class CanReceiveBufferOverflowException : InvalidOperationException
    {
        public CanReceiveBufferOverflowException(string message, Exception inner)
            : base(message, inner) { }
    }

    public sealed class CanCaptureSummary
    {
        public int StoredFrames { get; internal set; }
        public int InvalidFrames { get; internal set; }
        // J2534 indication/status records are not malformed bus frames.
        public int IgnoredIndicationRecords { get; internal set; }
        public int RejectedMissingOrShortRecords { get; internal set; }
        public int RejectedOversizeRecords { get; internal set; }
        public int RejectedArbitrationIds { get; internal set; }
        public int ExcessReturnedFrames { get; internal set; }
        public bool LimitReached { get; internal set; }
        public bool Cancelled { get; internal set; }
        public long SuccessfulReadCalls { get; internal set; }
        public long TimedOutReadCalls { get; internal set; }
        public long EmptyBufferReadCalls { get; internal set; }
        public long OtherStatusReadCalls { get; internal set; }
        public long ReadCallsWithoutStatus { get; internal set; }
        public long DriverReadErrors { get; internal set; }
        public long DriverBufferOverflowEvents { get; internal set; }
        public long ReadCallElapsedTicksTotal { get; internal set; }
        public long ReadCallElapsedTicksMax { get; internal set; }
        public long ProcessingElapsedTicksTotal { get; internal set; }
        public long ProcessingElapsedTicksMax { get; internal set; }
        public long TimingFrequency { get; internal set; }
    }

    /// <summary>
    /// Synchronous, testable capture engine. Once a batch has been returned by
    /// the driver, processes the entire returned batch (up to the configured cap)
    /// before honoring cancellation. This does NOT drain unread driver buffers.
    /// </summary>
    public sealed class CanCapturePump
    {
        private readonly ICanFrameBatchSource _source;
        private readonly int _maximumFrames;
        private readonly Func<DateTime> _clockUtc;
        private readonly Func<uint, string> _describe;
        // Retained even when a reader, callback or disk writer throws.
        public CanCaptureSummary LastSummary { get; private set; }

        public CanCapturePump(
            ICanFrameBatchSource source,
            int maximumFrames,
            Func<DateTime> clockUtc,
            Func<uint, string> describe)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (maximumFrames < 1) throw new ArgumentOutOfRangeException(nameof(maximumFrames));
            _maximumFrames = maximumFrames;
            _clockUtc = clockUtc ?? throw new ArgumentNullException(nameof(clockUtc));
            _describe = describe ?? (id => "");
        }

        private static void AccumulateReadElapsed(CanCaptureSummary summary, long elapsedTicks)
        {
            summary.ReadCallElapsedTicksTotal += elapsedTicks;
            if (elapsedTicks > summary.ReadCallElapsedTicksMax)
                summary.ReadCallElapsedTicksMax = elapsedTicks;
        }

        public CanCaptureSummary Run(
            CancellationToken cancellation,
            Action<CanCaptureRecord> store,
            Action<int> batchStored = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var summary = new CanCaptureSummary();
            LastSummary = summary;
            summary.TimingFrequency = Stopwatch.Frequency;

            while (!cancellation.IsCancellationRequested && summary.StoredFrames < _maximumFrames)
            {
                int requested = Math.Min(200, _maximumFrames - summary.StoredFrames);
                IReadOnlyList<CanReceivedFrame> batch;
                long readBegin = Stopwatch.GetTimestamp();
                try
                {
                    batch = _source.ReadBatch(requested, 5);
                }
                catch (CanReceiveBufferOverflowException)
                {
                    AccumulateReadElapsed(summary, Stopwatch.GetTimestamp() - readBegin);
                    summary.DriverBufferOverflowEvents++;
                    summary.DriverReadErrors++;
                    throw;
                }
                catch (Exception)
                {
                    AccumulateReadElapsed(summary, Stopwatch.GetTimestamp() - readBegin);
                    summary.DriverReadErrors++;
                    throw;
                }

                AccumulateReadElapsed(summary, Stopwatch.GetTimestamp() - readBegin);
                long processingBegin = Stopwatch.GetTimestamp();
                // Empty reads still matter: they do not create frame rows,
                // so preserve per-call status in the independent summary.
                var readDiagnostics = _source as ICanReadResultSource;
                string result = readDiagnostics?.LastReadResult;
                switch (result)
                {
                    case "STATUS_NOERROR":
                        summary.SuccessfulReadCalls++;
                        break;
                    case "TIMEOUT":
                        summary.TimedOutReadCalls++;
                        break;
                    case "BUFFER_EMPTY":
                        summary.EmptyBufferReadCalls++;
                        break;
                    default:
                        if (string.IsNullOrEmpty(result))
                            summary.ReadCallsWithoutStatus++;
                        else
                            summary.OtherStatusReadCalls++;
                        break;
                }
                if (batch == null)
                    throw new InvalidOperationException("J2534 receive source returned a null frame batch.");

                // Do not abort part-way through a batch on cancellation; those
                // frames have already been removed from the device-side queue.
                foreach (var frame in batch)
                {
                    if (summary.StoredFrames >= _maximumFrames)
                    {
                        summary.ExcessReturnedFrames++;
                        continue;
                    }

                    // Distinguish J2534 indication/control messages from
                    // malformed or invalid classical-CAN frames. Neither
                    // category counts as an accepted CAN-bus data row.
                    if (!CanFrameClassifier.TryValidate(frame, out uint id,
                        out CanFrameRejectionKind rejection))
                    {
                        switch (rejection)
                        {
                            case CanFrameRejectionKind.J2534Indication:
                                summary.IgnoredIndicationRecords++;
                                break;
                            case CanFrameRejectionKind.MissingOrShortHeader:
                                summary.InvalidFrames++;
                                summary.RejectedMissingOrShortRecords++;
                                break;
                            case CanFrameRejectionKind.ExceedsClassicalCanLength:
                                summary.InvalidFrames++;
                                summary.RejectedOversizeRecords++;
                                break;
                            case CanFrameRejectionKind.ArbitrationIdOutOfRange:
                                summary.InvalidFrames++;
                                summary.RejectedArbitrationIds++;
                                break;
                            default:
                                throw new InvalidOperationException("Unknown CAN rejection category.");
                        }
                        continue;
                    }

                    var data = frame.Data;

                    DateTime timestamp = _clockUtc();
                    if (timestamp.Kind != DateTimeKind.Utc)
                        throw new InvalidOperationException("The capture clock must supply UTC time.");

                    var record = new CanCaptureRecord
                    {
                        Index = summary.StoredFrames + 1L,
                        ReceivedAtUtc = timestamp.ToString("O", CultureInfo.InvariantCulture),
                        ArbId = "0x" + id.ToString("X3", CultureInfo.InvariantCulture),
                        ArbIdDescription = _describe(id) ?? "",
                        Data = BitConverter.ToString(data, 4, data.Length - 4),
                        J2534TimestampRaw = frame.TimestampRaw.HasValue
                            ? frame.TimestampRaw.Value.ToString(CultureInfo.InvariantCulture) : "",
                        J2534RxStatusHex = frame.RxStatusRaw.HasValue
                            ? "0x" + frame.RxStatusRaw.Value.ToString("X8", CultureInfo.InvariantCulture) : "",
                        J2534ReadResult = frame.ReadResult ?? ""
                    };
                    store(record);
                    summary.StoredFrames++;
                }

                if (batch.Count > 0)
                    batchStored?.Invoke(summary.StoredFrames);
                long processingTicks = Stopwatch.GetTimestamp() - processingBegin;
                summary.ProcessingElapsedTicksTotal += processingTicks;
                if (processingTicks > summary.ProcessingElapsedTicksMax)
                    summary.ProcessingElapsedTicksMax = processingTicks;

                // A hardware driver may return zero messages for a timeout.
                // Avoid busy spinning in that case; do not hide non-timeout faults.
                if (batch.Count == 0)
                    Thread.Sleep(1);
            }

            summary.LimitReached = summary.StoredFrames >= _maximumFrames;
            summary.Cancelled = cancellation.IsCancellationRequested;
            return summary;
        }
    }
}
