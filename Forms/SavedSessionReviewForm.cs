using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace J2534Diag
{
    // Independent read-only viewer: no manager, provider, live-state or transmit dependency.
    public sealed class SavedSessionReviewForm : Form
    {
        private readonly Button _openButton = new Button { Text = "Open saved session...", AutoSize = true };
        private readonly ComboBox _events = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _filter = new TextBox { Width = 260 };
        private readonly TextBox _source = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        private readonly TextBox _details = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
        private readonly Label _status = new Label { Text = "Choose a saved session report. No ECU requests.", Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly DataGridView _grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
            AutoGenerateColumns = true, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };
        private SavedSessionReport _document;
        public SavedSessionReviewForm()
        {
            Text = "Saved session review";
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            toolbar.Controls.Add(_openButton);
            toolbar.Controls.Add(_events);
            toolbar.Controls.Add(new Label { Text = "Find in report:", AutoSize = true, Margin = new Padding(8, 8, 3, 3) });
            toolbar.Controls.Add(_filter);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(6) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(_source, 0, 1);
            layout.Controls.Add(_grid, 0, 2); layout.Controls.Add(_details, 0, 3); layout.Controls.Add(_status, 0, 4);
            Controls.Add(layout);
            _source.Text = "Open a J2534Diag scalar session CSV to review saved observations, events and counters.\r\n" +
                "Saved values are historical. Opening a report does not resume a session or contact a vehicle.";
            _openButton.Click += (sender, e) =>
            {
                using (var dialog = new OpenFileDialog { Title = "Open J2534Diag saved session report",
                    Filter = "J2534Diag session report (*.csv)|*.csv", CheckFileExists = true, Multiselect = false })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { LoadReport(dialog.FileName); }
                    catch (Exception ex) { MessageBox.Show(this, "Could not open saved session: " + ex.Message,
                        "Saved session", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            };
            _filter.TextChanged += (sender, e) => RefreshRows();
            _events.SelectedIndexChanged += (sender, e) => RefreshRows();
            _grid.SelectionChanged += (sender, e) => ShowSelectedRecord();
        }
        internal void LoadReport(string path)
        {
            // A failed parse leaves the previously loaded document visible.
            var next = SavedSessionReport.Read(path);
            _document = next;
            _filter.Text = "";
            _events.Items.Clear(); _events.Items.Add("All events");
            _events.Items.AddRange(next.Rows.Select(r => r.Cell(2)).Distinct().Cast<object>().ToArray());
            _events.SelectedIndex = 0;
            _source.Text = "File: " + next.SourcePath + "\r\nFile SHA-256: " + next.SourceSha256 +
                "\r\nRecorded exporter: " + string.Join(" | ", next.Rows.Select(r => r.Cell(3)).Distinct()) +
                "\r\nExporter EXE SHA-256 (recorded claim): " + string.Join(" | ", next.Rows.Select(r => r.Cell(4)).Distinct()) +
                "\r\n" + next.ObservationRows + " observation rows; " + next.DisconnectRows +
                " disconnect events. File hash identifies these bytes; exporter identity is not authenticated.";
            RefreshRows();
        }
        private void RefreshRows()
        {
            if (_document == null) return;
            var table = new DataTable();
            table.Columns.Add("Source record", typeof(int));
            foreach (string header in SavedSessionReport.Headers) table.Columns.Add(header, typeof(string));
            foreach (var row in _document.Rows.Where(r => r.Matches(_filter.Text) &&
                (_events.SelectedIndex <= 0 || r.Cell(2) == Convert.ToString(_events.SelectedItem))))
            {
                var values = new object[17]; values[0] = row.SourceRecord;
                Array.Copy(row.Values, 0, values, 1, 16); table.Rows.Add(values);
            }
            var previous = _grid.DataSource as DataTable;
            _grid.DataSource = table;
            previous?.Dispose();
            foreach (DataGridViewColumn column in _grid.Columns)
            { column.SortMode = DataGridViewColumnSortMode.NotSortable; column.Width = column.Name == "Details" ? 360 : 150; }
            if (_grid.Columns.Count > 0) { _grid.Columns[0].Frozen = true; _grid.Columns[0].Width = 95; }
            _status.Text = table.Rows.Count + " of " + _document.Rows.Count +
                " saved records | Original record order | No ECU requests";
            ShowSelectedRecord();
        }
        private void ShowSelectedRecord()
        {
            if (_document == null || _grid.CurrentRow == null) { _details.Clear(); return; }
            int record;
            if (!int.TryParse(Convert.ToString(_grid.CurrentRow.Cells[0].Value), out record) ||
                record < 1 || record > _document.Rows.Count) { _details.Clear(); return; }
            var row = _document.Rows[record - 1]; var headers = SavedSessionReport.Headers;
            _details.Text = "Source data record " + record + "\r\n" +
                string.Join("\r\n", Enumerable.Range(0, headers.Length).Select(i => headers[i] + ": " + row.Cell(i)));
        }
    }
}
