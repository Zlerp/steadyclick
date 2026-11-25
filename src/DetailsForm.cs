using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace SteadyClick
{
    /// <summary>A ListView that repaints without flicker while rows stream in.</summary>
    sealed class SmoothListView : ListView
    {
        public SmoothListView()
        {
            DoubleBuffered = true;
        }
    }

    /// <summary>Live totals and the click-by-click record of the current (or most recent) run.</summary>
    sealed class DetailsForm : Form
    {
        readonly ClickEngine engine;
        readonly Timer refresh = new Timer();
        readonly SmoothListView list = new SmoothListView();
        readonly Label[] values = new Label[10];
        readonly Label lblSaved = new Label();
        readonly CheckBox chkFollow = new CheckBox();
        readonly Button btnSave = new Button();
        readonly Button btnFolder = new Button();
        int shownCount = -1;

        static readonly string[] Names =
        {
            "Clicks", "Run time", "Clicks per minute", "Average hold", "Furthest from spot",
            "Average wait", "Typical (median) wait", "Shortest wait", "Longest wait", "Longer pauses"
        };

        public DetailsForm(ClickEngine engine)
        {
            this.engine = engine;

            SuspendLayout();
            Text = "SteadyClick: session details";
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(241, 243, 246);
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(560, 420);
            ShowInTaskbar = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            // --- totals: two columns of five
            Panel totals = new Panel();
            totals.BackColor = Color.White;
            totals.SetBounds(12, 12, 616, 136);
            totals.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(totals);
            for (int i = 0; i < Names.Length; i++)
            {
                int column = i / 5, row = i % 5;
                Label name = new Label();
                name.AutoSize = true;
                name.Text = Names[i];
                name.ForeColor = Color.FromArgb(75, 85, 99);
                name.Location = new Point(14 + column * 308, 12 + row * 23);
                totals.Controls.Add(name);

                Label value = new Label();
                value.AutoSize = true;
                value.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                value.Text = "-";
                value.Location = new Point(160 + column * 308, 12 + row * 23);
                totals.Controls.Add(value);
                values[i] = value;
            }

            // --- click-by-click
            list.SetBounds(12, 158, 616, 300);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.VirtualMode = true;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.Columns.Add("#", 56, HorizontalAlignment.Right);
            list.Columns.Add("Time", 96);
            list.Columns.Add("Wait before", 92, HorizontalAlignment.Right);
            list.Columns.Add("Held", 64, HorizontalAlignment.Right);
            list.Columns.Add("X", 64, HorizontalAlignment.Right);
            list.Columns.Add("Y", 64, HorizontalAlignment.Right);
            list.Columns.Add("Off spot", 70, HorizontalAlignment.Right);
            list.Columns.Add("Note", 84);
            list.RetrieveVirtualItem += OnRetrieveItem;
            Controls.Add(list);

            // --- bottom row
            chkFollow.AutoSize = true;
            chkFollow.Text = "Follow the newest click";
            chkFollow.Checked = true;
            chkFollow.Location = new Point(12, 471);
            chkFollow.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(chkFollow);

            btnFolder.Text = "Open log folder";
            btnFolder.SetBounds(392, 466, 116, 27);
            btnFolder.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnFolder.UseVisualStyleBackColor = true;
            btnFolder.Click += delegate { OpenFolder(); };
            Controls.Add(btnFolder);

            btnSave.Text = "Save as CSV...";
            btnSave.SetBounds(514, 466, 114, 27);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSave.UseVisualStyleBackColor = true;
            btnSave.Click += delegate { SaveAs(); };
            Controls.Add(btnSave);

            lblSaved.SetBounds(12, 500, 616, 18);
            lblSaved.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblSaved.ForeColor = Color.FromArgb(107, 114, 128);
            lblSaved.AutoEllipsis = true;
            Controls.Add(lblSaved);

            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 526);
            ResumeLayout(false);
            PerformLayout();

            refresh.Interval = 250;
            refresh.Tick += delegate { RefreshNow(); };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshNow();
            refresh.Start();
        }

        // Closing only hides the window, so it reopens instantly with the same record.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                refresh.Stop();
                return;
            }
            refresh.Stop();
            base.OnFormClosing(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { RefreshNow(); refresh.Start(); }
        }

        static string Seconds(double v)
        {
            return v.ToString(v < 100 ? "0.000" : "0.0", CultureInfo.CurrentCulture) + " s";
        }

        static string Duration(double seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1
                ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                : t.Minutes + ":" + t.Seconds.ToString("00");
        }

        void RefreshNow()
        {
            ClickLog log = engine.Log;
            int count = log.Count;
            bool running = engine.Running;
            if (count == shownCount && !running && lblSaved.Tag as string == log.SavedPath) return;

            SessionStats s = log.Stats();
            values[0].Text = s.Clicks.ToString("N0");
            values[1].Text = s.Clicks > 0 ? Duration(s.Elapsed) : "-";
            values[2].Text = s.Waits > 0 ? s.ClicksPerMinute.ToString("0.0") : "-";
            values[3].Text = s.Clicks > 0 ? s.AverageHoldMs.ToString("0") + " ms" : "-";
            values[4].Text = s.Clicks > 0 ? s.FurthestOffset.ToString("0.0") + " px" : "-";
            values[5].Text = s.Waits > 0 ? Seconds(s.AverageWait) : "-";
            values[6].Text = s.Waits > 0 ? Seconds(s.MedianWait) : "-";
            values[7].Text = s.Waits > 0 ? Seconds(s.ShortestWait) : "-";
            values[8].Text = s.Waits > 0 ? Seconds(s.LongestWait) : "-";
            values[9].Text = s.Clicks > 0 ? s.Pauses.ToString("N0") : "-";

            if (count != shownCount)
            {
                list.VirtualListSize = count;
                shownCount = count;
                if (count > 0 && chkFollow.Checked) list.EnsureVisible(count - 1);
                else list.Invalidate();
            }

            string saved = log.SavedPath;
            lblSaved.Tag = saved;
            if (running) lblSaved.Text = "Recording. The run is saved automatically when it stops.";
            else if (saved.Length > 0) lblSaved.Text = "Saved automatically to " + saved;
            else lblSaved.Text = count == 0 ? "Nothing recorded yet. Press Start in the main window." : "";
            btnSave.Enabled = count > 0;
        }

        void OnRetrieveItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            ClickRecord r = engine.Log[e.ItemIndex];
            if (r == null)
            {
                e.Item = new ListViewItem(new string[] { "", "", "", "", "", "", "", "" });
                return;
            }
            string note = r.Pause ? "long pause" : "";
            if (r.Double) note = note.Length > 0 ? note + ", double" : "double";
            e.Item = new ListViewItem(new string[]
            {
                r.Index.ToString("N0"),
                r.Time.ToString("HH:mm:ss.fff"),
                r.Index == 1 ? "-" : r.Wait.ToString("0.000") + " s",
                r.HoldMs + " ms",
                r.X.ToString(),
                r.Y.ToString(),
                r.Offset.ToString("0.0") + " px",
                note
            });
            if (r.Pause) e.Item.ForeColor = Color.FromArgb(180, 83, 9);
        }

        void SaveAs()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV file (*.csv)|*.csv";
                dialog.FileName = "steadyclick-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    engine.Log.SaveCsv(dialog.FileName);
                    lblSaved.Text = "Saved to " + dialog.FileName;
                }
                catch (IOException ex) { lblSaved.Text = "Could not save: " + ex.Message; }
                catch (UnauthorizedAccessException ex) { lblSaved.Text = "Could not save: " + ex.Message; }
            }
        }

        static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(ClickLog.Folder);
                Process.Start("explorer.exe", "\"" + ClickLog.Folder + "\"");
            }
            catch (Exception) { }
        }
    }
}
