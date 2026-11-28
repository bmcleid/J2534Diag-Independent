using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using J2534Diag;
using SAE.J2534;
using static System.Net.Mime.MediaTypeNames;

namespace J2534Diag
{
    public partial class MisfireForm : Form
    {

        private J2534Manager j2534Manager;
        private readonly ComboBox _sourceSelect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly Label _status = new Label { AutoSize = true, Text = "Unknown — no Mode06 observations" };
        private readonly Dictionary<uint, Dictionary<int, Mode6Result>> _observed =
            new Dictionary<uint, Dictionary<int, Mode6Result>>();
        private int _generation;
        private int _activeGeneration;
        private FuelTrimPollingGate _pollGate;
        private string _lastFailure;
        private readonly ToolTip _details;
        private volatile bool _acceptFrames;
        private readonly object _frameSync = new object();
        private const string ReceiveOwner = "MISFIRE";
        private bool _ownsReceiveChannel;
        private Thread _monitorThread;
        private Thread _pollingThread;
        private CancellationTokenSource _cts;
        private IsoTpParser _isoTpParser;


        public MisfireForm(J2534Manager j2534Manager)
        {
            InitializeComponent();
            this.j2534Manager = j2534Manager;
            if (components == null) components = new System.ComponentModel.Container();
            _details = new ToolTip(components);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            bar.Controls.Add(new Label { AutoSize = true,
                Text = "Experimental Mode06 • GM reference labels; SRX support unverified. Results are not live counters." });
            bar.Controls.Add(_sourceSelect); bar.Controls.Add(_status);
            Controls.Add(bar); bar.BringToFront();
            _sourceSelect.SelectedIndexChanged += (sender, e) => RefreshObservedValues();
            _sourceSelect.Format += (sender, e) => { if (e.ListItem is uint) e.Value = "Source 0x" + ((uint)e.ListItem).ToString("X"); };
            _sourceSelect.FormattingEnabled = true;
            SetupMisfireDisplayControls();

            _isoTpParser = new IsoTpParser();
            _isoTpParser.FrameReceived += IsoTpParser_FrameReceived;

            this.Load += MisfireForm_Load;
            this.FormClosing += MisfireForm_FormClosing;
        }

        private void MisfireForm_Load(object sender, EventArgs e)
        {
            // Background polling starts only when this tab is selected.
        }

        private void MisfireForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopListening();
            if (_ownsReceiveChannel) e.Cancel = true;
        }

