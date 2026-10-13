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
        private bool _limitReached;
        private int _uiRefreshPending;
        private long _invalidFrames;

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
        private void ListenLoop()
        {
            var channel = j2534Manager.Channel;
            int count = 0;
            try
            {
                while (!_cts.IsCancellationRequested && count < MAX_RECORDS)
                {
                    var response = channel.GetMessages(200, 5);
                    foreach (var msg in response.Messages)
                    {
                        if (_cts.IsCancellationRequested) break;
                        if (count >= MAX_RECORDS) break;
                        var received = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                        var data = msg.Data;
                        if (data == null || data.Length < 4)
                        {
                            Interlocked.Increment(ref _invalidFrames);
                            continue;
                        }

                        uint arbIdValue = (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
                        var record = new CanMessage
                        {
                            Index = Interlocked.Increment(ref _messageIndex),
                            ReceivedAtUtc = received,
                            ArbId = $"0x{arbIdValue:X3}",
                            ArbIdDescription = GmCanId11Bit.KnownCanIds.TryGetValue(arbIdValue, out var description) ? description : "",
                            Data = BitConverter.ToString(data, 4, data.Length - 4)
                        };
                        lock (_allMessagesLock)
                            _allMessages.Add(record);
                        count++;
                        if (count % 200 == 0) ScheduleUiRefresh();
                    }
                }
                if (count >= MAX_RECORDS) _limitReached = true;
            }
            catch (Exception ex)
            {
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
                _listenerFilterId = j2534Manager.Channel.StartMsgFilter(filter);
                _isListening = true;
                _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "J2534 CAN capture" };
                _listenThread.Start();
                btnStart.Enabled = false;
                btnStop.Enabled = true;
            }
            catch (Exception ex)
            {
                _listenerFailure = ex.Message;
                if (_listenerFilterId.HasValue)
                {
                    try { j2534Manager.Channel.StopMsgFilter(_listenerFilterId.Value); }
                    catch (Exception cleanupError) { Debug.WriteLine(cleanupError); }
                    _listenerFilterId = null;
                }
                _isListening = false;
                j2534Manager.ReceiveGate.Release(ReceiveOwner);
                MessageBox.Show("Cannot start CAN capture: " + ex.Message, "CAN capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            _isListening = false;
            _listenThread = null;
            _cts.Dispose();
            _cts = null;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            ApplyFilterAndDistinct();

            if (_limitReached || _invalidFrames > 0 || _listenerFailure != null)
            {
                string message = (_limitReached ? $"Capture reached its {MAX_RECORDS} frame limit; later frames are not recorded.\n" : "") +
                    (_invalidFrames > 0 ? $"Malformed J2534 records ignored: {_invalidFrames}.\n" : "") +
                    (_listenerFailure != null ? "Capture stopped with driver error: " + _listenerFailure : "");
                MessageBox.Show(message, "Incomplete CAN capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return true;
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
                    List<CanCaptureRecord> imported;
                    using (var reader = new StreamReader(ofd.FileName))
                    {
                        // Accept original Index/ArbId/Data CSVs and timestamped CSVs.
                        imported = CanCaptureCsv.Read(reader);
                    }
                    lock (_allMessagesLock)
                    {
                        _allMessages.Clear();
                        _allMessages.AddRange(imported.Select(m => new CanMessage
                        {
                            Index = m.Index,
                            ReceivedAtUtc = m.ReceivedAtUtc,
                            ArbId = m.ArbId,
                            ArbIdDescription = m.ArbIdDescription,
                            Data = m.Data
                        }));
                    }
                    _lastFilteredIndex = imported.Count == 0 ? 0 : imported.Max(m => m.Index);
                    Interlocked.Exchange(ref _messageIndex, _lastFilteredIndex);
                    lock (_distinctSet) _distinctSet.Clear();
                    ApplyFilterAndDistinct();
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
