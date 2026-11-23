using System;
using System.Drawing;
using System.Windows.Forms;

namespace J2534Diag
{
    public partial class PidDisplayControl : UserControl
    {
        public event EventHandler IsEnabledChanged;
        private Font _fittedValueFont;
        private bool _fittingValueText;

        public string PidName
        {
            get => lblName.Text;
            set => lblName.Text = value;
        }

        public string ValueText
        {
            get => lblValue.Text;
            set => lblValue.Text = value;
        }

        public string Unit
        {
            get => lblUnit.Text;
            set => lblUnit.Text = value;
        }

        public bool IsEnabled
        {
            get => chkEnable.Checked;
            set => chkEnable.Checked = value;
        }

        public PidDisplayControl()
        {
            InitializeComponent();
            lblValue.AutoEllipsis = true;
            lblValue.UseMnemonic = false;
            lblValue.TextChanged += (s, e) => FitValueText();
            lblValue.SizeChanged += (s, e) => FitValueText();
            lblValue.Disposed += (s, e) =>
            {
                _fittedValueFont?.Dispose();
                _fittedValueFont = null;
            };
            FitValueText();
            chkEnable.CheckedChanged += (s, e) => IsEnabledChanged?.Invoke(this, EventArgs.Empty);
        }

        // Fit status strings as well as numeric values to the actual scaled label.
        // Start at the original 24 pt size each time so shorter values grow again.
        private void FitValueText()
        {
            if (_fittingValueText || lblValue.IsDisposed ||
                lblValue.ClientSize.Width <= 0 || lblValue.ClientSize.Height <= 0) return;
            _fittingValueText = true;
            try
            {
                var available = lblValue.ClientSize;
                available.Width = Math.Max(1, available.Width - lblValue.Padding.Horizontal - 4);
                available.Height = Math.Max(1, available.Height - lblValue.Padding.Vertical - 2);
                float size = 24F;
                var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                for (; size > 6F; size -= 0.5F)
                {
                    using (var candidate = new Font("Consolas", size, FontStyle.Bold))
                    {
                        var measured = TextRenderer.MeasureText(lblValue.Text, candidate,
                            new Size(int.MaxValue, int.MaxValue), flags);
                        if (measured.Width <= available.Width &&
                            measured.Height <= available.Height) break;
                    }
                }
                if (Math.Abs(lblValue.Font.SizeInPoints - size) < 0.01F) return;
                var previous = _fittedValueFont;
                _fittedValueFont = new Font("Consolas", size, FontStyle.Bold);
                lblValue.Font = _fittedValueFont;
                previous?.Dispose();
            }
            finally
            {
                _fittingValueText = false;
            }
        }
    }
}
