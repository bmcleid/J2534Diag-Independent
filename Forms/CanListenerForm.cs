using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SAE.J2534;

namespace J2534Diag
{
    public partial class CanListenerForm : Form
    {
        private const int MAX_RECORDS = 10000;
        private Thread _listenThread;
        private CancellationTokenSource _cts;
        private J2534Manager j2534Manager;
        private long _messageIndex = 0;
        private bool _isListening = false;

        private readonly BindingList<CanMessage> _messages = new BindingList<CanMessage>();
        private readonly List<CanMessage> _allMessages = new List<CanMessage>();
        private readonly object _allMessagesLock = new object();
        private const string ReceiveOwner = "CAN_CAPTURE";
        private int? _listenerFilterId;
        private string _listenerFailure;
        private long _captureFilterBeginHostTicks;
        private long _captureFilterDoneHostTicks;
        private bool _limitReached;
        private int _uiRefreshPending;
        private long _invalidFrames;
        private long _excessReturnedFrames;
        private CanCaptureSummary _lastCaptureSummary;
        private CanDiskCaptureLogger _diskLogger;
        private string _diskLogPath;
        private bool _nativeSixColumnLoaded;
        private PendingIntegritySidecar _pendingIntegrityReport;
        private readonly Button _retryReportButton = new Button
        {
            Text = "Retry integrity",
            Visible = false,
            Size = new System.Drawing.Size(115, 24),
            Location = new System.Drawing.Point(482, 14),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        private readonly CheckBox _diskLogOption = new CheckBox
        {
            Text = "Disk log",
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Location = new System.Drawing.Point(515, 312)
        };

        public CanListenerForm(J2534Manager j2534Manager)
        {
            InitializeComponent();

            dataGridViewMessages.AutoGenerateColumns = false;
            dataGridViewMessages.DataSource = _messages;

            dataGridViewMessages.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Index",
                HeaderText = "Index",
                DataPropertyName = "Index",
                Width = 60
            });
            dataGridViewMessages.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ReceivedAtUtc",
                HeaderText = "Received UTC (host)",
                DataPropertyName = "ReceivedAtUtc",
                Width = 215
            });
            dataGridViewMessages.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "J2534TimestampRaw",
                HeaderText = "J2534 time (raw)",
                DataPropertyName = "J2534TimestampRaw",
                Width = 150
            });
            dataGridViewMessages.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "J2534RxStatusHex",
                HeaderText = "RxStatus",
                DataPropertyName = "J2534RxStatusHex",
                Width = 110
            });
            dataGridViewMessages.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ArbId",
                HeaderText = "ArbId",
                DataPropertyName = "ArbId",
                Width = 120
            });
            dataGridViewMessages.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ArbIdDescription",
                HeaderText = "ArbId Description",
                DataPropertyName = "ArbIdDescription",
                Width = 200
            });
            dataGridViewMessages.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Data",
                HeaderText = "Data",
                DataPropertyName = "Data",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });

            Controls.Add(_diskLogOption);
            Controls.Add(_retryReportButton);
            _retryReportButton.BringToFront();
            _retryReportButton.Click += (sender, e) => RetryPendingIntegrityReport();
            this.Load += CanListenerForm_Load;
            this.FormClosing += CanListenerForm_FormClosing;
            this.j2534Manager = j2534Manager;
        }

        private void CanListenerForm_Load(object sender, EventArgs e)
        {
        }

        private void ScheduleUiRefresh()
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (Interlocked.Exchange(ref _uiRefreshPending, 1) != 0) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    Interlocked.Exchange(ref _uiRefreshPending, 0);
                    if (!IsDisposed && !Disposing)
                        ApplyFilterAndDistinct();
                }));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _uiRefreshPending, 0);
            }
        }

        // A single receive-and-store thread avoids discarding an asynchronous
        // processing backlog when the user stops the listener.
        // Adapter-specific work is confined here. The capture logic itself is
        // exercised through a fake ICanFrameBatchSource by offline tests.
        // All timestamps use the same host monotonic Stopwatch clock.
        // Do not infer TOPDON firmware queue depth or native CAN time from them.
        private void ApplyCaptureStartupTiming(CanCaptureSummary summary)
        {
            if (summary == null || _lastCaptureNative) return;
            // Native filter is installed inside NativeOwnedCanSession. Do not
            // fabricate equivalent managed-channel timing from old state.
            long opened = j2534Manager.CaptureOnlyChannelOpenedHostTicks;
            long beforeFilter = _captureFilterBeginHostTicks;
            long afterFilter = _captureFilterDoneHostTicks;
            long firstRead = summary.FirstReadStartedHostTicks;
            if (opened > 0 && beforeFilter >= opened)
                summary.ChannelToFilterElapsedTicks = beforeFilter - opened;
            if (afterFilter >= beforeFilter && beforeFilter > 0)
                summary.FilterInstallElapsedTicks = afterFilter - beforeFilter;
            if (firstRead >= afterFilter && afterFilter > 0)
                summary.FilterToFirstReadElapsedTicks = firstRead - afterFilter;
        }

        private void ListenLoop()
        {
            var channel = j2534Manager.Channel;
            CanCapturePump pump = null;
            try
            {
                pump = new CanCapturePump(
                    new J2534CanFrameSource(channel),
                    _diskLogger != null ? int.MaxValue : MAX_RECORDS,
                    () => DateTime.UtcNow,
                    id => GmCanId11Bit.KnownCanIds.TryGetValue(id, out var description) ? description : "");

                var summary = pump.Run(
                    _cts.Token,
                    record =>
                    {
                        var message = new CanMessage
                        {
                            Index = record.Index,
                            ReceivedAtUtc = record.ReceivedAtUtc,
                            ArbId = record.ArbId,
                            ArbIdDescription = record.ArbIdDescription,
                            Data = record.Data,
                            J2534TimestampRaw = record.J2534TimestampRaw,
                            J2534RxStatusHex = record.J2534RxStatusHex,
                            J2534ReadResult = record.J2534ReadResult
                        };
                        // All records go to the bounded writer when Disk log is
                        // selected. In-memory rows are only a rolling UI preview.
                        if (_diskLogger != null && !_diskLogger.TryEnqueue(record))
                            throw new IOException("Disk capture queue overflow or writer failure; capture halted. Consult integrity report.");

                        lock (_allMessagesLock)
                        {
                            _allMessages.Add(message);
                            if (_diskLogger != null && _allMessages.Count > MAX_RECORDS + 500)
                                _allMessages.RemoveRange(0, 500);
                        }
                        Interlocked.Exchange(ref _messageIndex, record.Index);
                    },
                    count => ScheduleUiRefresh());

                _limitReached = summary.LimitReached;
                _invalidFrames = summary.InvalidFrames;
                _excessReturnedFrames = summary.ExcessReturnedFrames;
                ApplyCaptureStartupTiming(summary);
                _lastCaptureSummary = summary;
            }
            catch (Exception ex)
            {
                if (pump?.LastSummary != null)
                {
                    _invalidFrames = pump.LastSummary.InvalidFrames;
                    _excessReturnedFrames = pump.LastSummary.ExcessReturnedFrames;
                    ApplyCaptureStartupTiming(pump.LastSummary);
                    _lastCaptureSummary = pump.LastSummary;
                }
                _listenerFailure = ex.Message;
                Debug.WriteLine("CAN listener stopped with error: " + ex);
            }
            finally
            {
                ScheduleUiRefresh();
                // Finalize only from UI; never self-join the receiving thread.
                if (!_cts.IsCancellationRequested && IsHandleCreated && !IsDisposed && !Disposing)
                {
                    try { BeginInvoke(new Action(() => StopListening())); }
                    catch (InvalidOperationException) { }
                }
            }
        }

        private void btnExportCsv_Click(object sender, EventArgs e)
        {
            if (_isListening)
            {
                MessageBox.Show("Stop the CAN listener before exporting the captured frames.", "Stop capture first", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_diskLogPath != null)
            {
                if (MessageBox.Show("Disk logging is the full-session file. Export CSV contains only the latest on-screen preview (up to 10,500 frames). Continue?", "Export preview only", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    return;
            }
            if (_nativeSixColumnLoaded)
            {
                if (MessageBox.Show(
                    "You loaded a six-column external native CAN recording. Export creates a DERIVED eight-column J2534Diag file. It does not retain the original file's labelled timestamp_us units or establish capture completeness. Keep the original source unchanged. Continue?",
                    "Export derived CAN evidence",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }
            using (var sfd = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"CAN-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    List<CanMessage> snapshot;
                    lock (_allMessagesLock)
                    {
                        snapshot = _allMessages.OrderBy(m => m.Index).ToList();
                    }
                    using (var writer = new StreamWriter(sfd.FileName, false, new System.Text.UTF8Encoding(true)))
                    {
                        // Export the stored capture, NOT the filtered/distinct UI subset.
                        CanCaptureCsv.Write(writer, snapshot);
                    }
                    MessageBox.Show($"Exported {snapshot.Count} stored CAN frames.", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Export failed: " + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void chkDistinctOnly_CheckedChanged(object sender, EventArgs e)
        {
            ApplyFilterAndDistinct();
        }

        private void CanListenerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopListening();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            StartListening();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopListening();
        }

        public void StartListening()
        {
            if (_isListening) return;
            if (_pendingIntegrityReport != null)
            {
                MessageBox.Show("An earlier integrity report still needs recovery. Use Retry integrity before starting a new capture.",
                    "Integrity recovery pending", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string selectedDiskPath = null;
            if (_diskLogOption.Checked)
            {
                using (var dialog = new SaveFileDialog
                {
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = $"CAN-session-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                    OverwritePrompt = false
                })
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return;
                    selectedDiskPath = dialog.FileName;
                    if (File.Exists(selectedDiskPath))
                    {
                        MessageBox.Show("Choose a new filename; existing recordings will never be overwritten.", "Capture already exists", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }
            if (j2534Manager.Channel == null || j2534Manager.Channel.IsDisposed)
            {
                MessageBox.Show("Connect a J2534 interface first.", "CAN capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!j2534Manager.ReceiveGate.TryAcquire(ReceiveOwner))
            {
                MessageBox.Show("J2534 channel is already in use by " + j2534Manager.ReceiveGate.CurrentOwner + ". Stop that diagnostic session before capturing.", "Channel in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                lock (_allMessagesLock) _allMessages.Clear();
                _messages.Clear();
                _messageIndex = 0;
                _listenerFailure = null;
                _limitReached = false;
                _invalidFrames = 0;
                _excessReturnedFrames = 0;
                _lastCaptureSummary = null;
                _diskLogPath = null;
                _diskLogger = null;
                _nativeSixColumnLoaded = false;
                Interlocked.Exchange(ref _uiRefreshPending, 0);
                _cts = new CancellationTokenSource();

                var filter = new MessageFilter
                {
                    FilterType = Filter.PASS_FILTER,
                    Mask = new byte[] { 0, 0, 0, 0 },
                    Pattern = new byte[] { 0, 0, 0, 0 },
                    FlowControl = new byte[] { 0, 0, 0, 0 },
                    TxFlags = j2534Manager.BitMode == BitType.BITS_11 ? TxFlag.NONE : TxFlag.CAN_29BIT_ID
                };
                // Do not clear other owners' filters/buffers. Remove only ours on stop.
                _captureFilterBeginHostTicks = Stopwatch.GetTimestamp();
                _listenerFilterId = j2534Manager.Channel.StartMsgFilter(filter);
                _captureFilterDoneHostTicks = Stopwatch.GetTimestamp();
                if (selectedDiskPath != null)
                {
                    // CreateNew prevents accidental replacement of historical logs.
                    _diskLogger = new CanDiskCaptureLogger(selectedDiskPath, 4096);
                    _diskLogPath = _diskLogger.FilePath;
                }
                _isListening = true;
                _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "J2534 CAN capture" };
                _listenThread.Start();
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                _diskLogOption.Enabled = false;
            }
            catch (Exception ex)
            {
                _listenerFailure = "Capture startup failed: " + ex.Message;
                // A worker may have started before a later startup/UI error.
                // Never release its J2534 receive ownership while it is alive.
                _cts?.Cancel();
                bool receiverStopped = _listenThread == null || !_listenThread.IsAlive || _listenThread.Join(5000);
                bool writerStopped = _diskLogger == null || _diskLogger.CompleteAndWait(15000);
                bool filterStopped = !_listenerFilterId.HasValue;
                if (_listenerFilterId.HasValue && receiverStopped)
                {
                    try
                    {
                        if (j2534Manager.Channel != null && !j2534Manager.Channel.IsDisposed)
                            j2534Manager.Channel.StopMsgFilter(_listenerFilterId.Value);
                        _listenerFilterId = null;
                        filterStopped = true;
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.WriteLine("J2534 filter cleanup failed during startup: " + cleanupError);
                    }
                }

                if (!receiverStopped || !writerStopped || !filterStopped)
                {
                    // StopListening can be retried after a blocking driver call
                    // or disk operation finally returns. Lease remains held.
                    _isListening = true;
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                    _diskLogOption.Enabled = false;
                    MessageBox.Show(_listenerFailure + "\nCleanup remains pending; receiver/channel ownership was retained. Retry Stop before changing tabs or disconnecting.", "Start failed, cleanup pending", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (_diskLogger != null && _diskLogPath != null)
                {
                    try { WriteIntegrityReport(_diskLogPath + ".integrity.txt", _diskLogger.GetStatus()); }
                    catch (Exception reportError) { Debug.WriteLine("Start failure integrity report error: " + reportError); }
                }
                _diskLogger = null;
                _listenThread = null;
                _cts?.Dispose();
                _cts = null;
                _isListening = false;
                j2534Manager.ReceiveGate.Release(ReceiveOwner);
                MessageBox.Show(_listenerFailure, "Cannot start CAN capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Called from UI. Wait for the only receive thread to exit before
        // releasing channel ownership or permitting export/switching consumers.
        public bool StopListening()
        {
            if (!_isListening) return true;
            _cts.Cancel();
            if (Thread.CurrentThread == _listenThread)
                throw new InvalidOperationException("CAN capture worker cannot stop itself.");
            if (_listenThread != null && !_listenThread.Join(5000))
            {
                MessageBox.Show("The J2534 receive call did not stop within five seconds. The channel remains reserved; do not start another scanner session.", "Capture shutdown pending", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // Wait for all accepted CSV rows to flush BEFORE closing the
            // J2534 session or allowing a new receiver to start.
            if (_diskLogger != null && !_diskLogger.CompleteAndWait(15000))
            {
                MessageBox.Show("The disk writer has not finished. Channel ownership remains reserved; retry Stop when disk I/O completes.", "Disk shutdown pending", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                if (_listenerFilterId.HasValue)
                {
                    if (j2534Manager.Channel != null && !j2534Manager.Channel.IsDisposed)
                        j2534Manager.Channel.StopMsgFilter(_listenerFilterId.Value);
                    _listenerFilterId = null;
                }
                j2534Manager.ReceiveGate.Release(ReceiveOwner);
            }
            catch (Exception ex)
            {
                _listenerFailure = ex.Message;
                MessageBox.Show("CAN filter cleanup failed. Receive ownership remains reserved until the connection is reset: " + ex.Message, "CAN capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            var diskStatus = _diskLogger?.GetStatus();
            string integrityFailure = null;
            if (diskStatus != null && _diskLogPath != null)
            {
                try { WriteIntegrityReport(_diskLogPath + ".integrity.txt", diskStatus); }
                catch (Exception ex) { integrityFailure = ex.Message; }
            }

            _isListening = false;
            _listenThread = null;
            _cts.Dispose();
            _cts = null;
            _diskLogger = null;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            _diskLogOption.Enabled = true;
            ApplyFilterAndDistinct();

            if (_limitReached || _invalidFrames > 0 || _excessReturnedFrames > 0 ||
                _listenerFailure != null || (diskStatus != null && !diskStatus.ApplicationWriteComplete) ||
                (_lastCaptureSummary != null && (_lastCaptureSummary.OtherStatusReadCalls > 0 ||
                    _lastCaptureSummary.ReadCallsWithoutStatus > 0 ||
                    _lastCaptureSummary.DriverBufferOverflowEvents > 0)) ||
                integrityFailure != null)
            {
                string message = (_limitReached ? "Capture reached its configured frame limit; later frames are not recorded.\n" : "") +
                    (_invalidFrames > 0 ? $"Malformed J2534 records ignored: {_invalidFrames}.\n" : "") +
                    (_excessReturnedFrames > 0 ? $"Frames returned beyond requested limit: {_excessReturnedFrames}.\n" : "") +
                    (_lastCaptureSummary?.DriverBufferOverflowEvents > 0
                        ? $"J2534 reported receive-buffer overflow {_lastCaptureSummary.DriverBufferOverflowEvents} time(s). Some frames may be irrecoverably lost.\n" : "") +
                    (_lastCaptureSummary?.OtherStatusReadCalls > 0 || _lastCaptureSummary?.ReadCallsWithoutStatus > 0
                        ? "J2534 returned unrecognized/unavailable read status. Review the integrity report.\n" : "") +
                    (_listenerFailure != null ? "Capture stopped with driver error: " + _listenerFailure + "\n" : "") +
                    (diskStatus != null && !diskStatus.ApplicationWriteComplete
                        ? $"Disk integrity warning: enqueued={diskStatus.Accepted}, written={diskStatus.Written}, queue drops={diskStatus.QueueFullDrops}, unwritten={diskStatus.AcceptedNotWritten}, writer error={diskStatus.WriterError ?? "none"}.\n" : "") +
                    (integrityFailure != null ? "Could not write capture integrity report: " + integrityFailure : "");
                MessageBox.Show(message + (diskStatus != null ? "\nFile: " + _diskLogPath : ""), "Incomplete CAN capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (diskStatus != null)
            {
                MessageBox.Show("Disk capture closed and application-level writes accounted for.\nCSV: " + _diskLogPath + "\nIntegrity report: " + _diskLogPath + ".integrity.txt" +
                    (_lastCaptureSummary?.IgnoredIndicationRecords > 0
                        ? "\nJ2534 control/indication records excluded from CAN data: " +
                          _lastCaptureSummary.IgnoredIndicationRecords : "") +
                    "\nDevice/bus-level completeness remains unverified.",
                    "Disk logging complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }

        private void WriteIntegrityReport(string outputPath, CanDiskLogStatus status)
        {
            // Retain exact serialized report text if publication fails.
            // The capture writer has already been closed before this finalization.
            var buffer = new StringWriter(CultureInfo.InvariantCulture);
            CanCaptureIntegrityReport.Write(
                buffer, _diskLogPath, status, Interlocked.Read(ref _messageIndex),
                _invalidFrames, _excessReturnedFrames, _limitReached,
                _listenerFailure, DateTime.UtcNow, _lastCaptureSummary);
            _pendingIntegrityReport = new PendingIntegritySidecar(
                _diskLogPath, outputPath, buffer.ToString());
            try
            {
                _pendingIntegrityReport.Publish();
                _pendingIntegrityReport = null;
                _retryReportButton.Visible = false;
            }
            catch
            {
                _retryReportButton.Visible = true;
                throw;
            }
        }

        private void RetryPendingIntegrityReport()
        {
            if (_pendingIntegrityReport == null) return;
            try
            {
                _pendingIntegrityReport.Publish();
                string path = _pendingIntegrityReport.SidecarPath;
                _pendingIntegrityReport = null;
                _retryReportButton.Visible = false;
                MessageBox.Show("Recovered original finalized integrity report: " + path +
                    "\nVehicle-bus completeness remains UNVERIFIED.",
                    "Integrity report recovered", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Report retry failed: " + ex.Message +
                    "\nThe pending report is retained. No capture CSV was overwritten.",
                    "Integrity report pending", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public class CanMessage : CanCaptureRecord
        {
        }

        private void btnLoadCsv_Click(object sender, EventArgs e)
        {
            if (_isListening)
            {
                MessageBox.Show("Stop the CAN listener before importing a capture.", "Stop capture first", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var ofd = new OpenFileDialog { Filter = "CSV Files (*.csv)|*.csv" })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    string sourceSha256;
                    bool isNative = false;
                    // Lock the original source against normal concurrent writes
                    // while hashing and parsing the SAME bytes from one handle.
                    var imported = OfflineCaptureProvenance.ReadConsistentFile(
                        ofd.FileName, source =>
                        {
                            // leaveOpen keeps the original locked handle valid.
                            using (var headerReader = new StreamReader(source,
                                System.Text.Encoding.UTF8, true, 1024, true))
                            {
                                isNative = NativeSixColumnCanImport.HasNativeHeader(
                                    headerReader.ReadLine());
                            }
                            source.Seek(0, SeekOrigin.Begin);
                            using (var reader = new StreamReader(source,
                                System.Text.Encoding.UTF8, true, 1024, true))
                                return isNative
                                    ? NativeSixColumnCanImport.Read(reader)
                                    : CanCaptureCsv.Read(reader);
                        }, out sourceSha256);
                    lock (_allMessagesLock)
                    {
                        _allMessages.Clear();
                        _allMessages.AddRange(imported.Select(m => new CanMessage
                        {
                            Index = m.Index,
                            ReceivedAtUtc = m.ReceivedAtUtc,
                            ArbId = m.ArbId,
                            ArbIdDescription = m.ArbIdDescription,
                            Data = m.Data,
                            J2534TimestampRaw = m.J2534TimestampRaw,
                            J2534RxStatusHex = m.J2534RxStatusHex,
                            J2534ReadResult = m.J2534ReadResult
                        }));
                    }
                    Interlocked.Exchange(ref _messageIndex, imported.Count == 0 ? 0 : imported.Max(m => m.Index));
                    _diskLogPath = null;
                    _diskLogger = null;
                    _nativeSixColumnLoaded = isNative;
                    ApplyFilterAndDistinct();
                    if (isNative)
                    {
                        MessageBox.Show(
                            "Loaded " + imported.Count + " native six-column CAN rows for OFFLINE viewing. Original timestamp_us values were preserved as raw timestamps; no host UTC was synthesized. No ECU identities or GM signal meanings were inferred.\\n\\n" +
                            OfflineCaptureProvenance.Describe(sourceSha256, true),
                            "Native CAN recording imported",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Loaded " + imported.Count + " rows for OFFLINE viewing.\\n" +
                            OfflineCaptureProvenance.Describe(sourceSha256, false),
                            "CAN recording imported", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to load CSV: " + ex.Message, "Load CSV", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void txtFilter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyFilterAndDistinct();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void txtFilter_Leave(object sender, EventArgs e)
        {
            ApplyFilterAndDistinct();
        }

        private void dataGridViewMessages_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                txtFilter.Text = "";
                ApplyFilterAndDistinct();
            }
        }

        private void ApplyFilterAndDistinct()
        {
            string input = txtFilter.Text.Trim();
            bool hasFilter = !string.IsNullOrEmpty(input);

            if (hasFilter && !input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                input = "0x" + input;

            List<CanMessage> snapshot;
            lock (_allMessagesLock)
            {
                snapshot = _allMessages.ToList();
            }

            IEnumerable<CanMessage> filtered = snapshot;

            if (hasFilter)
                filtered = filtered.Where(m => string.Equals(m.ArbId, input, StringComparison.OrdinalIgnoreCase));

            if (chkDistinctOnly.Checked)
                filtered = filtered.GroupBy(m => new { m.ArbId, m.Data }).Select(g => g.First());

            _messages.RaiseListChangedEvents = false;
            _messages.Clear();
            foreach (var msg in filtered)
                _messages.Add(msg);
            _messages.RaiseListChangedEvents = true;
            _messages.ResetBindings();
        }

        private void dataGridViewMessages_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                var row = dataGridViewMessages.Rows[e.RowIndex];
                var arbId = row.Cells["ArbId"].Value as string;
                if (!string.IsNullOrEmpty(arbId))
                {
                    txtFilter.Text = arbId;
                    ApplyFilterAndDistinct();
                }
            }
        }


    }
}
