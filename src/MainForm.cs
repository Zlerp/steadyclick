using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SteadyClick
{
    /// <summary>A white panel with a hairline border and a bold title: one group of settings.</summary>
    sealed class Card : Panel
    {
        static readonly Color Border = Color.FromArgb(214, 218, 224);

        public Card(string title)
        {
            BackColor = Color.White;
            DoubleBuffered = true;
            ResizeRedraw = true;

            Label heading = new Label();
            heading.AutoSize = true;
            heading.Text = title;
            heading.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            heading.ForeColor = Color.FromArgb(31, 41, 55);
            heading.Location = new Point(11, 10);
            Controls.Add(heading);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    sealed class MainForm : Form
    {
        const int HotkeyToggle = 1;
        const int HotkeyPick = 2;

        static readonly Color GoColor = Color.FromArgb(22, 128, 78);
        static readonly Color StopColor = Color.FromArgb(200, 55, 45);

        readonly ClickEngine engine = new ClickEngine();
        readonly Settings settings;
        readonly Timer uiTimer = new Timer();
        readonly ToolTip tips = new ToolTip();
        readonly List<Control> lockedWhileRunning = new List<Control>();

        bool wasRunning;
        bool picking;
        PickOverlay activeOverlay;

        // Hooks for the automated window test (SteadyClick.exe --uitest).
        internal string HotkeyProblem { get { return hotkeyProblem; } }
        internal Keys TogglePickKey(bool toggle) { return toggle ? KeyOf(cbToggle, Keys.F6) : KeyOf(cbPick, Keys.F7); }
        internal Point CurrentSpot { get { return new Point((int)numX.Value, (int)numY.Value); } }
        internal bool Picking { get { return picking; } }
        internal bool IsRunning { get { return engine.Running; } }
        internal long ClickCount { get { return engine.Count; } }
        internal void BeginPick() { BeginInvoke(new MethodInvoker(PickOnScreen)); }
        internal void SetStartDelay(decimal seconds) { numDelay.Value = seconds; }
        bool ready;
        string notice = "Ready.";
        string hotkeyProblem = "";

        RadioButton rbFixed, rbCursor;
        NumericUpDown numX, numY, numJitter, numMin, numMax, numHoldMin, numHoldMax, numDelay;
        NumericUpDown numMaxClicks, numMaxMinutes;
        Button btnPick, btnShow, btnStart, btnDetails;
        DetailsForm details;
        ComboBox cbButton, cbType, cbToggle, cbPick, cbVariation;
        CheckBox chkRestore, chkFailsafe, chkTop, chkPauses;
        Label lblJitter, lblJitterUnit, lblHint, lblSummary, lblStatus, lblRhythm;
        RhythmPreview preview;

        /// <summary>When true the window never writes settings (used for layout screenshots).</summary>
        public bool Transient;

        public MainForm()
        {
            settings = Settings.Load();
            BuildUi();
            ApplySettings();
            ready = true;
            UpdateSummary();

            uiTimer.Interval = 100;
            uiTimer.Tick += delegate { RefreshStatus(); };
            uiTimer.Start();
        }

        // ---------------------------------------------------------------- layout

        void BuildUi()
        {
            SuspendLayout();
            Text = "SteadyClick";
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(241, 243, 246);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            const int colW = 356, left = 12, right = 380, fullW = right + colW - left;

            // --- where
            Card where = AddCard("Where to click", left, 12, colW, 200);
            rbFixed = AddRadio(where, "A fixed spot", 12, 42);
            AddLabel(where, "X", 126, 45);
            numX = AddNumber(where, 142, 42, 70, -100000, 100000, 0, 1);
            AddLabel(where, "Y", 224, 45);
            numY = AddNumber(where, 240, 42, 70, -100000, 100000, 0, 1);
            btnPick = AddButton(where, "Pick on screen", 30, 80, 118, 27);
            btnShow = AddButton(where, "Show me", 154, 80, 78, 27);
            lblHint = AddLabel(where, "", 30, 112);
            lblHint.ForeColor = Color.FromArgb(107, 114, 128);
            lblJitter = AddLabel(where, "Random offset, up to", 30, 141);
            numJitter = AddNumber(where, 158, 138, 56, 0, 500, 0, 1);
            lblJitterUnit = AddLabel(where, "px", 220, 141);
            rbCursor = AddRadio(where, "Wherever the cursor already is", 12, 170);

            // --- click
            Card click = AddCard("Click", left, 222, colW, 110);
            AddLabel(click, "Button", 12, 45);
            cbButton = AddCombo(click, 62, 42, 90, new object[] { "Left", "Right", "Middle" });
            AddLabel(click, "Type", 176, 45);
            cbType = AddCombo(click, 214, 42, 116, new object[] { "Single click", "Double click" });
            chkRestore = AddCheck(click, "Put the cursor back after each click", 12, 76);

            // --- hotkeys
            Card keys = AddCard("Hotkeys and window", left, 342, colW, 110);
            object[] keyNames = { "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11" };
            AddLabel(keys, "Start / stop", 12, 45);
            cbToggle = AddCombo(keys, 88, 42, 64, keyNames);
            AddLabel(keys, "Pick spot", 176, 45);
            cbPick = AddCombo(keys, 240, 42, 64, keyNames);
            chkTop = AddCheck(keys, "Keep this window on top", 12, 76);

            // --- timing
            Card timing = AddCard("Timing", right, 12, colW, 302);
            AddLabel(timing, "Click every", 12, 41);
            numMin = AddNumber(timing, 112, 38, 70, 0.01m, 86400, 2, 0.1m);
            AddLabel(timing, "to", 190, 41);
            numMax = AddNumber(timing, 212, 38, 70, 0.01m, 86400, 2, 0.1m);
            AddLabel(timing, "seconds", 290, 41);
            AddLabel(timing, "Hold each click", 12, 75);
            numHoldMin = AddNumber(timing, 112, 72, 70, 1, 10000, 0, 5);
            AddLabel(timing, "to", 190, 75);
            numHoldMax = AddNumber(timing, 212, 72, 70, 1, 10000, 0, 5);
            AddLabel(timing, "ms", 290, 75);
            AddLabel(timing, "Start delay", 12, 109);
            numDelay = AddNumber(timing, 112, 106, 70, 0, 3600, 1, 0.5m);
            AddLabel(timing, "seconds", 190, 109);
            AddLabel(timing, "Variation", 12, 143);
            cbVariation = AddCombo(timing, 112, 140, 170, new object[] { "Human-like rhythm", "Even spread" });
            chkPauses = AddCheck(timing, "Take a longer pause now and then", 112, 169);
            preview = new RhythmPreview();
            preview.SetBounds(12, 198, colW - 24, 62);
            preview.BackColor = Color.White;
            preview.Font = new Font("Segoe UI", 8f);
            timing.Controls.Add(preview);
            lblRhythm = new Label();
            lblRhythm.SetBounds(12, 263, colW - 24, 34);
            lblRhythm.Font = new Font("Segoe UI", 8.25f);
            lblRhythm.ForeColor = Color.FromArgb(75, 85, 99);
            timing.Controls.Add(lblRhythm);

            // --- stop
            Card stop = AddCard("Stop automatically", right, 324, colW, 128);
            AddLabel(stop, "After", 12, 41);
            numMaxClicks = AddNumber(stop, 56, 38, 90, 0, 1000000000, 0, 10);
            AddLabel(stop, "clicks  (0 = no limit)", 154, 41);
            AddLabel(stop, "After", 12, 71);
            numMaxMinutes = AddNumber(stop, 56, 68, 90, 0, 100000, 1, 1);
            AddLabel(stop, "minutes  (0 = no limit)", 154, 71);
            chkFailsafe = AddCheck(stop, "Emergency stop: cursor in a screen's top-left corner", 12, 98);

            // --- summary, start, status
            lblSummary = new Label();
            lblSummary.SetBounds(left, 462, fullW, 22);
            lblSummary.Font = new Font("Segoe UI", 10f);
            lblSummary.ForeColor = Color.FromArgb(31, 41, 55);
            lblSummary.AutoEllipsis = true;
            Controls.Add(lblSummary);

            btnStart = new Button();
            btnStart.SetBounds(left, 490, fullW, 44);
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.ForeColor = Color.White;
            btnStart.BackColor = GoColor;
            btnStart.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            btnStart.Cursor = Cursors.Hand;
            btnStart.Click += delegate { Toggle(); };
            Controls.Add(btnStart);

            lblStatus = new Label();
            lblStatus.SetBounds(left, 544, fullW - 150, 20);
            lblStatus.ForeColor = Color.FromArgb(75, 85, 99);
            lblStatus.AutoEllipsis = true;
            Controls.Add(lblStatus);

            btnDetails = new Button();
            btnDetails.Text = "Session details...";
            btnDetails.SetBounds(left + fullW - 138, 540, 138, 27);
            btnDetails.UseVisualStyleBackColor = true;
            btnDetails.Click += delegate { ShowDetails(); };
            Controls.Add(btnDetails);
            Tip(btnDetails, "Every click of this run: when it happened, the wait before it, how long it was held\n" +
                            "and where it landed, plus totals. Each run is also saved to a CSV file.");

            lockedWhileRunning.AddRange(new Control[] { where, timing, click, stop, keys });

            // --- behaviour
            rbFixed.CheckedChanged += delegate { UpdateEnabled(); };
            numMin.ValueChanged += delegate { if (numMax.Value < numMin.Value) numMax.Value = numMin.Value; };
            numMax.ValueChanged += delegate { if (numMin.Value > numMax.Value) numMin.Value = numMax.Value; };
            numHoldMin.ValueChanged += delegate { if (numHoldMax.Value < numHoldMin.Value) numHoldMax.Value = numHoldMin.Value; };
            numHoldMax.ValueChanged += delegate { if (numHoldMin.Value > numHoldMax.Value) numHoldMin.Value = numHoldMax.Value; };
            btnPick.Click += delegate { PickOnScreen(); };
            btnShow.Click += delegate { FlashSpot(); };
            chkTop.CheckedChanged += delegate { TopMost = chkTop.Checked; };
            cbToggle.SelectedIndexChanged += delegate { HotkeysChanged(); };
            cbPick.SelectedIndexChanged += delegate { HotkeysChanged(); };

            EventHandler changed = delegate { UpdateSummary(); };
            foreach (NumericUpDown n in new NumericUpDown[] { numX, numY, numJitter, numMin, numMax, numHoldMin,
                                                              numHoldMax, numDelay, numMaxClicks, numMaxMinutes })
            {
                n.ValueChanged += changed;
            }
            foreach (ComboBox c in new ComboBox[] { cbButton, cbType, cbToggle, cbVariation }) c.SelectedIndexChanged += changed;
            rbFixed.CheckedChanged += changed;
            chkPauses.CheckedChanged += changed;
            cbVariation.SelectedIndexChanged += delegate { UpdateEnabled(); };

            // --- tooltips
            tips.AutoPopDelay = 12000;
            Tip(numX, "Screen position of the spot, in pixels from the left edge.");
            Tip(numY, "Screen position of the spot, in pixels from the top edge.");
            Tip(btnPick, "Dims the screen so you can click the exact spot you want.");
            Tip(btnShow, "Flashes a ring on the current spot. The ring's size is the random offset area.");
            Tip(numJitter, "Each click lands on a random point within this many pixels of the spot.\n0 = the exact same pixel every time.");
            Tip(rbCursor, "No fixed spot: clicks happen wherever you leave the cursor.");
            Tip(numMin, "Shortest wait between clicks.");
            Tip(numMax, "Longest wait between clicks. Each wait is picked at random between the two.\nSet both the same for a steady rhythm.");
            Tip(numHoldMin, "Shortest time the button is held down for one click.");
            Tip(numHoldMax, "Longest time the button is held down. Picked at random between the two.");
            Tip(numDelay, "How long to wait after you press Start before the first click.");
            Tip(cbVariation,
                "Human-like rhythm: waits bunch around a typical pace with a tail of slower ones, and the pace\n" +
                "drifts faster and slower over several clicks, the way a person's does. Hold times and the\n" +
                "random offset vary the same way. If the two wait values are nearly equal, the range is\n" +
                "opened up a little, because nobody clicks like a metronome.\n\n" +
                "Even spread: every value in the range is equally likely, each click independent of the last.");
            Tip(chkPauses, "About 1 wait in 30 runs well past your maximum, like someone glancing away for a moment.");
            Tip(preview, "How often each wait length will come up with the current settings.");
            Tip(chkRestore, "After each click the cursor jumps back to where you had it,\nso you can keep using the mouse between clicks.");
            Tip(numMaxClicks, "Stop by itself after this many clicks. 0 = keep going.");
            Tip(numMaxMinutes, "Stop by itself after this many minutes. 0 = keep going.");
            Tip(chkFailsafe, "Shove the cursor into the top-left corner of a screen to stop instantly,\neven if the hotkey is not getting through.");
            Tip(cbToggle, "Works from any app, even when this window is hidden.");
            Tip(cbPick, "Sets the spot to wherever the cursor is right now. Works from any app.");

            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(right + colW + left, 576);
            ResumeLayout(false);
            PerformLayout();
        }

        void Tip(Control c, string text)
        {
            tips.SetToolTip(c, text);
        }

        Card AddCard(string title, int x, int y, int w, int h)
        {
            Card card = new Card(title);
            card.SetBounds(x, y, w, h);
            Controls.Add(card);
            return card;
        }

        static Label AddLabel(Control parent, string text, int x, int y)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = text;
            l.Location = new Point(x, y);
            parent.Controls.Add(l);
            return l;
        }

        static RadioButton AddRadio(Control parent, string text, int x, int y)
        {
            RadioButton r = new RadioButton();
            r.AutoSize = true;
            r.Text = text;
            r.Location = new Point(x, y);
            parent.Controls.Add(r);
            return r;
        }

        static CheckBox AddCheck(Control parent, string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.AutoSize = true;
            c.Text = text;
            c.Location = new Point(x, y);
            parent.Controls.Add(c);
            return c;
        }

        static Button AddButton(Control parent, string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.UseVisualStyleBackColor = true;
            parent.Controls.Add(b);
            return b;
        }

        static ComboBox AddCombo(Control parent, int x, int y, int w, object[] items)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.SetBounds(x, y, w, 23);
            c.Items.AddRange(items);
            parent.Controls.Add(c);
            return c;
        }

        static NumericUpDown AddNumber(Control parent, int x, int y, int w,
                                       decimal min, decimal max, int decimals, decimal step)
        {
            NumericUpDown n = new NumericUpDown();
            n.SetBounds(x, y, w, 23);
            n.Minimum = min;
            n.Maximum = max;
            n.DecimalPlaces = decimals;
            n.Increment = step;
            n.TextAlign = HorizontalAlignment.Right;
            parent.Controls.Add(n);
            return n;
        }

        static void SetNumber(NumericUpDown n, double value)
        {
            decimal v;
            try { v = (decimal)value; }
            catch (OverflowException) { v = n.Minimum; }
            n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }

        // ---------------------------------------------------------------- settings <-> controls

        void ApplySettings()
        {
            ClickOptions o = settings.Options;
            rbFixed.Checked = o.FixedPoint;
            rbCursor.Checked = !o.FixedPoint;
            SetNumber(numX, o.X);
            SetNumber(numY, o.Y);
            SetNumber(numJitter, o.JitterRadius);
            SetNumber(numMax, Math.Max(o.MinInterval, o.MaxInterval));
            SetNumber(numMin, Math.Min(o.MinInterval, o.MaxInterval));
            SetNumber(numHoldMax, Math.Max(o.HoldMinMs, o.HoldMaxMs));
            SetNumber(numHoldMin, Math.Min(o.HoldMinMs, o.HoldMaxMs));
            SetNumber(numDelay, o.StartDelay);
            cbVariation.SelectedIndex = o.Humanize ? 0 : 1;
            chkPauses.Checked = o.HumanPauses;
            cbButton.SelectedIndex = (int)o.Button;
            cbType.SelectedIndex = o.DoubleClick ? 1 : 0;
            chkRestore.Checked = o.RestoreCursor;
            SetNumber(numMaxClicks, o.MaxClicks);
            SetNumber(numMaxMinutes, o.MaxSeconds / 60.0);
            chkFailsafe.Checked = o.CornerFailsafe;
            chkTop.Checked = settings.AlwaysOnTop;

            SelectKey(cbToggle, settings.ToggleKey, "F6");
            SelectKey(cbPick, settings.PickKey, "F7");
            UpdateEnabled();
            HotkeysChanged();
        }

        static void SelectKey(ComboBox box, Keys key, string fallback)
        {
            int i = box.Items.IndexOf(key.ToString());
            box.SelectedIndex = i >= 0 ? i : box.Items.IndexOf(fallback);
        }

        static Keys KeyOf(ComboBox box, Keys fallback)
        {
            Keys k;
            return box.SelectedItem != null && Enum.TryParse(box.SelectedItem.ToString(), out k) ? k : fallback;
        }

        ClickOptions ReadOptions()
        {
            ClickOptions o = new ClickOptions();
            o.FixedPoint = rbFixed.Checked;
            o.X = (int)numX.Value;
            o.Y = (int)numY.Value;
            o.JitterRadius = (int)numJitter.Value;
            o.MinInterval = (double)Math.Min(numMin.Value, numMax.Value);
            o.MaxInterval = (double)Math.Max(numMin.Value, numMax.Value);
            o.HoldMinMs = (int)Math.Min(numHoldMin.Value, numHoldMax.Value);
            o.HoldMaxMs = (int)Math.Max(numHoldMin.Value, numHoldMax.Value);
            o.StartDelay = (double)numDelay.Value;
            o.Humanize = cbVariation.SelectedIndex != 1;
            o.HumanPauses = chkPauses.Checked;
            o.Button = (MouseButtonChoice)Math.Max(0, cbButton.SelectedIndex);
            o.DoubleClick = cbType.SelectedIndex == 1;
            o.RestoreCursor = chkRestore.Checked;
            o.MaxClicks = (long)numMaxClicks.Value;
            o.MaxSeconds = (double)numMaxMinutes.Value * 60.0;
            o.CornerFailsafe = chkFailsafe.Checked;
            return o;
        }

        void SaveSettings()
        {
            if (Transient) return;
            settings.Options = ReadOptions();
            settings.ToggleKey = KeyOf(cbToggle, Keys.F6);
            settings.PickKey = KeyOf(cbPick, Keys.F7);
            settings.AlwaysOnTop = chkTop.Checked;
            settings.Save();
        }

        void UpdateEnabled()
        {
            bool fixedSpot = rbFixed.Checked;
            numX.Enabled = numY.Enabled = btnPick.Enabled = btnShow.Enabled = fixedSpot;
            numJitter.Enabled = lblJitter.Enabled = lblJitterUnit.Enabled = lblHint.Enabled = fixedSpot;
            chkRestore.Enabled = fixedSpot;
            chkPauses.Enabled = cbVariation.SelectedIndex != 1;
        }

        /// <summary>One plain sentence describing exactly what pressing Start will do.</summary>
        void UpdateSummary()
        {
            if (!ready) return;
            ClickOptions o = ReadOptions();

            string what = cbButton.SelectedItem + (o.DoubleClick ? " double-click" : " click");
            string where = !o.FixedPoint
                ? "wherever the cursor is"
                : "at " + o.X + ", " + o.Y + (o.JitterRadius > 0 ? " (within " + o.JitterRadius + " px)" : "");
            double lo, hi;
            Rhythm.EffectiveRange(o, out lo, out hi);
            string when;
            if (o.Humanize) when = "every " + lo.ToString("0.##") + " to " + hi.ToString("0.##") + " s in a human-like rhythm";
            else if (lo == hi) when = "every " + lo.ToString("0.##") + " s";
            else when = "every " + lo.ToString("0.##") + " to " + hi.ToString("0.##") + " s at random";

            preview.Recalculate(o);
            lblRhythm.Text = preview.Caption;

            string until;
            string clicksPart = o.MaxClicks > 0 ? o.MaxClicks + (o.MaxClicks == 1 ? " click" : " clicks") : "";
            string timePart = o.MaxSeconds > 0 ? (o.MaxSeconds / 60.0).ToString("0.#") + " min" : "";
            if (clicksPart.Length > 0 && timePart.Length > 0) until = "for " + clicksPart + " or " + timePart + ", whichever comes first";
            else if (clicksPart.Length > 0) until = "for " + clicksPart;
            else if (timePart.Length > 0) until = "for " + timePart;
            else until = "until you press " + cbToggle.SelectedItem;

            lblSummary.Text = what + " " + where + ", " + when + ", " + until + ".";
        }

        // ---------------------------------------------------------------- hotkeys

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Native.UnregisterHotKey(Handle, HotkeyToggle);
            Native.UnregisterHotKey(Handle, HotkeyPick);
            base.OnHandleDestroyed(e);
        }

        void HotkeysChanged()
        {
            if (cbToggle.SelectedItem == null || cbPick.SelectedItem == null) return;
            RegisterHotkeys();
            lblHint.Text = "Or point at any spot and press " + cbPick.SelectedItem + ". Works from any app.";
            UpdateStartButton(engine.Running);
        }

        void RegisterHotkeys()
        {
            if (!IsHandleCreated || cbToggle == null || cbToggle.SelectedItem == null || cbPick.SelectedItem == null) return;
            Native.UnregisterHotKey(Handle, HotkeyToggle);
            Native.UnregisterHotKey(Handle, HotkeyPick);

            Keys toggle = KeyOf(cbToggle, Keys.F6), pick = KeyOf(cbPick, Keys.F7);
            if (toggle == pick)
            {
                Native.RegisterHotKey(Handle, HotkeyToggle, Native.MOD_NOREPEAT, (uint)toggle);
                hotkeyProblem = "Start / stop and Pick spot can't share a key. Pick spot is off until you change one.";
                return;
            }

            bool okToggle = Native.RegisterHotKey(Handle, HotkeyToggle, Native.MOD_NOREPEAT, (uint)toggle);
            bool okPick = Native.RegisterHotKey(Handle, HotkeyPick, Native.MOD_NOREPEAT, (uint)pick);
            if (!okToggle) hotkeyProblem = toggle + " is taken by another program. Choose a different start / stop key.";
            else if (!okPick) hotkeyProblem = pick + " is taken by another program. Choose a different pick key.";
            else hotkeyProblem = "";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HotkeyToggle && !picking) Toggle();
                else if (id == HotkeyPick && picking && activeOverlay != null) activeOverlay.AcceptAtCursor();
                else if (id == HotkeyPick && !picking && !engine.Running) CaptureCursor();
                return;
            }
            base.WndProc(ref m);
        }

        // ---------------------------------------------------------------- choosing the spot

        void SetSpot(Point p)
        {
            rbFixed.Checked = true;
            SetNumber(numX, p.X);
            SetNumber(numY, p.Y);
            notice = "Spot set to " + p.X + ", " + p.Y + ".";
            FlashSpot();
        }

        void CaptureCursor()
        {
            SetSpot(Cursor.Position);
        }

        void PickOnScreen()
        {
            if (picking || engine.Running) return;
            picking = true;
            try
            {
                using (PickOverlay overlay = new PickOverlay(cbPick.SelectedItem.ToString()))
                {
                    activeOverlay = overlay;
                    if (overlay.ShowDialog(this) == DialogResult.OK) SetSpot(overlay.Picked);
                }
            }
            finally
            {
                activeOverlay = null;
                picking = false;
            }
        }

        void FlashSpot()
        {
            new SpotMarker(new Point((int)numX.Value, (int)numY.Value), (int)numJitter.Value).Show();
        }

        // ---------------------------------------------------------------- session details

        internal void ShowDetails()
        {
            if (details == null || details.IsDisposed)
            {
                details = new DetailsForm(engine);
                Rectangle area = Screen.FromControl(this).WorkingArea;
                Point beside = new Point(Right + 8, Top);
                bool fits = beside.X + details.Width <= area.Right;
                details.Location = fits ? beside : new Point(Math.Max(area.Left, Left + 40), Math.Max(area.Top, Top + 40));
            }
            if (!details.Visible) details.Show(this);
            details.Activate();
        }

        static string Elapsed(double seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1
                ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                : t.Minutes + ":" + t.Seconds.ToString("00");
        }

        // ---------------------------------------------------------------- start / stop

        void Toggle()
        {
            if (engine.Running) engine.Stop();
            else StartClicking();
            RefreshStatus();
        }

        void StartClicking()
        {
            ClickOptions o = ReadOptions();
            if (o.FixedPoint)
            {
                Point target = new Point(o.X, o.Y);
                if (!SystemInformation.VirtualScreen.Contains(target))
                {
                    notice = "That spot is off-screen. Pick a new one.";
                    return;
                }
                Rectangle screen = Screen.FromPoint(target).Bounds;
                int reach = o.JitterRadius + 3;
                if (o.CornerFailsafe && target.X <= screen.Left + reach && target.Y <= screen.Top + reach)
                {
                    notice = "That spot is inside the emergency-stop corner. Move it, or turn the emergency stop off.";
                    return;
                }
            }
            SaveSettings();
            engine.Start(o);
        }

        void UpdateStartButton(bool running)
        {
            string key = cbToggle.SelectedItem == null ? "F6" : cbToggle.SelectedItem.ToString();
            btnStart.Text = (running ? "Stop" : "Start") + "   (" + key + ")";
            btnStart.BackColor = running ? StopColor : GoColor;
        }

        void RefreshStatus()
        {
            bool running = engine.Running;
            if (running != wasRunning)
            {
                foreach (Control c in lockedWhileRunning) c.Enabled = !running;
                UpdateStartButton(running);
                if (!running)
                {
                    long n = engine.Count;
                    notice = engine.StopReason + " after " + n + (n == 1 ? " click." : " clicks.");
                }
                wasRunning = running;
            }

            string text;
            if (running)
            {
                long n = engine.Count;
                ClickRecord last = n > 0 ? engine.Log[(int)n - 1] : null;
                text = "Running: " + n + (n == 1 ? " click" : " clicks") +
                       ", next in " + engine.SecondsUntilNext.ToString("0.0") + " s";
                if (last != null && last.Index > 1)
                {
                    text += "   |   last wait " + last.Wait.ToString("0.00") + " s   |   " +
                            Elapsed(last.SinceStart + engine.SecondsSinceLastClick) + " elapsed";
                }
            }
            else
            {
                text = hotkeyProblem.Length > 0 ? hotkeyProblem : notice;
            }
            if (lblStatus.Text != text) lblStatus.Text = text;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            engine.Stop();
            uiTimer.Stop();
            SaveSettings();
            base.OnFormClosing(e);
        }
    }
}