        private void StartApiAndThreads()
        {
            // This screen transmits Mode $06 requests; never start in capture-only mode.
            if (j2534Manager.CaptureOnlyMode) return;
            if (j2534Manager.Channel == null || j2534Manager.Channel.IsDisposed) return;
            if (!j2534Manager.ReceiveGate.TryAcquire(ReceiveOwner))
            {
                Debug.WriteLine("Misfire monitor cannot start; receiver owned by " + j2534Manager.ReceiveGate.CurrentOwner);
                return;
            }
            _ownsReceiveChannel = true;
            _cts = new CancellationTokenSource();
            var cancellation = _cts;
            _pollGate = new FuelTrimPollingGate(500, 100, 6);
            var pollGate = _pollGate;
            _activeGeneration = ++_generation;
            int generation = _activeGeneration;
            _lastFailure = null;
            _observed.Clear(); _sourceSelect.Items.Clear(); RefreshObservedValues();
            lock (_frameSync) { _isoTpParser.Reset(); _acceptFrames = true; }
            _status.Text = "Awaiting replies; support not established";
            try
            {

            // Set up filters, etc.
            MessageFilter passFilter = j2534Manager.BitMode == BitType.BITS_11 ? new MessageFilter()
            {
                FilterType = Filter.PASS_FILTER,
                Mask = new byte[] { 0xFF, 0xFF, 0xFF, 0xF8 },
                Pattern = new byte[] { 0x00, 0x00, 0x07, 0xE8 },
                FlowControl = new byte[] { 0x00, 0x00, 0x07, 0xE0 },
                TxFlags = TxFlag.NONE
            } : new MessageFilter()
            {
                FilterType = Filter.PASS_FILTER,
                Mask = new byte[] { 0xFF, 0xFF, 0xFF, 0x00 },
                Pattern = new byte[] { 0x18, 0xDA, 0xF1, 0x00 },
                FlowControl = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                TxFlags = TxFlag.CAN_29BIT_ID
            };


            j2534Manager.Channel.DefaultTxFlag = j2534Manager.BitMode == BitType.BITS_11 ? TxFlag.NONE : TxFlag.CAN_29BIT_ID;
            j2534Manager.Channel.ClearMsgFilters();
            j2534Manager.Channel.StartMsgFilter(passFilter);
            _monitorThread = new Thread(() => ChannelLifecyclePolicy.RunCoupledWorker(cancellation, () =>
            {
                while (!cancellation.IsCancellationRequested && j2534Manager.Channel != null &&
                    !j2534Manager.Channel.IsDisposed)
                {
                    var response = j2534Manager.Channel.GetMessages(200, 2);
                    foreach (var message in response.Messages)
                    {
                        if (cancellation.IsCancellationRequested) return;
                        ProcessFrame(message.Data, unchecked((uint)message.RxStatus),
                            j2534Manager.BitMode == BitType.BITS_29, generation);
                    }
                    if (cancellation.Token.WaitHandle.WaitOne(1)) return;
                }
            }, ex => WorkerFailed(ex, generation))) { IsBackground = true };
            _pollingThread = new Thread(() => ChannelLifecyclePolicy.RunCoupledWorker(cancellation, () =>
            {
                pollGate.Run(() => Mode6TidMap.MisfireTests.Where(p => p.Enabled)
                    .Select(p => p.MonitorId).Distinct().ToArray(), monitor =>
                {
                    if (cancellation.IsCancellationRequested) return;
                    var request = new byte[12];
                    byte[] id = j2534Manager.BitMode == BitType.BITS_11 ?
                        new byte[] { 0, 0, 7, 0xDF } : new byte[] { 0x18, 0xDB, 0x33, 0xF1 };
                    Array.Copy(id, request, 4); request[4] = 2; request[5] = 6; request[6] = monitor;
                    j2534Manager.SendDiagnosticMessage(request);
                }, cancellation.Token);
                if (!cancellation.IsCancellationRequested)
                    Post(generation, () => _status.Text = pollGate.LastStopReason + "; last observed values retained");
            }, ex => WorkerFailed(ex, generation))) { IsBackground = true };
            _monitorThread.Start(); _pollingThread.Start();
            }
            catch (Exception ex)
            {
                WorkerFailed(ex, generation);
                StopListening();
            }
        }

        private void WorkerFailed(Exception error, int generation)
        {
            _lastFailure = error.GetType().Name + ": " + error.Message;
            Post(generation, () => _status.Text = "Worker error — " + _lastFailure);
        }
        private void Post(int generation, Action action)
        {
            Action guarded = () => { if (generation == _generation && !IsDisposed) action(); };
            if (IsDisposed || !IsHandleCreated) return;
            try { if (InvokeRequired) BeginInvoke(guarded); else guarded(); }
            catch (InvalidOperationException) { /* Window is retiring; no provider cleanup here. */ }
        }
        private void ProcessFrame(byte[] frame, uint rxStatus, bool extended, int generation)
        {
            uint source; byte[] flowControl;
            if (generation != _activeGeneration || !Mode6Response.TrySource(frame, rxStatus, extended,
                out source, out flowControl)) return;
            lock (_frameSync) { if (_acceptFrames) _isoTpParser.ProcessMessage(frame); }
        }

        private void IsoTpParser_FrameReceived(object sender, IsoTpFrameEventArgs e)
        {
            if (e.CanId == null || e.CanId.Length != 4 || e.Payload == null || e.Payload.Length < 2) return;
            uint source = ((uint)e.CanId[0] << 24) | ((uint)e.CanId[1] << 16) |
                ((uint)e.CanId[2] << 8) | e.CanId[3];
            if (e.Payload[0] != 0x46) return;
            if (e.FrameType == IsoTpFrameType.FirstFrame)
            {
                if (!Mode6TidMap.MisfireTests.Any(p => p.MonitorId == e.Payload[1])) return;
                uint target = source <= 0x7FF ? source - 8 : 0x18DA00F1u | ((source & 255u) << 8);
                j2534Manager.SendFlowControl(new[] { (byte)(target >> 24), (byte)(target >> 16),
                    (byte)(target >> 8), (byte)target }, blockSize: 0, separationTime: 0);
                return;
            }
            if (!e.IsComplete) return;
            List<Mode6Result> records;
            if (!Mode6Response.TryRead(e.Payload, out records)) return;
            foreach (var record in records.Where(r => r.IsReferenceMisfireCount))
                _pollGate?.Observe(record.MonitorId);
            int generation = _activeGeneration;
            Post(generation, () => ApplyResults(source, records));
        }

