using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace J2534Diag
{
    // Classical CAN normal addressing, with a bounded assembly per responder.
    public class IsoTpParser
    {
        private sealed class AssemblyState
        {
            public readonly List<byte> Bytes = new List<byte>();
            public int Length;
            public int NextSequence = 1;
            public long LastMilliseconds;
        }
        private readonly Dictionary<uint, AssemblyState> _assemblies = new Dictionary<uint, AssemblyState>();
        private readonly Func<long> _milliseconds;
        public IsoTpParser() : this(() => (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency)) { }
        internal IsoTpParser(Func<long> milliseconds) { _milliseconds = milliseconds; }
        public event EventHandler<IsoTpFrameEventArgs> FrameReceived;
        public void Reset() { _assemblies.Clear(); }

        public void ProcessMessage(byte[] data)
        {
            if (data == null || data.Length < 5 || data.Length > 12) return;
            uint id = ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
            long now = _milliseconds();
            foreach (uint expired in _assemblies.Where(p => now - p.Value.LastMilliseconds >= 1000)
                .Select(p => p.Key).ToArray()) _assemblies.Remove(expired);
            byte[] canId = data.Take(4).ToArray();
            byte[] payload = data.Skip(4).ToArray();
            int type = payload[0] >> 4;
            if (type == 0)
            {
                _assemblies.Remove(id);
                int length = payload[0] & 15;
                if (length == 0 || length > 7 || payload.Length < length + 1) return;
                FrameReceived?.Invoke(this, new IsoTpFrameEventArgs(IsoTpFrameType.SingleFrame,
                    canId, payload.Skip(1).Take(length).ToArray(), true));
            }
            else if (type == 1)
            {
                _assemblies.Remove(id);
                if (payload.Length != 8) return;
                int length = ((payload[0] & 15) << 8) | payload[1];
                if (length <= 7 || _assemblies.Count >= 8) return;
                var state = new AssemblyState { Length = length, LastMilliseconds = now };
                state.Bytes.AddRange(payload.Skip(2));
                _assemblies[id] = state;
                FrameReceived?.Invoke(this, new IsoTpFrameEventArgs(IsoTpFrameType.FirstFrame,
                    canId, state.Bytes.ToArray(), false));
            }
            else if (type == 2)
            {
                AssemblyState state;
                if (!_assemblies.TryGetValue(id, out state)) return;
                int remaining = state.Length - state.Bytes.Count;
                if ((payload[0] & 15) != state.NextSequence ||
                    payload.Length - 1 < Math.Min(7, remaining))
                {
                    _assemblies.Remove(id);
                    return;
                }
                state.Bytes.AddRange(payload.Skip(1).Take(remaining));
                state.NextSequence = (state.NextSequence + 1) & 15;
                state.LastMilliseconds = now;
                bool complete = state.Bytes.Count == state.Length;
                if (complete) _assemblies.Remove(id);
                FrameReceived?.Invoke(this, new IsoTpFrameEventArgs(IsoTpFrameType.ConsecutiveFrame,
                    canId, state.Bytes.ToArray(), complete));
            }
            else if (type == 3 && payload.Length >= 3)
                FrameReceived?.Invoke(this, new IsoTpFrameEventArgs(IsoTpFrameType.FlowControl, canId, payload, false));
            else _assemblies.Remove(id);
        }
    }
}
