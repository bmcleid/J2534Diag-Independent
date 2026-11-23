using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SAE.J2534;

namespace J2534Diag
{
    public partial class FuelTrimForm : Form
    {
        public struct MSG
        {
            public string id;
            public string data;
        }
        private J2534Manager j2534Manager;

        private bool useImperialUnits = true; // Set to true for imperial, false for metric
        private ConcurrentDictionary<byte, PidDisplayControl> _pidControls = new ConcurrentDictionary<byte, PidDisplayControl>();
        readonly List<ObdPid> ObdPids;
        readonly List<UdsPid> UdsPids;
        private readonly Dictionary<byte, UdsPid> _udsPidsByDidLowByte;

        private const string ReceiveOwner = "FUEL_TRIM";
        private bool _ownsReceiveChannel;
        private Thread _monitorThread;
        private Thread _pollingThread;
        private CancellationTokenSource _cts;
        private const int ThrottleBins = 10;
        private const int RpmBins = 10;
        private const int cellHeight = 40; // or any value you prefer

        private double?[,] trimGridBank1 = new double?[ThrottleBins, RpmBins];
        private double?[,] trimGridBank2 = new double?[ThrottleBins, RpmBins];

        private readonly FuelTrimSampleState _samples = new FuelTrimSampleState(Stopwatch.Frequency * 2);
        private readonly ComboBox _responseSource = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 285
        };
        private System.Windows.Forms.Timer _freshnessTimer;
        private int _displayGeneration;
        private readonly object _sampleQueueLock = new object();
        private readonly Dictionary<string, Tuple<FuelTrimInput, long, int>> _pendingSamples =
            new Dictionary<string, Tuple<FuelTrimInput, long, int>>();
        private bool _sampleRefreshQueued;
        public bool BatchMode { get; private set; } = false;

        public FuelTrimForm(J2534Manager j2534Manager)
        {
            InitializeComponent();

            SetupGrids();

            ObdPids = ObdPidMap.GetObdPids(useImperialUnits);
            UdsPids = UdsPidMap.GetUdsPids(useImperialUnits);
            _udsPidsByDidLowByte = UdsPids.ToDictionary(pid => pid.DidLowByte);
            SetupPidDisplayControls();
            flpActions.Controls.Add(new Label { Text = "Response source:", AutoSize = true });
            flpActions.Controls.Add(_responseSource);
            flpActions.Controls.Add(new Label
            {
                Text = "Mode 01 polling only. 2 s freshness (app policy). Grids show observed history; address is not ECU identity.",
                AutoSize = true
            });
            tlpRight.RowStyles[1].Height = 76;
            lblBank1.Text = "Bank 1 observed history";
            lblBank2.Text = "Bank 2 observed history";
            _responseSource.SelectedIndexChanged += (sender, args) =>
            {
                trimGridBank1 = new double?[ThrottleBins, RpmBins];
                trimGridBank2 = new double?[ThrottleBins, RpmBins];
                RefreshFuelDisplay(false);
            };
            _freshnessTimer = new System.Windows.Forms.Timer(components) { Interval = 250 };
            _freshnessTimer.Tick += (sender, args) =>
            {
                if (_cts != null && _cts.IsCancellationRequested) _samples.Clear();
                RefreshFuelDisplay(false);
            };
            _freshnessTimer.Start();
            RefreshFuelDisplay(false);

            // Start everything after the form is loaded
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
            this.j2534Manager = j2534Manager;
        }

        private void SetupPidDisplayControls()
        {
            foreach (var pid in ObdPids)
            {
                var pidControl = new PidDisplayControl
                {
                    PidName = pid.Name,
                    Unit = pid.Unit,
                    IsEnabled = pid.Enabled
                };
                pidControl.IsEnabledChanged += (s, e) => pid.Enabled = pidControl.IsEnabled;
                flpPids.Controls.Add(pidControl);
                _pidControls[pid.Pid] = pidControl; // Add this line
            }
        }

