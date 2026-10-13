using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace J2534Diag
{
    /// <summary>
    /// A deliberately narrow receive-only abstraction. A fake can drive exactly
    /// the same capture algorithm as the Windows J2534 reader, without hardware.
    /// Does not transmit, change filters, or manage an adapter connection.
    /// </summary>
    public interface ICanFrameBatchSource
    {
        IReadOnlyList<byte[]> ReadBatch(int requestedMaximum, int timeoutMilliseconds);
    }

    public sealed class CanCaptureSummary
    {
        public int StoredFrames { get; internal set; }
        public int InvalidFrames { get; internal set; }
        public int ExcessReturnedFrames { get; internal set; }
        public bool LimitReached { get; internal set; }
        public bool Cancelled { get; internal set; }
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

        public CanCaptureSummary Run(
            CancellationToken cancellation,
            Action<CanCaptureRecord> store,
            Action<int> batchStored = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var summary = new CanCaptureSummary();

            while (!cancellation.IsCancellationRequested && summary.StoredFrames < _maximumFrames)
            {
                int requested = Math.Min(200, _maximumFrames - summary.StoredFrames);
                var batch = _source.ReadBatch(requested, 5);
                if (batch == null)
                    throw new InvalidOperationException("J2534 receive source returned a null frame batch.");

                // Do not abort part-way through a batch on cancellation; those
                // frames have already been removed from the device-side queue.
                foreach (var data in batch)
                {
                    if (summary.StoredFrames >= _maximumFrames)
                    {
                        summary.ExcessReturnedFrames++;
                        continue;
                    }

                    // Classic J2534 CAN records contain four CAN-ID bytes.
                    if (data == null || data.Length < 4)
                    {
                        summary.InvalidFrames++;
                        continue;
                    }

                    uint id = ((uint)data[0] << 24) | ((uint)data[1] << 16) |
                        ((uint)data[2] << 8) | data[3];

                    DateTime timestamp = _clockUtc();
                    if (timestamp.Kind != DateTimeKind.Utc)
                        throw new InvalidOperationException("The capture clock must supply UTC time.");

                    var record = new CanCaptureRecord
                    {
                        Index = summary.StoredFrames + 1L,
                        ReceivedAtUtc = timestamp.ToString("O", CultureInfo.InvariantCulture),
                        ArbId = "0x" + id.ToString("X3", CultureInfo.InvariantCulture),
                        ArbIdDescription = _describe(id) ?? "",
                        Data = BitConverter.ToString(data, 4, data.Length - 4)
                    };
                    store(record);
                    summary.StoredFrames++;
                }

                batchStored?.Invoke(summary.StoredFrames);

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
