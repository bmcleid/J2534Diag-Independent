using System;
using System.Collections.Generic;

namespace J2534Diag
{
    // Already copied from a future sole-owner native J2534 read, never pointers.
    public sealed class NativeCanReadResult
    {
        public uint Status { get; private set; }
        public uint Count { get; private set; }
        public IReadOnlyList<CanReceivedFrame> Slots { get; private set; }
        public NativeCanReadResult(uint status, uint count, IReadOnlyList<CanReceivedFrame> slots)
        {
            Status = status;
            Count = count;
            Slots = slots ?? Array.Empty<CanReceivedFrame>();
        }
    }
    public interface INativeCanReadProvider
    {
        NativeCanReadResult ReadNative(int maximum, int timeoutMilliseconds);
    }
    // Offline-only safety gate. This class makes NO driver calls or connections.
    public sealed class NativeCanReadBoundary : ICanFrameBatchSource, ICanReadResultSource
    {
        private readonly INativeCanReadProvider _provider;
        public string LastReadResult { get; private set; }
        public NativeCanReadBoundary(INativeCanReadProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }
        public IReadOnlyList<CanReceivedFrame> ReadBatch(int maximum, int timeout)
        {
            if (maximum < 1 || maximum > 200) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (timeout < 0) throw new ArgumentOutOfRangeException(nameof(timeout));
            LastReadResult = null;
            NativeCanReadResult r = _provider.ReadNative(maximum, timeout);
            if (r == null) throw new InvalidOperationException("Native read returned null.");
            string label;
            switch (r.Status)
            {
                case 0x00: label = "STATUS_NOERROR"; break;
                case 0x09: label = "TIMEOUT"; break;
                case 0x10: label = "BUFFER_EMPTY"; break;
                case 0x12: label = "BUFFER_OVERFLOW"; break;
                default: throw new InvalidOperationException("Unknown native J2534 read status 0x" +
                    r.Status.ToString("X8") + "; no rows trusted.");
            }
            LastReadResult = label;
            if (r.Count > maximum || r.Count > r.Slots.Count)
                return RejectCount(r, maximum);
            int n = (int)r.Count;
            if (n > 0 && AllUnpopulated(r.Slots, n))
                return RejectCount(r, maximum);
            var records = new List<CanReceivedFrame>(n);
            for (int i = 0; i < n; i++)
            {
                var f = r.Slots[i];
                if (f == null) records.Add(new CanReceivedFrame { ReadResult = label });
                else records.Add(new CanReceivedFrame {
                    Data = f.Data == null ? null : (byte[])f.Data.Clone(),
                    TimestampRaw = f.TimestampRaw, RxStatusRaw = f.RxStatusRaw,
                    ReadResult = label });
            }
            if (r.Status == 0x12)
                throw new CanReceiveBufferOverflowException(
                    "Native J2534 BUFFER_OVERFLOW reported; bus loss remains possible.",
                    null, records);
            return records;
        }
        private static bool AllUnpopulated(IReadOnlyList<CanReceivedFrame> slots, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var f = slots[i];
                if (f != null && ((f.Data != null && f.Data.Length > 0) ||
                    (f.TimestampRaw.HasValue && f.TimestampRaw.Value != 0) ||
                    (f.RxStatusRaw.HasValue && f.RxStatusRaw.Value != 0))) return false;
            }
            return true;
        }
        private static IReadOnlyList<CanReceivedFrame> RejectCount(NativeCanReadResult r, int max)
        {
            string error = "Untrusted native returned count " + r.Count +
                " requested " + max + " populated slots " + r.Slots.Count;
            if (r.Status == 0x12)
                throw new CanReceiveBufferOverflowException(
                    "Native J2534 BUFFER_OVERFLOW: " + error,
                    new InvalidOperationException(error));
            throw new InvalidOperationException(error);
        }
    }
}
