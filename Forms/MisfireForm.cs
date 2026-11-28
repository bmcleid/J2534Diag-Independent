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
        private readonly Label _status = new Label { AutoSize = true, Text = "Undiscovered — no Mode06 support/results" };
        private readonly Dictionary<uint, Dictionary<int, Mode6Result>> _observed =
            new Dictionary<uint, Dictionary<int, Mode6Result>>();
        private int _generation;
        private int _activeGeneration;
        private Mode6DiscoverySession _mode6Session;
        private Action<byte[]> _sendMode6;
        private Func<int, CancellationToken, bool> _waitMode6;
        private bool _extendedMode6;
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
            _sendMode6 = frame => j2534Manager.SendDiagnosticMessage(frame);
            _waitMode6 = (milliseconds, token) => token.WaitHandle.WaitOne(milliseconds);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            bar.Controls.Add(new Label { AutoSize = true,
                Text = "Mode06 advertised support ≠ diagnosis • GM labels unverified • not live counters" });
            bar.Controls.Add(_sourceSelect); bar.Controls.Add(_status);
            // Put the toolbar in its own measured row; a sibling Fill panel can
            // otherwise consume the entire embedded form and hide docked controls.
            tlpRight.Controls.Remove(flpMisfires);
            tlpRight.RowCount = 2; tlpRight.RowStyles.Clear();
            tlpRight.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tlpRight.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            bar.Dock = DockStyle.Fill; bar.Margin = Padding.Empty;
            tlpRight.Controls.Add(bar, 0, 0);
            tlpRight.Controls.Add(flpMisfires, 0, 1);
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
            BeginMode6Session();
            int generation = _activeGeneration;
            _status.Text = "Discovering support — results gated by responder advertisement";
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
                RunMode6Polling(cancellation.Token, generation);
            }, ex => WorkerFailed(ex, generation))) { IsBackground = true };
            _monitorThread.Start(); _pollingThread.Start();
            }
            catch (Exception ex)
            {
                WorkerFailed(ex, generation);
                StopListening();
            }
        }

        private void BeginMode6Session()
        {
            _activeGeneration = ++_generation;
            int generation = _activeGeneration;
            _extendedMode6 = j2534Manager.BitMode == BitType.BITS_29;
            var session = new Mode6DiscoverySession(_extendedMode6);
            _mode6Session = session;
            _lastFailure = null;
            _observed.Clear(); _sourceSelect.Items.Clear(); RefreshObservedValues();
            lock (_frameSync) { _isoTpParser.Reset(); _acceptFrames = true; }
            session.Changed += () => Post(generation, RefreshDiscovery);
            session.Results += (source, records) => Post(generation, () => ApplyResults(source, records));
        }
        private void RunMode6Polling(CancellationToken token, int generation)
        {
            var session = _mode6Session;
            session.Run(request =>
            {
                if (generation != _generation) throw new OperationCanceledException();
                _sendMode6(request.ToCanFrame(_extendedMode6));
            }, _waitMode6, () => Mode6TidMap.MisfireTests.Where(p => p.Enabled)
                .Select(p => p.MonitorId).Distinct().ToArray(), token);
        }
        private void RefreshDiscovery()
        {
            if (_mode6Session == null) return;
            foreach (var source in _mode6Session.Sources)
                if (!_sourceSelect.Items.Contains(source.Source)) _sourceSelect.Items.Add(source.Source);
            if (_sourceSelect.SelectedIndex < 0 && _sourceSelect.Items.Count > 0) _sourceSelect.SelectedIndex = 0;
            RefreshObservedValues();
            _status.Text = _mode6Session.Status;
            _status.AccessibleDescription = string.Join(" | ", _mode6Session.Sources.Select(p => "0x" + p.Source.ToString("X") + ": " + p.Status));
            _details.SetToolTip(_status, _status.AccessibleDescription);
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
            if (e.FrameType == IsoTpFrameType.FirstFrame)
            {
                if (e.Payload[0] != 0x46) return;
                if (_mode6Session == null || !_mode6Session.AllowsFirstFrame(source, e.Payload[1])) return;
                uint target = source <= 0x7FF ? source - 8 : 0x18DA00F1u | ((source & 255u) << 8);
                j2534Manager.SendFlowControl(new[] { (byte)(target >> 24), (byte)(target >> 16),
                    (byte)(target >> 8), (byte)target }, blockSize: 0, separationTime: 0);
                return;
            }
            if (!e.IsComplete) return;
            _mode6Session?.Accept(source, e.Payload);
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
                if (!_sourceSelect.Items.Contains(source)) _sourceSelect.Items.Add(source);
                if (_sourceSelect.SelectedIndex < 0) _sourceSelect.SelectedIndex = 0;
            }
            foreach (var record in records)
                if (_misfireControls.ContainsKey(((record.MonitorId << 8) | record.TestId)))
                    values[((record.MonitorId << 8) | record.TestId)] = record;
            RefreshObservedValues();
            RefreshDiscovery();
        }
        private void RefreshObservedValues()
        {
            Dictionary<int, Mode6Result> values = null;
            if (_sourceSelect.SelectedItem is uint) _observed.TryGetValue((uint)_sourceSelect.SelectedItem, out values);
            foreach (var pair in _misfireControls)
            {
                Mode6Result result;
                bool found = values != null && values.TryGetValue(pair.Key, out result);
                if (!found)
                {
                    Mode6SupportState support = _sourceSelect.SelectedItem is uint && _mode6Session != null ?
                        _mode6Session.SupportFor((uint)_sourceSelect.SelectedItem, (byte)(pair.Key >> 8)) : Mode6SupportState.Undiscovered;
                    pair.Value.ValueText = support == Mode6SupportState.NotAdvertised ? "Not advertised" :
                        support == Mode6SupportState.Advertised ? "No result" :
                        support == Mode6SupportState.Inconclusive ? "Unverified" : "Undiscovered";
                    pair.Value.Unit = "counts (GM ref.)";
                    pair.Value.AccessibleDescription = support + "; no observed TID result; monitor support is not proof of TID/scaling validity";
                    _details.SetToolTip(pair.Value, pair.Value.AccessibleDescription); continue;
                }
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
                    ValueText = "Undiscovered"
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
            _mode6Session?.Stop();
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
            _status.Text = _lastFailure == null ? "Stopped — last observed results retained" : "Stopped after error — " + _lastFailure;
            _cts?.Dispose();
            _cts = null;
            _ownsReceiveChannel = false;
            j2534Manager.ReceiveGate.Release(ReceiveOwner);
        }
    }
}
