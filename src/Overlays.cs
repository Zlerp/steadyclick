using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SteadyClick
{
    /// <summary>Dims every screen and lets the user click the spot they want targeted.</summary>
    sealed class PickOverlay : Form
    {
        readonly Label readout = new Label();

        readonly string hotkeyName;

        public Point Picked { get; private set; }

        /// <summary>Takes the spot under the cursor right now and closes. Called for the pick hotkey.</summary>
        public void AcceptAtCursor()
        {
            Picked = Cursor.Position;
            DialogResult = DialogResult.OK;
        }

        public PickOverlay(string hotkeyName)
        {
            this.hotkeyName = hotkeyName;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(12, 16, 24);
            Opacity = 0.55;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            DoubleBuffered = true;

            readout.AutoSize = true;
            readout.BackColor = Color.FromArgb(255, 214, 10);
            readout.ForeColor = Color.Black;
            readout.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            readout.Padding = new Padding(4, 2, 4, 2);
            readout.Visible = false;
            Controls.Add(readout);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            MoveReadout();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            MoveReadout();
        }

        void MoveReadout()
        {
            Point screen = Cursor.Position;
            Point local = PointToClient(screen);
            readout.Text = screen.X + ", " + screen.Y;
            int x = local.X + 20, y = local.Y + 20;
            if (x + readout.Width > ClientSize.Width) x = local.X - 20 - readout.Width;
            if (y + readout.Height > ClientSize.Height) y = local.Y - 20 - readout.Height;
            readout.Location = new Point(x, y);
            readout.Visible = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                AcceptAtCursor();
            }
            else
            {
                DialogResult = DialogResult.Cancel;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) AcceptAtCursor();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle primary = Screen.PrimaryScreen.Bounds;
            primary.Offset(-Left, -Top);
            using (Font big = new Font("Segoe UI", 22f, FontStyle.Bold))
            using (Font small = new Font("Segoe UI", 12f))
            using (StringFormat center = new StringFormat())
            {
                center.Alignment = StringAlignment.Center;
                float cx = primary.Left + primary.Width / 2f;
                float cy = primary.Top + primary.Height * 0.12f;
                e.Graphics.DrawString("Point at the spot, then click or press " + hotkeyName, big, Brushes.White, cx, cy, center);
                e.Graphics.DrawString("Enter or Space also work.  Esc or right-click cancels.", small, Brushes.Gainsboro,
                    cx, cy + big.GetHeight(e.Graphics) + 6, center);
            }
        }
    }

    /// <summary>
    /// A ring that flashes over the target for a moment. It is click-through and never takes focus,
    /// so it cannot swallow a click meant for the app underneath.
    /// </summary>
    sealed class SpotMarker : Form
    {
        readonly Timer life = new Timer();

        public SpotMarker(Point center, int jitterRadius)
        {
            int radius = Math.Max(jitterRadius, 12) + 4;
            int size = radius * 2 + 1;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(255, 59, 48);
            Opacity = 0.9;   // makes the window layered, which is what lets clicks pass through
            Bounds = new Rectangle(center.X - radius, center.Y - radius, size, size);

            using (GraphicsPath shape = new GraphicsPath(FillMode.Alternate))
            {
                shape.AddEllipse(0, 0, size - 1, size - 1);
                shape.AddEllipse(3, 3, size - 7, size - 7);          // hollow the ring out
                shape.AddRectangle(new Rectangle(radius - 6, radius, 13, 1));   // crosshair
                shape.AddRectangle(new Rectangle(radius, radius - 6, 1, 13));
                Region = new Region(shape);
            }

            life.Interval = 1100;
            life.Tick += delegate { life.Stop(); Close(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                              Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            life.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            life.Dispose();
            base.OnFormClosed(e);
        }
    }
}
