using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace J2534Diag
{
    // Bounded UI-accepted observations, not a lossless raw CAN recording.
    // All mutable state is locked because driver-error callbacks may arrive on workers.
    public enum FuelTrimEvidenceCounter
    {
        EligibleScalarFrames, NonScalarOrInvalidIgnored, CoalescedBeforeAcceptance,
        QueueCapacityDiscarded, InactiveOrGenerationDiscarded, UnavailableUiDiscarded,
        DecodeOrStateRejected, ReportObservationRejected
    }

    public sealed class FuelTrimSessionReport
    {
        private sealed class Reading { public double Value; public long Ticks; public string State; }
        private sealed class Row
        {
            public string Utc, Session, Event, Mode, Vin, Source, Pid, Name, Unit, Value, State, Age, Ticks, Details;
        }
        private static readonly byte[] Pids = { 0x0C, 6, 7, 8, 9, 0x11 };
        private readonly object _sync = new object();
        private readonly Queue<Row> _rows = new Queue<Row>();
        private readonly Dictionary<string, Dictionary<byte, Reading>> _latest = new Dictionary<string, Dictionary<byte, Reading>>();
        private readonly int _capacity;
        private readonly long _frequency;
        private string _session = "", _mode = "Not started", _vin = "";
        private bool _active;
        private long _discarded;
        private string _status = "Idle", _selectedSource = "";
        private readonly Dictionary<byte, long> _observed = Pids.ToDictionary(p => p, p => 0L);
        private readonly Dictionary<byte, long> _stale = Pids.ToDictionary(p => p, p => 0L);
        private readonly Dictionary<FuelTrimEvidenceCounter, long> _counters =
            Enum.GetValues(typeof(FuelTrimEvidenceCounter)).Cast<FuelTrimEvidenceCounter>().ToDictionary(c => c, c => 0L);
        public string SessionState { get { lock (_sync) return _status; } }
        public long AcceptedObservations { get { lock (_sync) return _observed.Values.Sum(); } }
        public long Counter(FuelTrimEvidenceCounter counter) { lock (_sync) return _counters[counter]; }
        public void Account(FuelTrimEvidenceCounter counter, int count = 1)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            lock (_sync) _counters[counter] += count;
        }
        public void SetStatus(string state, string detail, DateTime utc)
        {
            lock (_sync)
            {
                if (state == "Cleanup pending" && _status.StartsWith("Driver error", StringComparison.Ordinal))
                    state = "Driver error; cleanup pending";
                if (_status.StartsWith("Driver error", StringComparison.Ordinal) &&
                    !state.StartsWith("Driver error", StringComparison.Ordinal)) return;
                if ((_status == "Unanswered requests" || _status == "No enabled PIDs") && state == "Stopped") return;
                if (_status == state) return;
                _status = state;
                Add(utc, "SessionState", details: "State=" + state + ";Reason=" + detail);
            }
        }
        public void SelectSource(string source, DateTime utc)
        {
            lock (_sync)
            {
                source = source ?? "";
                if (source == _selectedSource) return;
                string previous = _selectedSource; _selectedSource = source;
                Add(utc, "SourceSelected", source, details: "Previous=" + previous + ";Selected=" + source);
            }
        }
        public void ChartsCleared(DateTime utc)
        { lock (_sync) Add(utc, "ChartsCleared", details: "Observed-history display only; observations retained; no ECU command."); }
        public string HealthText()
        {
            lock (_sync)
            {
                long dropped = _counters[FuelTrimEvidenceCounter.QueueCapacityDiscarded] +
                    _counters[FuelTrimEvidenceCounter.InactiveOrGenerationDiscarded] +
                    _counters[FuelTrimEvidenceCounter.UnavailableUiDiscarded] +
                    _counters[FuelTrimEvidenceCounter.DecodeOrStateRejected] +
                    _counters[FuelTrimEvidenceCounter.ReportObservationRejected];
                return "Session: " + _status + " | Accepted: " + _observed.Values.Sum() +
                    " | Coalesced: " + _counters[FuelTrimEvidenceCounter.CoalescedBeforeAcceptance] + " | Discarded: " + dropped;
            }
        }
        public FuelTrimSessionReport(long frequency, int capacity = 20000)
        {
            if (frequency <= 0 || capacity < 12) throw new ArgumentOutOfRangeException();
            _frequency = frequency; _capacity = capacity;
        }
        public int RetainedRows { get { lock (_sync) return _rows.Count; } }
        public long DiscardedRows { get { lock (_sync) return _discarded; } }
        private static string Name(byte pid)
        {
            switch (pid) { case 0x0C: return "Engine RPM"; case 6: return "STFT Bank 1";
                case 7: return "LTFT Bank 1"; case 8: return "STFT Bank 2";
                case 9: return "LTFT Bank 2"; default: return "Throttle Position"; }
        }
        private void Add(DateTime utc, string kind, string source = "", byte? pid = null,
            double? value = null, string state = "", double? age = null, long? ticks = null, string details = "")
        {
            if (_rows.Count == _capacity) { _rows.Dequeue(); _discarded++; }
            _rows.Enqueue(new Row { Utc = utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Session = _session, Event = kind, Mode = _mode, Vin = _vin, Source = source,
                Pid = pid.HasValue ? pid.Value.ToString("X2") : "", Name = pid.HasValue ? Name(pid.Value) : "",
                Unit = pid.HasValue ? (pid.Value == 0x0C ? "rpm" : "%") : "",
                Value = value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "",
                State = state, Age = age.HasValue ? age.Value.ToString("0.###", CultureInfo.InvariantCulture) : "",
                Ticks = ticks.HasValue ? ticks.Value.ToString(CultureInfo.InvariantCulture) : "", Details = details });
        }
        public void Begin(string mode, string vin, DateTime utc)
        {
            lock (_sync)
            {
                _latest.Clear(); _active = true; _status = "Running"; _selectedSource = ""; _session = Guid.NewGuid().ToString("N");
                _mode = mode ?? "Unknown"; _vin = vin ?? "";
                Add(utc, "SessionStart", details: "UI-accepted scalar observations; coalesced before acceptance; not raw CAN.");
                foreach (byte pid in Pids) Add(utc, "State", pid: pid, state: "Unknown");
            }
        }
        public bool Observe(string source, byte pid, double value, long ticks, DateTime acceptedUtc, long acceptedTicks)
        {
            lock (_sync)
            {
                if (!_active || string.IsNullOrEmpty(source) || !Pids.Contains(pid) || ticks < 0 || acceptedTicks < ticks ||
                    double.IsNaN(value) || double.IsInfinity(value)) return false;
                Dictionary<byte, Reading> values;
                if (!_latest.TryGetValue(source, out values))
                {
                    if (_latest.Count >= 16) return false;
                    values = new Dictionary<byte, Reading>(); _latest[source] = values;
                    foreach (byte p in Pids) Add(acceptedUtc, "State", source, p, state: "Unknown");
                }
                Reading previous;
                if (values.TryGetValue(pid, out previous) && ticks < previous.Ticks) return false;
                _observed[pid]++;
                string freshness = (acceptedTicks - ticks) / (double)_frequency <= 2 ? "Fresh" : "Stale";
                values[pid] = new Reading { Value = value, Ticks = ticks, State = freshness };
                Add(acceptedUtc, "Observation", source, pid, value, freshness,
                    age: (acceptedTicks - ticks) * 1000.0 / _frequency, ticks: ticks,
                    details: "UTC is UI acceptance time; ReceivedHostTicks is monotonic arrival time.");
                return true;
            }
        }
        public void Refresh(long now, DateTime utc)
        {
            lock (_sync)
            {
                if (!_active) return;
                foreach (var source in _latest)
                    foreach (var pair in source.Value)
                    {
                        Reading r = pair.Value;
                        if (now < r.Ticks || (now - r.Ticks) / (double)_frequency <= 2 || r.State == "Stale") continue;
                        r.State = "Stale";
                        _stale[pair.Key]++;
                        Add(utc, "State", source.Key, pair.Key, state: "Stale",
                            age: (now - r.Ticks) * 1000.0 / _frequency, ticks: r.Ticks);
                    }
            }
        }
        public void Stop(long now, DateTime utc, string reason)
        {
            lock (_sync)
            {
                if (!_active) return;
                SetStatus("Stopped", reason, utc);
                foreach (var source in _latest)
                    foreach (byte pid in Pids)
                    {
                        Reading r;
                        bool known = source.Value.TryGetValue(pid, out r);
                        Add(utc, "State", source.Key, pid, state: "Stopped",
                            age: known && now >= r.Ticks ? (double?)((now - r.Ticks) * 1000.0 / _frequency) : null,
                            ticks: known ? (long?)r.Ticks : null);
                    }
                Add(utc, "SessionStop", details: reason); _active = false;
            }
        }
        public void Event(string kind, string detail, DateTime utc)
        { lock (_sync) Add(utc, kind, details: detail ?? ""); }
        private static string Quote(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        public void Export(string path, string build, string exeSha256, string disconnectOutcome)
        {
            Row[] rows; Row[] summaries; long discarded; string counters, state;
            lock (_sync)
            {
                rows = _rows.ToArray(); discarded = _discarded; state = _status;
                counters = string.Join(";", _counters.Select(p => p.Key + "=" + p.Value.ToString(CultureInfo.InvariantCulture)));
                summaries = Pids.Select(pid => new Row { Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    Event = "PidSummary", Pid = pid.ToString("X2"), Name = Name(pid), Unit = pid == 0x0C ? "rpm" : "%",
                    Details = "AcceptedObservations=" + _observed[pid].ToString(CultureInfo.InvariantCulture) +
                        ";StaleTransitions=" + _stale[pid].ToString(CultureInfo.InvariantCulture) +
                        ";Scope=All report sessions and sources; counts survive row eviction" }).ToArray();
            }
            string full = Path.GetFullPath(path), temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                {
                    writer.WriteLine("Utc,SessionId,Event,Build,ExecutableSHA256,ConnectionMode,VIN,ResponseSource,PID,Name,Unit,Value,Freshness,AgeMs,ReceivedHostTicks,Details");
                    Action<Row> write = r => writer.WriteLine(string.Join(",", new[] { r.Utc,r.Session,r.Event,build,exeSha256,
                        r.Mode,r.Vin,r.Source,r.Pid,r.Name,r.Unit,r.Value,r.State,r.Age,r.Ticks,r.Details }.Select(Quote)));
                    write(new Row { Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), Event = "ExportMetadata",
                        Details = "Schema=2;FreshnessSeconds=2;HostTickFrequency=" + _frequency.ToString(CultureInfo.InvariantCulture) +
                            ";RetainedRows=" + rows.Length.ToString(CultureInfo.InvariantCulture) + ";DiscardedOldestRows=" +
                            discarded.ToString(CultureInfo.InvariantCulture) + ";DisconnectOutcome=" + (disconnectOutcome ?? "Not attempted") +
                            ";SessionState=" + state + ";" + counters +
                            ";Counters are report-lifetime boundary counts, not proof of bus losslessness" +
                            ";UTC may jump; rows retain event order; freshness uses monotonic ticks" +
                            ";Age is recorded at state transition, not inferred at export; address is not ECU identity." });
                    foreach (Row row in rows) write(row);
                    foreach (Row summary in summaries) write(summary);
                }
                if (File.Exists(full)) File.Replace(temp, full, null);
                else File.Move(temp, full);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
