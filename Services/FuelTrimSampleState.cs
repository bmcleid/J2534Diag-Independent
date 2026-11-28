using System;
using System.Collections.Generic;
using System.Globalization;

namespace J2534Diag
{
    public sealed class FuelTrimInput
    {
        public string SourceKey { get; internal set; }
        public string Protocol { get; internal set; }
        public byte Pid { get; internal set; }
        public byte[] Data { get; internal set; }
    }

    // Narrow framing for the six existing scalar channels. None requires
    // segmented transport. This does not identify an ECU or establish PID support.
    public static class FuelTrimScalarFrame
    {
        public static bool TryRead(byte[] frame, uint rxStatus, out FuelTrimInput input)
        {
            input = null;
            if (frame == null || frame.Length < 8 || frame.Length > 12 ||
                (rxStatus & ~0x100u) != 0) return false;
            uint id = ((uint)frame[0] << 24) | ((uint)frame[1] << 16) |
                ((uint)frame[2] << 8) | frame[3];
            bool extended = (rxStatus & 0x100u) != 0;
            if (extended ? (id & 0xFFFFFF00u) != 0x18DAF100u : (id < 0x7E8 || id > 0x7EF))
                return false;
            // Single-frame only. Reject FF/CF/FC, zero-length and truncated PCI.
            int length = frame[4];
            if (length < 3 || length > 7 || frame.Length < 5 + length) return false;
            string protocol;
            int offset;
            byte pid;
            if (frame[5] == 0x41)
            {
                protocol = "Mode01";
                pid = frame[6];
                offset = 7;
            }
            else if (length >= 4 && frame[5] == 0x62 && frame[6] == 0xF4)
            {
                protocol = "UDS-F4 (mapping unverified)";
                pid = frame[7];
                offset = 8;
            }
            else return false;
            int width = pid == 0x0C ? 2 :
                (pid == 0x06 || pid == 0x07 || pid == 0x08 || pid == 0x09 || pid == 0x11) ? 1 : 0;
            if (width == 0 || length != offset - 5 + width) return false;
            var data = new byte[width];
            Array.Copy(frame, offset, data, 0, width);
            input = new FuelTrimInput
            {
                SourceKey = "0x" + id.ToString(extended ? "X8" : "X3", CultureInfo.InvariantCulture) + " / " + protocol,
                Protocol = protocol, Pid = pid, Data = data
            };
            return true;
        }
    }

    public sealed class FuelTrimSnapshot
    {
        private readonly Dictionary<byte, double> _values;
        internal FuelTrimSnapshot(Dictionary<byte, double> values) { _values = values; }
        public double? Value(byte pid)
        {
            double value;
            return _values.TryGetValue(pid, out value) ? (double?)value : null;
        }
        public bool TryBank(int bank, out double throttle, out double rpm, out double total)
        {
            throttle = rpm = total = 0;
            byte stft = bank == 1 ? (byte)0x06 : (byte)0x08;
            byte ltft = bank == 1 ? (byte)0x07 : (byte)0x09;
            double shortTrim, longTrim;
            if ((bank != 1 && bank != 2) ||
                !_values.TryGetValue(0x11, out throttle) || !_values.TryGetValue(0x0C, out rpm) ||
                !_values.TryGetValue(stft, out shortTrim) || !_values.TryGetValue(ltft, out longTrim))
                return false;
            total = shortTrim + longTrim;
            return true;
        }
    }

    // UI-thread state, with an injected monotonic clock domain. The age limit
    // is an application display policy and is not an OEM diagnostic threshold.
    public sealed class FuelTrimSampleState
    {
        private sealed class Sample { public double Value; public long Ticks; }
        private readonly Dictionary<string, Dictionary<byte, Sample>> _sources =
            new Dictionary<string, Dictionary<byte, Sample>>(StringComparer.Ordinal);
        private readonly long _maximumAge;
        public FuelTrimSampleState(long maximumAgeTicks)
        {
            if (maximumAgeTicks < 1) throw new ArgumentOutOfRangeException(nameof(maximumAgeTicks));
            _maximumAge = maximumAgeTicks;
        }
        public void Clear() { _sources.Clear(); }
        public bool Update(string source, byte pid, double value, long ticks)
        {
            bool valid = pid == 0x0C ? value >= 0 && value <= 16383.75 :
                pid == 0x11 ? value >= 0 && value <= 100 :
                (pid >= 0x06 && pid <= 0x09) && value >= -100 && value < 100;
            if (string.IsNullOrEmpty(source) || ticks < 0 || !valid ||
                double.IsNaN(value) || double.IsInfinity(value)) return false;
            Dictionary<byte, Sample> values;
            if (!_sources.TryGetValue(source, out values))
            {
                if (_sources.Count >= 16) return false;
                values = new Dictionary<byte, Sample>();
                _sources.Add(source, values);
            }
            Sample previous;
            if (values.TryGetValue(pid, out previous) && ticks < previous.Ticks) return false;
            values[pid] = new Sample { Value = value, Ticks = ticks };
            return true;
        }
        public FuelTrimSnapshot Snapshot(string source, long nowTicks)
        {
            var result = new Dictionary<byte, double>();
            Dictionary<byte, Sample> values;
            if (source != null && _sources.TryGetValue(source, out values))
                foreach (var pair in values)
                    if (nowTicks >= pair.Value.Ticks && nowTicks - pair.Value.Ticks <= _maximumAge)
                        result.Add(pair.Key, pair.Value.Value);
            return new FuelTrimSnapshot(result);
        }
    }
}