        private void SetupGrids()
        {
            // dgvTrimGridBank1
            dgvTrimGridBank1.CellFormatting += TrimGrid_CellFormatting;
            dgvTrimGridBank1.CellPainting += TrimGrid_CellPainting;
            dgvTrimGridBank1.RowTemplate.Height = cellHeight;

            //dgvTrimGridBank2
            dgvTrimGridBank2.CellFormatting += TrimGrid_CellFormatting;
            dgvTrimGridBank2.CellPainting += TrimGrid_CellPainting;
            dgvTrimGridBank2.RowTemplate.Height = cellHeight;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
        }

        private void SendSessionRequest(params byte[] data)
        {
            var request = new byte[12];
            int len = Math.Min(data.Length, 12);
            Array.Copy(data, request, len);
            // The rest of request[] is already 0x00 by default
            j2534Manager.SendDiagnosticMessage(request);
            Thread.Sleep(10); // Give some time for the request to be sent
        }

        private void btnClearFuelTrims_Click(object sender, EventArgs e)
        {
            trimGridBank1 = new double?[ThrottleBins, RpmBins];
            trimGridBank2 = new double?[ThrottleBins, RpmBins];
            UpdateTrimGridDisplay();
            //This attempt wasgetting 7F failure
            //var clearFuelTrims = new byte[12];
            //clearFuelTrims[0] = 0x14;
            //clearFuelTrims[1] = 0xDA;
            //clearFuelTrims[2] = 0x11;
            //clearFuelTrims[3] = 0xF1;

            ////clearFuelTrims[0] = 0x18;
            ////clearFuelTrims[1] = 0xDB;
            ////clearFuelTrims[2] = 0x33;
            ////clearFuelTrims[3] = 0xF1;

            //clearFuelTrims[4] = 0x03;    // Length: 3 bytes follow (service + 2 bytes RID)
            //clearFuelTrims[5] = 0x31;    // Service: RoutineControl
            //clearFuelTrims[6] = 0x01;    // Subfunction: StartRoutine
            //clearFuelTrims[7] = 0xFF;    // Routine Identifier High byte
            //clearFuelTrims[8] = 0x00;    // Routine Identifier Low byte
            //                             // clearFuelTrims[9]..[11] remain 0x00

            //j2534Manager.Channel.SendMessage(clearFuelTrims);
        }


        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Signal threads to stop
            StopListening();
        }

        private void StartApiAndThreads()
        {
            // Defense in depth: this page sends OBD/UDS requests.
            if (j2534Manager.CaptureOnlyMode) return;
            if (j2534Manager.Channel == null || j2534Manager.Channel.IsDisposed) return;
            if (!j2534Manager.ReceiveGate.TryAcquire(ReceiveOwner))
            {
                Debug.WriteLine("Fuel trim cannot start; receiver owned by " + j2534Manager.ReceiveGate.CurrentOwner);
                return;
            }
            _samples.Clear();
            _responseSource.Items.Clear();
            trimGridBank1 = new double?[ThrottleBins, RpmBins];
            trimGridBank2 = new double?[ThrottleBins, RpmBins];
            int generation = Interlocked.Increment(ref _displayGeneration);
            RefreshFuelDisplay(false);
            _ownsReceiveChannel = true;
            _cts = new CancellationTokenSource();
            var sessionCancellation = _cts;

            try
            {

                // Set up filters, etc.
                MessageFilter passFilter = j2534Manager.BitMode == BitType.BITS_11 ? new MessageFilter()
                {
                    FilterType = Filter.PASS_FILTER,
                    Mask = new byte[] { 0xF0, 0xFF, 0xFF, 0x00 },
                    Pattern = new byte[] { 0x00, 0x00, 0x07, 0xD0 },
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

                //Start background CAN monitor
                _monitorThread = new Thread(() => ChannelLifecyclePolicy.RunCoupledWorker(sessionCancellation, () =>
                {
                    while (!sessionCancellation.IsCancellationRequested && j2534Manager.Channel != null && !j2534Manager.Channel.IsDisposed)
                    {
                        try
                        {
                            var resp = j2534Manager?.Channel?.GetMessages(200, 2);
                            //Debug.WriteLine($"Received {resp.Messages.Length} messages at {DateTime.Now:HH:mm:ss.fff}");
                            foreach (var msg in resp.Messages)
                            {
                                if (sessionCancellation.IsCancellationRequested || j2534Manager.Channel == null || j2534Manager.Channel.IsDisposed)
                                {
                                    return;
                                }
                                // Process immediately
                                QueueScalarSample(msg.Data, unchecked((uint)msg.RxStatus), generation);
                            }
                        }
                        catch
                        {
                            return;
                        }
                        Thread.Sleep(1);
                    }
                }, ex => Debug.WriteLine("Fuel trim worker failed: " + ex)))
                { IsBackground = true };
                _monitorThread.Start();

                // Start polling thread
                _pollingThread = new Thread(() => ChannelLifecyclePolicy.RunCoupledWorker(sessionCancellation, () =>
                {

                    while (!sessionCancellation.IsCancellationRequested && j2534Manager.Channel != null && !j2534Manager.Channel.IsDisposed)
                    {
                        // Do not probe UDS-F4 as a fallback. Its applicability is
                        // unverified for this vehicle; missing OBD replies stay unknown.
                        foreach (var pid in ObdPids.Where(p => p.Enabled))
                        {
                            if (sessionCancellation.IsCancellationRequested || j2534Manager.Channel == null ||
                                j2534Manager.Channel.IsDisposed) break;
                            var request = BuildObdPidRequest(pid, j2534Manager.BitMode);
                            if (sessionCancellation.IsCancellationRequested || j2534Manager.Channel == null ||
                                j2534Manager.Channel.IsDisposed) return;
                            try
                            {
                                j2534Manager.SendDiagnosticMessage(request);
                            }
                            catch (J2534Exception ex)
                            {
                                Debug.WriteLine($"Error sending message: {ex.Message}");
                                return;
                            }
                            Thread.Sleep(2);
                        }
                        Thread.Sleep(5);

                    }
                }, ex => Debug.WriteLine("Fuel trim worker failed: " + ex)))
                { IsBackground = true };
                _pollingThread.Start();
            }
            catch (J2534Exception ex)
            {
                Debug.WriteLine($"Error starting API and threads: {ex.Message}");
                StopListening();
            }
        }

        private void QueueScalarSample(byte[] data, uint rxStatus, int generation)
        {
            FuelTrimInput input;
            if (!FuelTrimScalarFrame.TryRead(data, rxStatus, out input)) return;
            long received = Stopwatch.GetTimestamp();
            if (!IsHandleCreated || IsDisposed || Disposing) return;
            lock (_sampleQueueLock)
            {
                string key = input.SourceKey + "/" + input.Pid;
                if (!_pendingSamples.ContainsKey(key) && _pendingSamples.Count >= 96) return;
                // Latest scalar observation per address/protocol/PID, not a raw logger.
                _pendingSamples[key] = Tuple.Create(input, received, generation);
                if (_sampleRefreshQueued) return;
                _sampleRefreshQueued = true;
            }
            try { BeginInvoke(new Action(FlushScalarSamples)); }
            catch (InvalidOperationException)
            {
                lock (_sampleQueueLock)
                {
                    _pendingSamples.Clear();
                    _sampleRefreshQueued = false;
                }
            }
        }

        private void FlushScalarSamples()
        {
            List<Tuple<FuelTrimInput, long, int>> pending;
            lock (_sampleQueueLock)
            {
                pending = _pendingSamples.Values.ToList();
                _pendingSamples.Clear();
                _sampleRefreshQueued = false;
            }
            if (IsDisposed || Disposing || !_ownsReceiveChannel ||
                _cts == null || _cts.IsCancellationRequested) return;
            bool accepted = false;
            foreach (var queued in pending)
            {
                if (queued.Item3 != _displayGeneration) continue;
                FuelTrimInput input = queued.Item1;
                double value;
                if (input.Protocol == "Mode01")
                {
                    var definition = ObdPids.FirstOrDefault(p => p.Pid == input.Pid);
                    if (definition == null) continue;
                    value = definition.Formula(input.Data);
                }
                else
                {
                    UdsPid definition;
                    if (!_udsPidsByDidLowByte.TryGetValue(input.Pid, out definition)) continue;
                    value = definition.Formula(input.Data);
                }
                if (!_samples.Update(input.SourceKey, input.Pid, value, queued.Item2)) continue;
                if (!_responseSource.Items.Contains(input.SourceKey))
                {
                    _responseSource.Items.Add(input.SourceKey);
                    if (_responseSource.SelectedIndex < 0) _responseSource.SelectedIndex = 0;
                }
                accepted = true;
            }
            RefreshFuelDisplay(accepted);
        }

        byte[] BuildObdPidRequest(ObdPid pid, BitType bitMode)
        {
            // 4 bytes ArbId, 1 byte Sid, 1 byte Mode, 1 byte Pid, pad with 0x00 to 12 bytes
            var request = new byte[12];
            if(bitMode == BitType.BITS_11)
            {
                Array.Copy(pid.ArbId, 0, request, 0, 4);
            }
            else
            {
                request[0] = 0x18;
                request[1] = 0xDB;
                request[2] = 0x33;
                request[3] = 0xF1;
            }
            request[4] = pid.Sid;
            request[5] = pid.Mode;
            request[6] = pid.Pid;
            // The rest (request[7]..request[11]) are already 0x00 by default
            return request;
        }

        private void RefreshFuelDisplay(bool recordHistory)
        {
            var snapshot = _samples.Snapshot(_responseSource.SelectedItem as string, Stopwatch.GetTimestamp());
            foreach (var definition in ObdPids)
            {
                PidDisplayControl control;
                if (_pidControls.TryGetValue(definition.Pid, out control))
                {
                    double? value = snapshot.Value(definition.Pid);
                    control.ValueText = value.HasValue
                        ? value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                        : "Unknown / stale";
                }
            }
            if (recordHistory)
            {
                for (int bank = 1; bank <= 2; bank++)
                {
                    double throttle, rpm, total;
                    if (!snapshot.TryBank(bank, out throttle, out rpm, out total)) continue;
                    int tBin = Math.Min((int)(throttle / 10.0), ThrottleBins - 1);
                    int rBin = Math.Min((int)(rpm / 500.0), RpmBins - 1);
                    (bank == 1 ? trimGridBank1 : trimGridBank2)[tBin, rBin] = total;
                }
            }
            UpdateTrimGridDisplay();
        }

        private void UpdateTrimGridDisplay()
        {
            UpdateSingleTrimGridDisplay(dgvTrimGridBank1, trimGridBank1, "Bank 1");
            UpdateSingleTrimGridDisplay(dgvTrimGridBank2, trimGridBank2, "Bank 2");
        }

        private void UpdateSingleTrimGridDisplay(DataGridView dgv, double?[,] grid, string bankLabel)
        {
            if (dgv.InvokeRequired)
            {
                dgv.Invoke(new Action(() => UpdateSingleTrimGridDisplay(dgv, grid, bankLabel)));
                return;
            }

            dgv.SuspendLayout();

            int cellHeight = 40;
            int cellWidth = (int)(cellHeight);

            // Setup columns if not already done
            if (dgv.ColumnCount != RpmBins)
            {
                dgv.Columns.Clear();
                for (int r = 0; r < RpmBins; r++)
                {
                    int rpmStart = r * 500;
                    int rpmEnd = (r + 1) * 500;
                    dgv.Columns.Add($"rpm{r}", $"{rpmStart}");
                    dgv.Columns[r].Width = cellWidth;
                }
            }

            // Setup rows if not already done
            if (dgv.RowCount != ThrottleBins)
            {
                dgv.Rows.Clear();
                for (int t = 0; t < ThrottleBins; t++)
                {
                    int binIdx = ThrottleBins - 1 - t; // Flip: 0=100%, 9=0%
                    int tpStart = binIdx * 10;
                    int tpEnd = (binIdx + 1) * 10;
                    dgv.Rows.Add();
                    dgv.Rows[t].HeaderCell.Value = $"{tpStart}%";
                }
            }

            dgv.RowTemplate.Height = cellHeight;

            // Fill grid (flip Y)
            for (int t = 0; t < ThrottleBins; t++)
            {
                int binIdx = ThrottleBins - 1 - t; // Flip: 0=100%, 9=0%
                for (int r = 0; r < RpmBins; r++)
                {
                    double? dbl = grid[binIdx, r];
                    int? val = dbl.HasValue ? (int?)dbl : null;

                    if (val.HasValue && val < -99) val = null;
                    if (val.HasValue && val > 99) val = null;

                    dgv.Rows[t].Cells[r].Value = $"{val}";
                    ;
                }
            }

            dgv.ResumeLayout();
        }
        private void TrimGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            var dgv = sender as DataGridView;
            if (dgv == null) return;

            // Only color data cells, not headers
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var value = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return;

            if (double.TryParse(value.ToString(), out double trim))
            {
                // Good: -5% to +5% (green)
                if (trim >= -8 && trim <= 8)
                {
                    e.CellStyle.BackColor = Color.FromArgb(60, 200, 60); // Green
                    e.CellStyle.ForeColor = Color.Black;
                }
                // Marginal: -10% to -5% or 5% to 10% (yellow)
                else if ((trim >= -13 && trim < -8) || (trim > 8 && trim <= 13))
                {
                    e.CellStyle.BackColor = Color.FromArgb(255, 220, 60); // Yellow
                    e.CellStyle.ForeColor = Color.Black;
                }
                // Bad: < -10% or > 10% (red)
                else
                {
                    e.CellStyle.BackColor = Color.FromArgb(220, 60, 60); // Red
                    e.CellStyle.ForeColor = Color.White;
                }
            }
        }

        private void TrimGrid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            var dgv = sender as DataGridView;
            if (dgv == null) return;

            // Only paint data cells, not headers
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            // Paint everything as normal
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.Focus);

            // If this cell is selected, draw a blue border
            if (dgv.CurrentCell != null && e.RowIndex == dgv.CurrentCell.RowIndex && e.ColumnIndex == dgv.CurrentCell.ColumnIndex)
            {
                dgv.CurrentCell.Selected = false;
                dgv.CurrentCell.Style.SelectionBackColor = Color.FromArgb(0, 10, 10, 10);
            }

            e.Handled = true;
        }
        public void StartListening()
        {
            if (j2534Manager.CaptureOnlyMode) return;
            if (_ownsReceiveChannel)
            {
                if (_cts != null && !_cts.IsCancellationRequested &&
                    _monitorThread != null && _monitorThread.IsAlive &&
                    _pollingThread != null && _pollingThread.IsAlive) return;
                // Retire both workers and filters before replacing any session field.
                StopListening();
                if (_ownsReceiveChannel) return;
            }
            StartApiAndThreads();
        }

        public void StopListening()
        {
            if (!_ownsReceiveChannel) return;
            Interlocked.Increment(ref _displayGeneration);
            _samples.Clear();
            RefreshFuelDisplay(false);
            _cts?.Cancel();
            // Do not release the exclusive reader until both worker threads exit.
            bool pollingStopped = ChannelLifecyclePolicy.RequestStopAndWait(_pollingThread, _cts, 3000);
            bool monitorStopped = ChannelLifecyclePolicy.RequestStopAndWait(_monitorThread, _cts, 3000);
            if (!pollingStopped || !monitorStopped)
            {
                Debug.WriteLine("Fuel trim receive shutdown pending; retaining channel ownership.");
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
                Debug.WriteLine("Fuel trim filter cleanup failed: " + ex);
                return;
            }
            _cts?.Dispose();
            _cts = null;
            _ownsReceiveChannel = false;
            j2534Manager.ReceiveGate.Release(ReceiveOwner);
        }
    }
}
