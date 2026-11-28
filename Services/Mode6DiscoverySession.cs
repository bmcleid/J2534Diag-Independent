using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace J2534Diag
{
    public enum Mode6SupportState { Undiscovered, Advertised, NotAdvertised, Inconclusive }

    public sealed class Mode6Request
    {
        public uint? ResponseSource { get; private set; }
        public byte MonitorId { get; private set; }
        public bool IsAvailability => (MonitorId & 31) == 0;
        public Mode6Request(uint? source, byte monitor) { ResponseSource = source; MonitorId = monitor; }
        public byte[] ToCanFrame(bool extended)
        {
            uint id = !ResponseSource.HasValue ? (extended ? 0x18DB33F1u : 0x7DFu) :
                extended ? 0x18DA00F1u | ((ResponseSource.Value & 255u) << 8) : ResponseSource.Value - 8;
            var frame = new byte[12];
            frame[0] = (byte)(id >> 24); frame[1] = (byte)(id >> 16);
            frame[2] = (byte)(id >> 8); frame[3] = (byte)id;
            frame[4] = 2; frame[5] = 6; frame[6] = MonitorId;
            return frame;
        }
    }

    public sealed class Mode6PeerSnapshot
    {
        public uint Source { get; internal set; }
        public string Status { get; internal set; }
        public Dictionary<byte, uint> Availability { get; internal set; }
    }

    // One bounded discovery + result snapshot per Start. No driver dependencies.
    // The very same Run/Accept boundary is used by the production form and fake transport.
    public sealed class Mode6DiscoverySession
    {
        private sealed class Peer
        {
            public readonly Dictionary<byte, uint> Pages = new Dictionary<byte, uint>();
            public readonly Dictionary<byte, string> Failures = new Dictionary<byte, string>();
            public string Status = "Undiscovered";
        }
        private readonly object _sync = new object();
        private readonly Dictionary<uint, Peer> _peers = new Dictionary<uint, Peer>();
        private readonly Func<long> _milliseconds;
        private readonly bool _extended;
        private Mode6Request _pending;
        private readonly HashSet<uint> _answered = new HashSet<uint>();
        private bool _stopped;
        private int _started;
        private long _start;
        private string _status = "Undiscovered";
        private int _requests;
        private int _ignored;
        public const int MaximumRequests = 128;
        public const int MaximumMilliseconds = 30000;
        public const int ResponseWindowMilliseconds = 300;
        public const int RequestGapMilliseconds = 50;
        public event Action Changed;
        public event Action<uint, List<Mode6Result>> Results;
        public Mode6DiscoverySession(bool extended = false, Func<long> milliseconds = null)
        { _extended = extended; _milliseconds = milliseconds ?? (() => (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency)); }
        public string Status { get { lock (_sync) return _status; } }
        public int Requests { get { lock (_sync) return _requests; } }
        public int IgnoredResponses { get { lock (_sync) return _ignored; } }
        public Mode6PeerSnapshot[] Sources
        {
            get { lock (_sync) return _peers.OrderBy(p => p.Key).Select(p => new Mode6PeerSnapshot
                { Source = p.Key, Status = p.Value.Status, Availability = new Dictionary<byte, uint>(p.Value.Pages) }).ToArray(); }
        }
        private void Notify() { Changed?.Invoke(); }
        private void SetStatus(string value) { lock (_sync) _status = value; Notify(); }
        public void Stop() { lock (_sync) { _stopped = true; _pending = null; _status = "Stopped — prior support/results retained"; } Notify(); }
        public static bool TryBitmap(byte[] payload, out byte page, out uint bitmap)
        {
            page = 0; bitmap = 0;
            if (payload == null || payload.Length != 6 || payload[0] != 0x46 || (payload[1] & 31) != 0) return false;
            page = payload[1];
            bitmap = ((uint)payload[2] << 24) | ((uint)payload[3] << 16) | ((uint)payload[4] << 8) | payload[5];
            return page != 0xE0 || (bitmap & 1) == 0; // FF is final; the next-range bit is reserved here.
        }
        private bool ValidSource(uint id) => _extended ? (id & 0xFFFFFF00u) == 0x18DAF100u : id >= 0x7E8 && id <= 0x7EF;
        public Mode6SupportState SupportFor(uint source, byte monitor)
        {
            lock (_sync)
            {
                Peer peer;
                if ((monitor & 31) == 0 || !_peers.TryGetValue(source, out peer)) return Mode6SupportState.Undiscovered;
                int targetPage = monitor & 0xE0;
                for (int page = 0; page <= targetPage; page += 32)
                {
                    uint bits;
                    if (!peer.Pages.TryGetValue((byte)page, out bits))
                        return peer.Failures.ContainsKey((byte)page) ? Mode6SupportState.Inconclusive : Mode6SupportState.Undiscovered;
                    if (page == targetPage)
                        return (bits & (1u << (32 - (monitor - page)))) != 0 ? Mode6SupportState.Advertised : Mode6SupportState.NotAdvertised;
                    if ((bits & 1) == 0) return Mode6SupportState.NotAdvertised;
                }
                return Mode6SupportState.Undiscovered;
            }
        }
        public bool AllowsFirstFrame(uint source, byte monitor)
        {
            lock (_sync) return !_stopped && _pending != null && !_pending.IsAvailability &&
                _pending.ResponseSource == source && _pending.MonitorId == monitor &&
                SupportFor(source, monitor) == Mode6SupportState.Advertised;
        }
        public bool Accept(uint source, byte[] payload)
        {
            List<Mode6Result> results = null;
            lock (_sync)
            {
                var pending = _pending;
                if (_stopped || !ValidSource(source) || pending == null ||
                    (pending.ResponseSource.HasValue && pending.ResponseSource != source) || _answered.Contains(source))
                { _ignored++; return false; }
                Peer peer;
                bool known = _peers.TryGetValue(source, out peer);
                if (payload != null && payload.Length == 3 && payload[0] == 0x7F && payload[1] == 6)
                {
                    if (!known) { if (_peers.Count >= 16) { _ignored++; return false; } peer = new Peer(); _peers.Add(source, peer); }
                    peer.Status = "Negative response NRC 0x" + payload[2].ToString("X2") + " — support/result unverified";
                    if (payload[2] != 0x78) { peer.Failures[pending.MonitorId] = peer.Status; _answered.Add(source); }
                }
                else if (pending.IsAvailability)
                {
                    byte page; uint bits;
                    if (!TryBitmap(payload, out page, out bits) || page != pending.MonitorId)
                    { _ignored++; return false; }
                    if (!known) { if (_peers.Count >= 16) { _ignored++; return false; } peer = new Peer(); _peers.Add(source, peer); }
                    peer.Pages[page] = bits;
                    peer.Failures.Remove(page);
                    peer.Status = "Advertised bitmap 0x" + page.ToString("X2") + "=" + bits.ToString("X8");
                    _answered.Add(source);
                }
                else
                {
                    if (!known || SupportFor(source, pending.MonitorId) != Mode6SupportState.Advertised ||
                        !Mode6Response.TryRead(payload, out results) || results.Any(r => r.MonitorId != pending.MonitorId))
                    { _ignored++; return false; }
                    peer.Status = "Result received MID 0x" + pending.MonitorId.ToString("X2") + "; interpretation unverified";
                    _answered.Add(source);
                }
            }
            if (results != null) Results?.Invoke(source, results);
            Notify(); return true;
        }
        private bool Exchange(Mode6Request request, Action<Mode6Request> send,
            Func<int, CancellationToken, bool> wait, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_stopped || _requests >= MaximumRequests || _milliseconds() - _start >= MaximumMilliseconds) return false;
                _pending = request; _answered.Clear(); _requests++;
            }
            try
            {
                send(request);
                if (wait(ResponseWindowMilliseconds, cancellation)) cancellation.ThrowIfCancellationRequested();
                cancellation.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    Peer peer;
                    if (!request.ResponseSource.HasValue)
                        foreach (var item in _peers.Where(p => !_answered.Contains(p.Key)))
                        {
                            item.Value.Status = "No final discovery response — support unverified";
                            item.Value.Failures[0] = item.Value.Status;
                        }
                    if (request.ResponseSource.HasValue && !_answered.Contains(request.ResponseSource.Value) &&
                        _peers.TryGetValue(request.ResponseSource.Value, out peer))
                    {
                        peer.Status = "No response MID 0x" + request.MonitorId.ToString("X2") + " — unverified";
                        peer.Failures[request.MonitorId] = peer.Status;
                    }
                }
            }
            finally { lock (_sync) _pending = null; }
            Notify();
            if (wait(RequestGapMilliseconds, cancellation)) cancellation.ThrowIfCancellationRequested();
            return true;
        }
        public void Run(Action<Mode6Request> send, Func<int, CancellationToken, bool> wait,
            Func<byte[]> enabledMonitors, CancellationToken cancellation)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Start a new Mode06 discovery session for retry.");
            lock (_sync) { if (_stopped) return; }
            _start = _milliseconds();
            try
            {
                SetStatus("Discovering advertised Mode06 support");
                if (!Exchange(new Mode6Request(null, 0), send, wait, cancellation)) return;
                foreach (var source in Sources.Select(p => p.Source))
                {
                    for (int page = 0; page < 0xE0; page += 32)
                    {
                        var snapshot = Sources.First(p => p.Source == source); uint bits;
                        if (!snapshot.Availability.TryGetValue((byte)page, out bits) || (bits & 1) == 0) break;
                        if (!Exchange(new Mode6Request(source, (byte)(page + 32)), send, wait, cancellation))
                        { SetStatus("Discovery request/time limit reached; remaining support unverified"); return; }
                    }
                }
                SetStatus("Reading advertised implemented monitors — one bounded snapshot");
                int resultRequests = 0;
                foreach (var source in Sources.Select(p => p.Source))
                    foreach (byte monitor in enabledMonitors().Where(m => m >= 0xA2 && m <= 0xA9).Distinct().OrderBy(m => m))
                    {
                        if (!enabledMonitors().Contains(monitor) || SupportFor(source, monitor) != Mode6SupportState.Advertised) continue;
                        if (!Exchange(new Mode6Request(source, monitor), send, wait, cancellation))
                        { SetStatus("Result request/time limit reached; prior evidence retained"); return; }
                        resultRequests++;
                    }
                SetStatus(Sources.Length == 0 ? "No discovery response — support unknown" :
                    resultRequests == 0 ? "Discovery finished — no advertised enabled implemented monitors" :
                    "Snapshot finished — last observed results; SRX interpretation unverified");
            }
            catch (OperationCanceledException) { SetStatus("Cancelled — prior support/results retained"); }
            finally { lock (_sync) { _pending = null; _stopped = true; } }
        }
    }
}
