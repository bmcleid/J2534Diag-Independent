// MainForm.cs
using System;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Windows.Forms;
using SAE.J2534;

namespace J2534Diag
{
    public partial class MainForm : Form
    {
        private J2534Manager j2534Manager;
        private VehicleTabForm vehicleTabForm;
        private FuelTrimForm fuelTrimForm;
        private CanListenerForm canListenerForm;
        private MisfireForm misfireForm;

        public MainForm()
        {
            InitializeComponent();
        }

        public void UpdateSelectedVehicle(bool connected, string vin)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateSelectedVehicle(connected, vin)));
                return;
            }

            if (!connected)
            {
                selectedVehicleControl.SetVehicle(false, "", "");
                return;
            }

            if (string.IsNullOrEmpty(vin))
            {
                selectedVehicleControl.SetVehicle(true, "",
                    j2534Manager?.CaptureOnlyMode == true
                        ? "Capture-only: VIN intentionally not requested"
                        : "VIN unavailable");
                return;
            }

            var sb = new StringBuilder();

            var vehicle = VinDecoder.DecodeVin(vin);
            var space = " ";

            var sbEngine = new StringBuilder();
            if (!string.IsNullOrEmpty(vehicle.EngineCylinders))
            {
                sbEngine.Append(vehicle.EngineCylinders).Append(" Cyl, ");
            }

            if (!string.IsNullOrEmpty(vehicle.DisplacementL))
            {
                sbEngine.Append(vehicle.DisplacementL).Append(" L, ");
            }

            if (!string.IsNullOrEmpty(vehicle.EngineHP))
            {
                sbEngine.Append(vehicle.EngineHP).Append(" HP, ");
            }


            if (!string.IsNullOrEmpty(vehicle.ModelYear))
            {
                sb.Append(vehicle.ModelYear).Append(space);
            }

            if (!string.IsNullOrEmpty(vehicle.Make))
            {
                sb.Append(vehicle.Make).Append(space);
            }

            if (!string.IsNullOrEmpty(vehicle.Model))
            {
                sb.Append(vehicle.Model).Append(space);
            }

            if (!string.IsNullOrEmpty(vehicle.Series))
            {
                sb.Append(vehicle.Series).Append(space);
            }

            if (sbEngine.Length > 0)
            {
                sb.Append(" (").Append(sbEngine.ToString().TrimEnd(new char[] { ',', ' '})).Append(")");
            }

            selectedVehicleControl.SetVehicle(connected, vin, sb.ToString());
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                j2534Manager = new J2534Manager(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize J2534 Manager: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                vehicleTabForm = new VehicleTabForm(this, j2534Manager) { TopLevel = false, Dock = DockStyle.Fill, FormBorderStyle = FormBorderStyle.None };
                fuelTrimForm = new FuelTrimForm(j2534Manager) { TopLevel = false, Dock = DockStyle.Fill, FormBorderStyle = FormBorderStyle.None };
                canListenerForm = new CanListenerForm(j2534Manager) { TopLevel = false, Dock = DockStyle.Fill, FormBorderStyle = FormBorderStyle.None };
                misfireForm = new MisfireForm(j2534Manager) { TopLevel = false, Dock = DockStyle.Fill, FormBorderStyle = FormBorderStyle.None };
                tabControl1.TabPages["tabVehicle"].Controls.Add(vehicleTabForm);
                tabControl1.TabPages["tabFuelTrim"].Controls.Add(fuelTrimForm);
                tabControl1.TabPages["tabCanListener"].Controls.Add(canListenerForm);
                tabControl1.TabPages["tabMisfireMonitor"].Controls.Add(misfireForm);

                vehicleTabForm.Show();
                fuelTrimForm.Show();
                canListenerForm.Show();
                misfireForm.Show();
                // Intercept navigation BEFORE the selected page changes.
                // A blocked CAN receive/write shutdown must retain its visible tab.
                tabControl1.Selecting += TabControl_SelectingWithCaptureGuard;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize Program: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Stop in-process readers before disposing the shared J2534 channel.
            // A blocked driver read retains ownership; keep the application open
            // rather than unloading the driver beneath an active worker.
            canListenerForm?.StopListening();
            fuelTrimForm?.StopListening();
            misfireForm?.StopListening();
            if (j2534Manager?.ReceiveGate.CurrentOwner != null)
            {
                e.Cancel = true;
                MessageBox.Show(
                    "The J2534 channel is still owned by " + j2534Manager.ReceiveGate.CurrentOwner +
                    ". Close the active diagnostic session before exiting.",
                    "Channel shutdown pending", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            base.OnFormClosing(e);
            if (!e.Cancel)
                j2534Manager?.Dispose();
        }

        private void TabControl_SelectingWithCaptureGuard(object sender, TabControlCancelEventArgs e)
        {
            if (canListenerForm == null || e.TabPage == null) return;
            if (tabControl1.SelectedTab == tabControl1.TabPages["tabCanListener"] &&
                e.TabPage != tabControl1.TabPages["tabCanListener"] &&
                !canListenerForm.StopListening())
            {
                // StopListening retains the receive lease on worker/writer/filter
                // cleanup failure. Do not leave the capture tab until retry succeeds.
                e.Cancel = true;
            }
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            //vehicleTabForm?.StopListening();
            fuelTrimForm?.StopListening();
            canListenerForm?.StopListening();
            misfireForm?.StopListening();

            // Capture-only is a distinct connection state. The fuel trim and
            // misfire tabs issue outbound diagnostic requests, so neither may
            // automatically start after selecting those tabs.
            if (j2534Manager?.CaptureOnlyMode == true)
            {
                if (tabControl1.SelectedTab == tabControl1.TabPages["tabFuelTrim"] ||
                    tabControl1.SelectedTab == tabControl1.TabPages["tabMisfireMonitor"])
                    MessageBox.Show(
                        "Capture-only connection is active. This tab requires diagnostic requests and will not poll the vehicle.\n" +
                        "Use CAN Listener for capture, or explicitly reconnect in active diagnostics mode.",
                        "Capture-only mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (tabControl1.SelectedTab == tabControl1.TabPages["tabFuelTrim"])
            {
                fuelTrimForm.StartListening();
            }
            else if (tabControl1.SelectedTab == tabControl1.TabPages["tabMisfireMonitor"])
            {
                misfireForm.StartListening();
            }
        }
    }
}
