using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
        // Frames are supplied only by a source that actually obtained and
        // validated a native overflow response with a returned count. The
        // pinned J2534-Sharp Channel.GetMessages exception currently supplies
        // no such frames; its default remains an empty collection.
        public IReadOnlyList<CanReceivedFrame> ReturnedFrames { get; }

        public CanReceiveBufferOverflowException(string message, Exception inner)
            : this(message, inner, null) { }

        public CanReceiveBufferOverflowException(string message, Exception inner,
            IReadOnlyList<CanReceivedFrame> returnedFrames)
            : base(message, inner)
        {
            ReturnedFrames = returnedFrames == null
                ? (IReadOnlyList<CanReceivedFrame>)Array.Empty<CanReceivedFrame>()
                : new List<CanReceivedFrame>(returnedFrames).AsReadOnly();
        }
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
        public long OverflowFramesProvidedBySource { get; internal set; }
        public long OverflowFramesAcceptedByPump { get; internal set; }
        public long ReadCallElapsedTicksTotal { get; internal set; }
        public long ReadCallElapsedTicksMax { get; internal set; }
        public long ProcessingElapsedTicksTotal { get; internal set; }
        public long ProcessingElapsedTicksMax { get; internal set; }
        public long TimingFrequency { get; internal set; }
        public long FirstReadStartedHostTicks { get; internal set; }
        public long ChannelToFilterElapsedTicks { get; internal set; }
        public long FilterInstallElapsedTicks { get; internal set; }
        public long FilterToFirstReadElapsedTicks { get; internal set; }
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
        private readonly int _readMaximum;
        private readonly int _readTimeoutMilliseconds;
        // Retained even when a reader, callback or disk writer throws.
        public CanCaptureSummary LastSummary { get; private set; }

        public CanCapturePump(
            ICanFrameBatchSource source,
            int maximumFrames,
            Func<DateTime> clockUtc,
            Func<uint, string> describe,
            int readMaximum = 200,
            int readTimeoutMilliseconds = 5)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (readMaximum < 1 || readMaximum > 200)
                throw new ArgumentOutOfRangeException(nameof(readMaximum));
            if (readTimeoutMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(readTimeoutMilliseconds));
            _readMaximum = readMaximum;
            _readTimeoutMilliseconds = readTimeoutMilliseconds;
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
                int requested = Math.Min(_readMaximum, _maximumFrames - summary.StoredFrames);
                IReadOnlyList<CanReceivedFrame> batch;
                CanReceiveBufferOverflowException reportedOverflow = null;
                long readBegin = Stopwatch.GetTimestamp();
                if (summary.FirstReadStartedHostTicks == 0)
                    summary.FirstReadStartedHostTicks = readBegin;
                try
                {
                    batch = _source.ReadBatch(requested, _readTimeoutMilliseconds);
                }
                catch (CanReceiveBufferOverflowException ex)
                {
                    AccumulateReadElapsed(summary, Stopwatch.GetTimestamp() - readBegin);
                    summary.DriverBufferOverflowEvents++;
                    summary.DriverReadErrors++;
                    reportedOverflow = ex;
                    batch = ex.ReturnedFrames;
                    summary.OverflowFramesProvidedBySource += batch.Count;
                    // A native source reporting more rows than requested has
                    // not established valid return-count semantics. Refuse all
                    // such rows, preserve the original overflow exception.
                    if (batch.Count > requested)
                    {
                        summary.ExcessReturnedFrames += batch.Count;
                        ExceptionDispatchInfo.Capture(ex).Throw();
                    }
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
                // The overflow has its own independent counter. Do not
                // misclassify a thrown read as an ordinary completed poll.
                if (reportedOverflow == null)
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
                    if (reportedOverflow != null)
                        summary.OverflowFramesAcceptedByPump++;
                }

                if (batch.Count > 0)
                    batchStored?.Invoke(summary.StoredFrames);
                long processingTicks = Stopwatch.GetTimestamp() - processingBegin;
                summary.ProcessingElapsedTicksTotal += processingTicks;
                if (processingTicks > summary.ProcessingElapsedTicksMax)
                    summary.ProcessingElapsedTicksMax = processingTicks;

                // Preserve any supplied partial frames, but always terminate
                // and report the original native overflow as incomplete.
                if (reportedOverflow != null)
                    ExceptionDispatchInfo.Capture(reportedOverflow).Throw();

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
