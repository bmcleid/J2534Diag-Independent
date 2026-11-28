using System;
using System.Collections.Generic;

namespace J2534Diag
{
    // Complete classical-CAN Mode06 application payloads, not raw CAN frames.
    // IDs describe the GM reference table; reception does not prove SRX support.
    public sealed class Mode6Result
    {
        public byte MonitorId { get; internal set; }
        public byte TestId { get; internal set; }
        public byte UnitsAndScalingId { get; internal set; }
        public ushort RawValue { get; internal set; }
        public ushort RawMinimum { get; internal set; }
        public ushort RawMaximum { get; internal set; }
        public bool IsReferenceMisfireCount => MonitorId >= 0xA2 && MonitorId <= 0xA9 &&
            (TestId == 0x0B || TestId == 0x0C) && UnitsAndScalingId == 0x24;
    }

    public static class Mode6Response
    {
        public static bool TrySource(byte[] frame, uint rxStatus, bool extended,
            out uint source, out byte[] flowControlId)
        {
            source = 0; flowControlId = null;
            if (frame == null || frame.Length < 5 || frame.Length > 12 ||
                (rxStatus & ~0x100u) != 0 || ((rxStatus & 0x100u) != 0) != extended) return false;
            uint id = ((uint)frame[0] << 24) | ((uint)frame[1] << 16) |
                ((uint)frame[2] << 8) | frame[3];
            if (extended ? (id & 0xFFFFFF00u) != 0x18DAF100u : id < 0x7E8 || id > 0x7EF)
                return false;
            source = id;
            uint target = extended ? 0x18DA00F1u | ((id & 255u) << 8) : id - 8;
            flowControlId = new[] { (byte)(target >> 24), (byte)(target >> 16),
                (byte)(target >> 8), (byte)target };
            return true;
        }

        public static bool TryRead(byte[] payload, out List<Mode6Result> results)
        {
            results = new List<Mode6Result>();
            // Reject the entire incomplete reply, including a truncated final record.
            // Support bitmaps (46 00/20/... +4 bytes) are not test-value records.
            if (payload == null || payload.Length < 10 || payload.Length > 4095 ||
                payload[0] != 0x46 || (payload.Length - 1) % 9 != 0) return false;
            for (int i = 1; i < payload.Length; i += 9)
            {
                if ((payload[i] & 0x1F) == 0) { results.Clear(); return false; }
                results.Add(new Mode6Result { MonitorId = payload[i], TestId = payload[i + 1],
                    UnitsAndScalingId = payload[i + 2], RawValue = Read16(payload, i + 3),
                    RawMinimum = Read16(payload, i + 5), RawMaximum = Read16(payload, i + 7) });
            }
            return true;
        }
        private static ushort Read16(byte[] data, int offset) =>
            (ushort)((data[offset] << 8) | data[offset + 1]);
    }
}