        private readonly Dictionary<int, PidDisplayControl> _misfireControls =
            new Dictionary<int, PidDisplayControl>();

        private void ApplyResults(uint source, List<Mode6Result> records)
        {
            Dictionary<int, Mode6Result> values;
            if (!_observed.TryGetValue(source, out values))
            {
                if (_observed.Count >= 16) return;
                values = new Dictionary<int, Mode6Result>(); _observed.Add(source, values);
                _sourceSelect.Items.Add(source);
                if (_sourceSelect.SelectedIndex < 0) _sourceSelect.SelectedIndex = 0;
            }
            foreach (var record in records)
                if (_misfireControls.ContainsKey(((record.MonitorId << 8) | record.TestId)))
                    values[((record.MonitorId << 8) | record.TestId)] = record;
            RefreshObservedValues();
            _status.Text = "Last observed Mode06 results; mapping/support on SRX unverified";
        }
        private void RefreshObservedValues()
        {
            Dictionary<int, Mode6Result> values = null;
            if (_sourceSelect.SelectedItem is uint) _observed.TryGetValue((uint)_sourceSelect.SelectedItem, out values);
            foreach (var pair in _misfireControls)
            {
                Mode6Result result;
                bool found = values != null && values.TryGetValue(pair.Key, out result);
                if (!found) { pair.Value.ValueText = "Unknown"; pair.Value.Unit = "counts (GM ref.)"; pair.Value.AccessibleDescription = "No observed result"; _details.SetToolTip(pair.Value, "No observed result"); continue; }
                result = values[pair.Key];
                pair.Value.ValueText = result.IsReferenceMisfireCount ? result.RawValue.ToString() : "Unknown units";
                pair.Value.Unit = result.IsReferenceMisfireCount ? "counts (GM ref.)" : "unverified";
                pair.Value.AccessibleDescription = "MID=" + result.MonitorId.ToString("X2") +
                    "; TID=" + result.TestId.ToString("X2") + "; UASID=" + result.UnitsAndScalingId.ToString("X2") +
                    "; RawValue=" + result.RawValue + "; RawMin=" + result.RawMinimum + "; RawMax=" + result.RawMaximum +
                    "; no pass/fail interpretation";
                _details.SetToolTip(pair.Value, pair.Value.AccessibleDescription);
            }
        }

        private void SetupMisfireDisplayControls()
        {
            foreach (var tid in Mode6TidMap.MisfireTests)
            {
                var control = new PidDisplayControl
                {
                    PidName = tid.Label,
                    Unit = "counts (GM ref.)",
                    IsEnabled = tid.Enabled,
                    ValueText = "Unknown"
                };

                control.IsEnabledChanged += (sender, e) => tid.Enabled = control.IsEnabled;
                flpMisfires.Controls.Add(control);
                _misfireControls[((tid.MonitorId << 8) | tid.TestId)] = control;
            }
        }


        public void StartListening()
        {
            if (j2534Manager.CaptureOnlyMode) return;
            if (!_ownsReceiveChannel && (_monitorThread == null || !_monitorThread.IsAlive))
            {
                StartApiAndThreads();
            }
        }

        public void StopListening()
        {
            if (!_ownsReceiveChannel) return;
            _cts?.Cancel();
            _acceptFrames = false;
            ++_generation;
            bool pollingStopped = ChannelLifecyclePolicy.RequestStopAndWait(_pollingThread, _cts, 3000);
            bool monitorStopped = ChannelLifecyclePolicy.RequestStopAndWait(_monitorThread, _cts, 3000);
            if (!pollingStopped || !monitorStopped)
            {
                _status.Text = "Shutdown pending — connection retained";
                return;
            }
            _pollingThread = null;
            _monitorThread = null;
            try
            {
                if (j2534Manager.Channel != null && !j2534Manager.Channel.IsDisposed)
                    j2534Manager.Channel.ClearMsgFilters();
            }
            catch (Exception ex)
            {
                _status.Text = "Cleanup failed — connection retained: " + ex.Message;
                return;
            }
            lock (_frameSync) _isoTpParser.Reset();
            _pollGate?.Dispose(); _pollGate = null;
            _status.Text = _lastFailure == null ? "Stopped — last observed results retained" : "Stopped after error — " + _lastFailure;
            _cts?.Dispose();
            _cts = null;
            _ownsReceiveChannel = false;
            j2534Manager.ReceiveGate.Release(ReceiveOwner);
        }
    }
}
