using System;
using System.Drawing;
using System.Windows.Forms;

namespace J2534Diag
{
    partial class FuelTrimForm
    {
        private System.ComponentModel.IContainer components = null;
        private FlowLayoutPanel flpPids;
        private FlowLayoutPanel flpActions;
        private Panel pnlGrids;
        private DataGridView dgvTrimGridBank1;
        private DataGridView dgvTrimGridBank2;
        private Label lblBank1;
        private Label lblBank2;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (j2534Manager != null && _disconnectReporter != null)
                    j2534Manager.DisconnectOutcomeChanged -= _disconnectReporter;
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Text = "J2534 OBD-II Scan Tool";

            // PID Panel (right, top)
            this.flpPids = new FlowLayoutPanel();
            this.flpPids.Dock = DockStyle.None;
            this.flpPids.AutoScroll = true;
            this.flpPids.WrapContents = true;
            this.flpPids.Padding = new Padding(4);

            // --- Grids Panel (right, bottom) ---
            this.pnlGrids = new Panel();
            this.pnlGrids.Dock = DockStyle.None;
            this.pnlGrids.AutoScroll = true;

            this.lblBank1 = new Label
            {
                Text = "Fuel Trim Grid - Bank 1",
                Location = new Point(0, 0),
                AutoSize = true,
                ForeColor = Color.Blue,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            // --- Add DataGridView for Bank 1 ---
            this.dgvTrimGridBank1 = new DataGridView
            {
                Name = "dgvTrimGridBank1",
                Location = new Point(0, 20),
                Size = new Size(482, 421),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false,
                RowHeadersWidth = 80,
                ReadOnly = true,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                Font = new Font("Consolas", 9F, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Color.DarkCyan,
                BackgroundColor = Color.FromArgb(30, 30, 40),
                EnableHeadersVisualStyles = false
            };
            this.dgvTrimGridBank1.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 60);
            this.dgvTrimGridBank1.ColumnHeadersDefaultCellStyle.ForeColor = Color.Cyan;


            this.lblBank2 = new Label
            {
                Text = "Fuel Trim Grid - Bank 2",
                Location = new Point(492, 0),
                AutoSize = true,
                ForeColor = Color.Orange,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };

            // --- Add DataGridView for Bank 2 ---
            this.dgvTrimGridBank2 = new DataGridView
            {
                Name = "dgvTrimGridBank2",
                Location = new Point(492, 20),
                Size = new Size(482, 421),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false,
                RowHeadersWidth = 80,
                ReadOnly = true,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                Font = new Font("Consolas", 9F, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Color.DarkOrange,
                BackgroundColor = Color.FromArgb(30, 30, 40),
                EnableHeadersVisualStyles = false
            };
            this.dgvTrimGridBank2.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 60);
            this.dgvTrimGridBank2.ColumnHeadersDefaultCellStyle.ForeColor = Color.Orange;

            // Add a FlowLayoutPanel for action buttons in pnlBottom
            flpActions = new FlowLayoutPanel();
            flpActions.Dock = DockStyle.None;
            flpActions.AutoSize = false;
            flpActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flpActions.WrapContents = true;
            flpActions.FlowDirection = FlowDirection.LeftToRight;
            //flpActions.Padding = new Padding(10, 10, 10, 10);

            // Add the Clear Fuel Trims button
            var btnClearFuelTrims = new Button();
            btnClearFuelTrims.Name = "btnClearFuelTrims";
            btnClearFuelTrims.Text = "Clear Charts";
            btnClearFuelTrims.AutoSize = true;
            btnClearFuelTrims.Click += btnClearFuelTrims_Click;
            flpActions.Controls.Add(btnClearFuelTrims);

            // Add grids and labels to pnlGrids
            this.pnlGrids.Controls.Add(this.lblBank1);
            this.pnlGrids.Controls.Add(this.dgvTrimGridBank1);
            this.pnlGrids.Controls.Add(this.lblBank2);
            this.pnlGrids.Controls.Add(this.dgvTrimGridBank2);

            // Size these three regions from the actual client viewport.
            // Avoid nested preferred-size tables feeding back the grid's old size.
            this.Controls.Add(this.flpPids);
            this.Controls.Add(this.flpActions);
            this.Controls.Add(this.pnlGrids);
        }

        #endregion
    }
}

