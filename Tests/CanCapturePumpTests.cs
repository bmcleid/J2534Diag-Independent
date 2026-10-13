using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class CanCapturePumpTests
    {
        private sealed class FakeSource : ICanFrameBatchSource
        {
            public readonly Queue<byte[][]> Batches = new Queue<byte[][]>();
            public readonly List<int> Requested = new List<int>();
            public Action OnRead;
            public bool Fail;

            public IReadOnlyList<byte[]> ReadBatch(int requestedMaximum, int timeoutMilliseconds)
            {
                Requested.Add(requestedMaximum);
                if (timeoutMilliseconds != 5)
                    throw new Exception("Unexpected read timeout.");
                OnRead?.Invoke();
                if (Fail) throw new InvalidOperationException("Simulated driver failure");
                if (Batches.Count == 0)
                    return Array.Empty<byte[]>();
                return Batches.Dequeue();
            }
        }

        private static int _assertions;
        private static void Check(bool value, string reason)
        {
            ++_assertions;
            if (!value) throw new Exception(reason);
        }

        private static readonly DateTime TestClock =
            new DateTime(2026, 10, 13, 16, 0, 0, DateTimeKind.Utc);

        private static CanCapturePump Pump(FakeSource source, int cap)
        {
            return new CanCapturePump(source, cap, () => TestClock, id => id == 0x7EA ? "TCM sample" : "");
        }

        public static int RunAll()
        {
            CancelMidBatchStillStoresAllReturnedFrames();
            CapacityStopsAndReportsExcessReturnedFrames();
            MalformedFrameDoesNotConsumeCapacity();
            BothCanIdWidthsAndTimestamp();
            EmptyBatchAndCancellation();
            DriverErrorMustPropagate();
            NonUtcTimestampIsRejected();
            return _assertions;
        }

        private static byte[] Frame(uint canId, params byte[] payload)
        {
            return new[] { (byte)(canId >> 24), (byte)(canId >> 16),
                (byte)(canId >> 8), (byte)canId }.Concat(payload).ToArray();
        }

        private static void CancelMidBatchStillStoresAllReturnedFrames()
        {
            var source = new FakeSource();
            source.Batches.Enqueue(new[]
            {
                Frame(0x7EA, 0x01, 0x41), Frame(0x7EA, 0x02, 0x42),
                Frame(0x7EA, 0x03, 0x43)
            });
            var saved = new List<CanCaptureRecord>();
            using (var cancel = new CancellationTokenSource())
            {
                var result = Pump(source, 10).Run(cancel.Token, row =>
                {
                    saved.Add(row);
                    if (saved.Count == 1) cancel.Cancel();
                });
                Check(result.Cancelled, "Cancellation status missing");
                Check(!result.LimitReached, "Cancellation should not imply frame cap");
                Check(result.StoredFrames == 3 && saved.Count == 3, "Returned batch was partially discarded");
                Check(source.Requested.Count == 1, "Reader continued after cancellation");
                Check(saved.Select(x => x.Index).SequenceEqual(new long[] { 1, 2, 3 }), "Row indexing/order wrong");
            }
        }

        private static void CapacityStopsAndReportsExcessReturnedFrames()
        {
            var source = new FakeSource();
            // A nonconforming source over-returns one frame; flag it as discarded.
            source.Batches.Enqueue(new[]
            {
                Frame(0x7EA, 0x11), Frame(0x7EA, 0x22), Frame(0x7EA, 0x33)
            });
            var stored = new List<CanCaptureRecord>();
            var summary = Pump(source, 2).Run(CancellationToken.None, stored.Add);
            Check(summary.StoredFrames == 2, "Frame cap not enforced");
            Check(summary.ExcessReturnedFrames == 1, "Over-return not counted");
            Check(summary.LimitReached, "LimitReached missing");
            Check(source.Requested.SequenceEqual(new[] { 2 }), "Reader exceeded remaining frame budget");
            Check(stored.Count == 2 && stored[1].Data == "22", "Wrong frames retained");
        }

        private static void MalformedFrameDoesNotConsumeCapacity()
        {
            var source = new FakeSource();
            source.Batches.Enqueue(new[]
            {
                new byte[] { 0, 1 }, Frame(0x7EA, 0x7F, 0x22, 0x31)
            });
            var stored = new List<CanCaptureRecord>();
            var result = Pump(source, 1).Run(CancellationToken.None, stored.Add);
            Check(result.InvalidFrames == 1, "Malformed record not reported");
            Check(result.StoredFrames == 1 && result.ExcessReturnedFrames == 0, "Malformed record consumed budget");
            Check(stored[0].ArbId == "0x7EA", "Wrong CAN ID after malformed record");
        }

        private static void BothCanIdWidthsAndTimestamp()
        {
            var source = new FakeSource();
            source.Batches.Enqueue(new[]
            {
                Frame(0x7E8, 0x03, 0x41, 0x0C),
                Frame(0x18DAF111, 0x03, 0x62, 0xF1, 0x90)
            });
            var captured = new List<CanCaptureRecord>();
            Pump(source, 2).Run(CancellationToken.None, captured.Add);
            Check(captured[0].ArbId == "0x7E8", "11-bit ID formatting incorrect");
            Check(captured[1].ArbId == "0x18DAF111", "29-bit ID formatting incorrect");
            Check(captured[0].ReceivedAtUtc == "2026-10-13T16:00:00.0000000Z", "UTC host timestamp format wrong");
            Check(captured[1].Data == "03-62-F1-90", "Raw payload changed");
        }

        private static void EmptyBatchAndCancellation()
        {
            var source = new FakeSource();
            using (var cancel = new CancellationTokenSource())
            {
                source.OnRead = cancel.Cancel;
                var result = Pump(source, 3).Run(cancel.Token, _ => { });
                Check(result.Cancelled, "Empty batch cancellation not observed");
                Check(result.StoredFrames == 0, "Empty batch falsely counted");
            }
        }

        private static void DriverErrorMustPropagate()
        {
            var source = new FakeSource { Fail = true };
            try
            {
                Pump(source, 10).Run(CancellationToken.None, _ => { });
                throw new Exception("Driver failure was silently swallowed");
            }
            catch (InvalidOperationException ex)
            {
                Check(ex.Message == "Simulated driver failure", "Driver error changed or suppressed");
            }
        }

        private static void NonUtcTimestampIsRejected()
        {
            var source = new FakeSource();
            source.Batches.Enqueue(new[] { Frame(0x7EA, 0x33) });
            var pump = new CanCapturePump(source, 1,
                () => new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Local), null);
            try
            {
                pump.Run(CancellationToken.None, _ => { });
                throw new Exception("Non-UTC timestamp was silently accepted");
            }
            catch (InvalidOperationException ex)
            {
                Check(ex.Message.Contains("UTC"), "Missing invalid-clock explanation");
            }
        }
    }
}
