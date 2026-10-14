using System;
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
using J2534Diag.Properties;
using SAE.J2534;

namespace J2534Diag
{
    public partial class VehicleTabForm : Form
    {

        public struct MSG
        {
            public string id;
            public string data;
        }
        private MainForm mainForm;
        private J2534Manager j2534Manager;
        private readonly CheckBox _captureOnlyCheck = new CheckBox
        {
            Checked = true,
            Text = "Capture-only: no app diagnostic requests (CAN ACK may occur)",
            Dock = DockStyle.Fill,
            AutoSize = true
        };
        private readonly ComboBox _canWidthSelect = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };


        public VehicleTabForm(MainForm mainForm, J2534Manager j2534Manager)
        {
            InitializeComponent();

            // The old Connect button performed active VIN interrogation without
            // asking. Capture-only is now the explicit default and CAN bit
            // width must be chosen independently of vehicle identification.
            tableLayoutPanel1.SuspendLayout();
            tableLayoutPanel1.RowCount = 6;
            tableLayoutPanel1.RowStyles.Clear();
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.SetRow(btnConnect, 4);
            tableLayoutPanel1.SetRow(pictureBox1, 5);
            tableLayoutPanel1.Controls.Add(_captureOnlyCheck, 0, 2);
            _canWidthSelect.Items.AddRange(new object[]
            {
                "11-bit CAN identifier, 500 kbps",
                "29-bit CAN identifier, 500 kbps"
            });
            _canWidthSelect.SelectedIndex = 0;
            tableLayoutPanel1.Controls.Add(_canWidthSelect, 0, 3);
            _captureOnlyCheck.CheckedChanged += (sender, args) =>
                _canWidthSelect.Enabled = _captureOnlyCheck.Checked;
            tableLayoutPanel1.ResumeLayout(true);

            using (var ms = new System.IO.MemoryStream(Resources.J2534Diag))
            {
                this.pictureBox1.Image = Image.FromStream(ms);
            }

            // Start everything after the form is loaded
            this.Load += Form1_Load;
            this.FormClosing += VehicleTabForm_FormClosing;

            this.mainForm = mainForm;
            this.j2534Manager = j2534Manager;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (j2534Manager?.Devices?.Any() == true)
            {
                cbDevices.Items.AddRange(j2534Manager.Devices.Select(api => api.Name).ToArray());
                cbDevices.SelectedIndex = 0;
            }
            else
            {
                cbDevices.Items.Add("No J2534 devices found");
                btnConnect.Enabled = false;
            }
        }

        private void VehicleTabForm_FormClosing(object sender, FormClosingEventArgs e)
        {
        }

        //bool TryPrintVinFromPayload(byte[] payload, bool isotpComplete)
        //{
        //    // OBD2 Mode 09 PID 02: 49 02 01 <VIN...>
        //    // UDS 0x22 F190: 62 F1 90 <VIN...>
        //    if (payload.Length >= 4 && payload[0] == 0x49 && payload[1] == 0x02)
        //    {
        //        // OBD2: skip 49 02 01, VIN is next 17 bytes (may be split across multiple frames)
        //        int vinStart = 3;
        //        int vinLen = Math.Min(17, payload.Length - vinStart);
        //        if (vinLen > 0)
        //        {
        //            string vin = System.Text.Encoding.ASCII.GetString(payload, vinStart, vinLen);
        //            if (isotpComplete && vinLen == 17)
        //            {
        //                // Find the parent MainForm and call UpdateSelectedVehicle
        //                mainForm.UpdateSelectedVehicle(vin, "", "");
        //                return true;
        //            }
        //        }
        //    }
        //    else if (payload.Length >= 4 && payload[0] == 0x62 && payload[1] == 0xF1 && payload[2] == 0x90)
        //    {
        //        // UDS: skip 62 F1 90, VIN is next 17 bytes
        //        int vinStart = 3;
        //        int vinLen = Math.Min(17, payload.Length - vinStart);
        //        if (vinLen > 0)
        //        {
        //            string vin = System.Text.Encoding.ASCII.GetString(payload, vinStart, vinLen);
        //            Console.WriteLine($"VIN: {vin} (Time: {DateTime.Now:HH:mm:ss.fff})");
        //            return true;
        //        }
        //    }
        //    return false;
        //}

        private void btnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                btnConnect.Enabled = false;
                btnConnect.Text = "Connecting ...";
                Application.DoEvents();

                if(j2534Manager?.Devices?.Any() != true)
                {
                    MessageBox.Show("No J2534 devices found. Please install J2534 device drivers and try again.", "No Devices Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var selectedApi = j2534Manager.Devices[cbDevices.SelectedIndex];
                if (_captureOnlyCheck.Checked)
                {
                    var selectedWidth = _canWidthSelect.SelectedIndex == 1
                        ? BitType.BITS_29 : BitType.BITS_11;
                    j2534Manager.InitializeCaptureOnly(selectedApi.Filename, selectedWidth);
                    btnConnect.Text = "Capture-only connected (no VIN requests)";
                    MessageBox.Show(
                        "Capture-only CAN channel opened. No application VIN/PID queries will be sent by this mode.\n" +
                        "Open CAN Listener, optionally enable Disk log, and click Start to capture.\n" +
                        "CAN hardware may still acknowledge bus frames electrically. RLink X3 behavior remains unverified.",
                        "Capture-only connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    // Explicit user selection: legacy mode actively sends VIN
                    // identification requests and may send ISO-TP flow control.
                    j2534Manager.Initialize(selectedApi.Filename);
                    mainForm.UpdateSelectedVehicle(j2534Manager.Channel != null, j2534Manager.Vin);
                    btnConnect.Text = "Active diagnostics connected";
                }
            }
            catch (Exception ex)
            {
                btnConnect.Text = "Connect";
                MessageBox.Show($"J2534 Error: {ex.Message}", "J2534 Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }
    }
}
