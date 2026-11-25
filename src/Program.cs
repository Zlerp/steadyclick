using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("SteadyClick")]
[assembly: AssemblyProduct("SteadyClick")]
[assembly: AssemblyDescription("Clicks the real mouse on a spot at a randomized interval.")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace SteadyClick
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Test and screenshot modes must not leave run logs behind.
            if (args.Length > 0 && args[0].StartsWith("--")) ClickLog.AutoSaveEnabled = false;
            if (args.Length == 2 && args[0] == "--screenshot-details") return DetailsScreenshot(args[1]);

            if (args.Length == 2 && args[0] == "--selftest") return SelfTest.Run(args[1]);
            if (args.Length == 2 && args[0] == "--screenshot") return Screenshot(args[1]);
            if (args.Length == 2 && args[0] == "--uitest") return UiTest.Run(args[1]);

            // Two copies would fight over the same global hotkeys, so hand off to the first one.
            bool first;
            using (Mutex single = new Mutex(true, "SteadyClick.SingleInstance", out first))
            {
                if (!first)
                {
                    Process me = Process.GetCurrentProcess();
                    foreach (Process p in Process.GetProcessesByName(me.ProcessName))
                    {
                        if (p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(p.MainWindowHandle, 9);   // SW_RESTORE
                            SetForegroundWindow(p.MainWindowHandle);
                            break;
                        }
                    }
                    return 0;
                }
                Application.Run(new MainForm());
            }
            return 0;
        }

        /// <summary>SteadyClick.exe --screenshot-details out.png : the details window filled with a sample run.</summary>
        static int DetailsScreenshot(string path)
        {
            ClickEngine engine = new ClickEngine();
            ClickOptions o = new ClickOptions();
            o.X = 1720; o.Y = 720; o.JitterRadius = 6;
            Rhythm rhythm = new Rhythm(o, new Random(3));
            DateTime t = DateTime.Now.AddMinutes(-2);
            double since = 0;
            for (int i = 1; i <= 80; i++)
            {
                bool pause;
                double wait = rhythm.NextInterval(out pause);
                int dx, dy;
                rhythm.NextOffset(out dx, out dy);
                ClickRecord r = new ClickRecord();
                r.Index = i; r.Time = t; r.SinceStart = since;
                r.Wait = i == 1 ? 0 : wait; r.Pause = i > 1 && pause;
                r.HoldMs = rhythm.NextHoldMs();
                r.X = o.X + dx; r.Y = o.Y + dy; r.Offset = Math.Sqrt(dx * dx + dy * dy);
                engine.Log.Add(r);
                if (i > 1) { since += wait; t = t.AddSeconds(wait); r.SinceStart = since; r.Time = t; }
            }
            DetailsForm form = new DetailsForm(engine);
            form.StartPosition = FormStartPosition.CenterScreen;
            form.TopMost = true;
            form.ShowInTaskbar = true;
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 800;
            timer.Tick += delegate
            {
                timer.Stop();
                Rectangle b = form.Bounds;
                using (Bitmap bmp = new Bitmap(b.Width, b.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                Application.Exit();
            };
            form.Shown += delegate { timer.Start(); };
            Application.Run(form);
            return 0;
        }

        /// <summary>SteadyClick.exe --screenshot out.png : saves a picture of the window, for checking the layout.</summary>
        static int Screenshot(string path)
        {
            MainForm form = new MainForm();
            form.Transient = true;
            form.TopMost = true;
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 700;
            timer.Tick += delegate
            {
                timer.Stop();
                Rectangle b = form.Bounds;
                using (Bitmap bmp = new Bitmap(b.Width, b.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                form.Close();
            };
            form.Shown += delegate { timer.Start(); };
            Application.Run(form);
            return 0;
        }
    }

    /// <summary>
    /// SteadyClick.exe --uitest report.txt
    /// Opens the real window and presses the real hotkeys: sets the spot from the cursor, sets it
    /// from inside the pick overlay (by hotkey and by click), and starts and stops a run.
    /// </summary>
    static class UiTest
    {
        public static int Run(string reportPath)
        {
            StringBuilder report = new StringBuilder();
            bool allPassed = true;
            MainForm form = new MainForm();
            form.Transient = true;
            Stopwatch clock = Stopwatch.StartNew();

            Rectangle screen = Screen.PrimaryScreen.Bounds;
            Point c = new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2);
            Point p1 = new Point(c.X + 137, c.Y - 91);
            Point p2 = new Point(c.X - 203, c.Y + 58);
            Point p3 = new Point(c.X + 61, c.Y + 149);
            int pickKey = 0, toggleKey = 0;

            int step = 0;
            double stepAt = 0;
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 25;
            timer.Tick += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                if (now < stepAt) return;
                switch (step)
                {
                    case 0:
                        pickKey = (int)form.TogglePickKey(false);
                        toggleKey = (int)form.TogglePickKey(true);
                        if (!Check(report, "hotkeys registered", form.HotkeyProblem.Length == 0, form.HotkeyProblem))
                        {
                            allPassed = false;
                            step = 99;
                            return;
                        }
                        Native.SetCursorPos(p1.X, p1.Y);
                        step = 1; stepAt = now + 0.15;
                        break;
                    case 1:
                        Native.TapKey(pickKey);
                        step = 2; stepAt = now + 0.4;
                        break;
                    case 2:
                        allPassed &= Check(report, "pick hotkey sets the spot to the cursor", form.CurrentSpot == p1,
                            "wanted " + p1 + ", got " + form.CurrentSpot);
                        form.BeginPick();
                        step = 3; stepAt = now + 0.6;
                        break;
                    case 3:
                        allPassed &= Check(report, "Pick on screen opens the overlay", form.Picking, "picking=" + form.Picking);
                        Native.SetCursorPos(p2.X, p2.Y);
                        step = 4; stepAt = now + 0.15;
                        break;
                    case 4:
                        Native.TapKey(pickKey);
                        step = 5; stepAt = now + 0.5;
                        break;
                    case 5:
                        allPassed &= Check(report, "pick hotkey inside the overlay sets the spot and closes it",
                            !form.Picking && form.CurrentSpot == p2,
                            "picking=" + form.Picking + ", wanted " + p2 + ", got " + form.CurrentSpot);
                        form.BeginPick();
                        step = 6; stepAt = now + 0.6;
                        break;
                    case 6:
                        Native.SendMouseAt(Native.LEFTDOWN, p3.X, p3.Y);
                        Native.SendMouseAt(Native.LEFTUP, p3.X, p3.Y);
                        step = 7; stepAt = now + 0.5;
                        break;
                    case 7:
                        allPassed &= Check(report, "clicking inside the overlay sets the spot and closes it",
                            !form.Picking && form.CurrentSpot == p3,
                            "picking=" + form.Picking + ", wanted " + p3 + ", got " + form.CurrentSpot);
                        form.SetStartDelay(60);   // so starting never actually clicks during the test
                        Native.TapKey(toggleKey);
                        step = 8; stepAt = now + 0.4;
                        break;
                    case 8:
                        allPassed &= Check(report, "start/stop hotkey starts a run", form.IsRunning, "running=" + form.IsRunning);
                        Native.TapKey(toggleKey);
                        step = 9; stepAt = now + 0.4;
                        break;
                    case 9:
                        allPassed &= Check(report, "start/stop hotkey stops it again, with no clicks made",
                            !form.IsRunning && form.ClickCount == 0,
                            "running=" + form.IsRunning + ", clicks=" + form.ClickCount);
                        step = 99;
                        break;
                    default:
                        timer.Stop();
                        report.AppendLine(allPassed ? "RESULT: PASS" : "RESULT: FAIL");
                        File.WriteAllText(reportPath, report.ToString());
                        form.Close();
                        break;
                }
            };
            form.Shown += delegate { stepAt = clock.Elapsed.TotalSeconds + 0.6; timer.Start(); };
            Application.Run(form);
            return allPassed ? 0 : 1;
        }

        static bool Check(StringBuilder report, string name, bool ok, string detail)
        {
            report.AppendLine((ok ? "PASS  " : "FAIL  ") + name + "   [" + detail + "]");
            return ok;
        }
    }

    /// <summary>
    /// SteadyClick.exe --selftest report.txt
    /// Drives the real engine against a throwaway window and writes what that window actually
    /// received. Moves the real mouse for a few seconds.
    /// </summary>
    static class SelfTest
    {
        sealed class Hit
        {
            public double Time;
            public MouseButtons Button;
            public double Distance;
        }

        public static int Run(string reportPath)
        {
            StringBuilder report = new StringBuilder();
            bool allPassed = true;
            List<Hit> hits = new List<Hit>();
            Stopwatch clock = Stopwatch.StartNew();
            ClickEngine engine = new ClickEngine();
            Point center = Point.Empty;
            Point cursorBefore = Cursor.Position;

            Form form = new Form();
            form.Text = "SteadyClick self-test";
            form.AutoScaleMode = AutoScaleMode.None;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.ClientSize = new Size(420, 300);
            form.TopMost = true;
            form.BackColor = Color.FromArgb(24, 28, 36);
            // Tag our injected clicks so a click from the user's own hand is not counted.
            IntPtr tag = new IntPtr(0x5C11C);
            Native.ExtraInfo = tag;
            form.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (Native.GetMessageExtraInfo() != tag) return;
                Point p = form.PointToScreen(e.Location);
                Hit h = new Hit();
                h.Time = clock.Elapsed.TotalSeconds;
                h.Button = e.Button;
                h.Distance = Math.Sqrt(Math.Pow(p.X - center.X, 2) + Math.Pow(p.Y - center.Y, 2));
                hits.Add(h);
            };

            int phase = 0;
            double phaseStart = 0;
            double stopLatencyMs = 0;
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 25;
            timer.Tick += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                switch (phase)
                {
                    case 0:   // let the window settle, then: 12 left clicks, 0.15-0.35 s apart, 12 px offset
                        if (now < 0.5) return;
                        form.Activate();
                        center = form.PointToScreen(new Point(210, 150));
                        cursorBefore = Cursor.Position;
                        ClickOptions a = new ClickOptions();
                        a.X = center.X; a.Y = center.Y; a.JitterRadius = 12;
                        a.MinInterval = 0.15; a.MaxInterval = 0.35;
                        a.MaxClicks = 12; a.RestoreCursor = true; a.Humanize = false;
                        allPassed &= RhythmChecks(report);
                        hits.Clear();
                        engine.Start(a);
                        phase = 1;
                        break;

                    case 1:
                        if (engine.Running) return;
                        if (phaseStart == 0) { phaseStart = now; return; }
                        if (now - phaseStart < 0.2) return;
                        {
                            double minGap = double.MaxValue, maxGap = 0, maxDist = 0;
                            int left = 0;
                            for (int i = 0; i < hits.Count; i++)
                            {
                                if (hits[i].Button == MouseButtons.Left) left++;
                                maxDist = Math.Max(maxDist, hits[i].Distance);
                                if (i > 0)
                                {
                                    double gap = hits[i].Time - hits[i - 1].Time;
                                    minGap = Math.Min(minGap, gap);
                                    maxGap = Math.Max(maxGap, gap);
                                }
                            }
                            Point after = Cursor.Position;
                            allPassed &= Check(report, "A  sent 12 clicks", engine.Count == 12, "sent " + engine.Count);
                            allPassed &= Check(report, "A  window received 12 left clicks", left == 12 && hits.Count == 12,
                                "received " + hits.Count + ", left " + left);
                            allPassed &= Check(report, "A  gaps within 0.15-0.35 s", minGap >= 0.12 && maxGap <= 0.42,
                                "min " + F(minGap) + " s, max " + F(maxGap) + " s");
                            allPassed &= Check(report, "A  gaps actually vary", maxGap - minGap > 0.02,
                                "spread " + F(maxGap - minGap) + " s");
                            allPassed &= Check(report, "A  every click within 12 px of the spot", maxDist <= 13.5,
                                "furthest " + F(maxDist) + " px");
                            // Informational only: a hand on the mouse during the test moves the cursor too.
                            report.AppendLine("INFO  A  cursor before " + cursorBefore + ", after " + after +
                                              " (equal when nobody touches the mouse)");
                            allPassed &= Check(report, "A  stop reason", engine.StopReason.StartsWith("Finished"), engine.StopReason);

                            // The run record must agree with what the window saw.
                            SessionStats stats = engine.Log.Stats();
                            allPassed &= Check(report, "L  record has one row per click", engine.Log.Count == 12 && stats.Waits == 11,
                                engine.Log.Count + " rows, " + stats.Waits + " waits");
                            allPassed &= Check(report, "L  recorded waits match the real gaps",
                                Math.Abs(stats.ShortestWait - minGap) < 0.04 && Math.Abs(stats.LongestWait - maxGap) < 0.04,
                                "recorded " + F(stats.ShortestWait) + " to " + F(stats.LongestWait) + " s, seen " + F(minGap) + " to " + F(maxGap) + " s");
                            ClickRecord sample = engine.Log[5];
                            allPassed &= Check(report, "L  rows carry position, hold and offset",
                                sample != null && sample.Index == 6 && sample.HoldMs >= 60 && sample.HoldMs <= 130 &&
                                Math.Abs(sample.X - center.X) <= 12 && Math.Abs(sample.Y - center.Y) <= 12 && stats.FurthestOffset <= 12.5,
                                sample == null ? "missing" : "row 6: " + sample.X + "," + sample.Y + " held " + sample.HoldMs + " ms, furthest " + F(stats.FurthestOffset) + " px");
                            string csv = reportPath + ".csv";
                            engine.Log.SaveCsv(csv);
                            string[] csvLines = File.ReadAllLines(csv);
                            File.Delete(csv);
                            allPassed &= Check(report, "L  CSV export has a header and 12 rows",
                                csvLines.Length == 13 && csvLines[0].StartsWith("click,time,") && csvLines[12].StartsWith("12,"),
                                csvLines.Length + " lines");
                        }
                        // B: 3 right double-clicks
                        ClickOptions b = new ClickOptions();
                        b.X = center.X; b.Y = center.Y;
                        b.MinInterval = 0.2; b.MaxInterval = 0.25;
                        b.Button = MouseButtonChoice.Right; b.DoubleClick = true; b.MaxClicks = 3; b.Humanize = false;
                        hits.Clear();
                        engine.Start(b);
                        phase = 2; phaseStart = 0;
                        break;

                    case 2:
                        if (engine.Running) return;
                        if (phaseStart == 0) { phaseStart = now; return; }
                        if (now - phaseStart < 0.2) return;
                        {
                            int rightHits = 0;
                            foreach (Hit h in hits) if (h.Button == MouseButtons.Right) rightHits++;
                            allPassed &= Check(report, "B  3 right double-clicks = 6 right presses", rightHits == 6 && hits.Count == 6,
                                "received " + hits.Count + ", right " + rightHits);
                        }
                        // C: a long wait must be interruptible
                        ClickOptions c = new ClickOptions();
                        c.X = center.X; c.Y = center.Y;
                        c.MinInterval = 30; c.MaxInterval = 30; c.Humanize = false;
                        engine.Start(c);
                        phase = 3; phaseStart = now;
                        break;

                    case 3:
                        if (now - phaseStart < 0.4) return;
                        {
                            Stopwatch stopTimer = Stopwatch.StartNew();
                            engine.Stop();
                            stopLatencyMs = stopTimer.Elapsed.TotalMilliseconds;
                            allPassed &= Check(report, "C  Stop interrupts a 30 s wait", !engine.Running && stopLatencyMs < 200,
                                F(stopLatencyMs) + " ms, reason: " + engine.StopReason);
                        }
                        // D: time limit
                        ClickOptions d = new ClickOptions();
                        d.X = center.X; d.Y = center.Y;
                        d.MinInterval = 0.1; d.MaxInterval = 0.1;
                        d.MaxSeconds = 0.55; d.Humanize = false;
                        hits.Clear();
                        engine.Start(d);
                        phase = 4; phaseStart = now;
                        break;

                    case 4:
                        if (engine.Running) return;
                        allPassed &= Check(report, "D  time limit ends the run",
                            engine.StopReason.Contains("time limit") && engine.Count >= 4 && engine.Count <= 7,
                            engine.Count + " clicks in " + F(now - phaseStart) + " s, reason: " + engine.StopReason);
                        // F: human-like mode driving the real mouse
                        ClickOptions f = new ClickOptions();
                        f.X = center.X; f.Y = center.Y; f.JitterRadius = 10;
                        f.MinInterval = 0.1; f.MaxInterval = 0.3;
                        f.Humanize = true; f.HumanPauses = false; f.MaxClicks = 10;
                        hits.Clear();
                        engine.Start(f);
                        phase = 5; phaseStart = 0;
                        break;

                    case 5:
                        if (engine.Running) return;
                        if (phaseStart == 0) { phaseStart = now; return; }
                        if (now - phaseStart < 0.2) return;
                        {
                            double minGap = double.MaxValue, maxGap = 0, maxDist = 0;
                            for (int i = 0; i < hits.Count; i++)
                            {
                                maxDist = Math.Max(maxDist, hits[i].Distance);
                                if (i > 0)
                                {
                                    double gap = hits[i].Time - hits[i - 1].Time;
                                    minGap = Math.Min(minGap, gap);
                                    maxGap = Math.Max(maxGap, gap);
                                }
                            }
                            allPassed &= Check(report, "F  human-like: window received 10 clicks", hits.Count == 10, "received " + hits.Count);
                            allPassed &= Check(report, "F  human-like: gaps within 0.1-0.3 s", minGap >= 0.06 && maxGap <= 0.39,
                                "min " + F(minGap) + " s, max " + F(maxGap) + " s");
                            allPassed &= Check(report, "F  human-like: every click within 10 px", maxDist <= 11.5,
                                "furthest " + F(maxDist) + " px");
                        }
                        // G: park the cursor 80 px away, then send a press that carries its own position.
                        // It must land on the exact pixel, which proves a moving hand cannot shift a click.
                        hits.Clear();
                        for (int i = 0; i < 5; i++)
                        {
                            Native.SetCursorPos(center.X + 80, center.Y - 60);
                            Native.SendMouseAt(Native.LEFTDOWN, center.X, center.Y);
                            Native.SendMouseAt(Native.LEFTUP, center.X, center.Y);
                        }
                        phase = 7; phaseStart = now;
                        break;

                    case 7:
                        if (now - phaseStart < 0.4) return;
                        {
                            double worst = 0;
                            foreach (Hit h in hits) worst = Math.Max(worst, h.Distance);
                            allPassed &= Check(report, "G  press lands on the exact pixel even with the cursor elsewhere",
                                hits.Count == 5 && worst == 0, hits.Count + " received, furthest " + F(worst) + " px");
                        }
                        // E: emergency corner (only run if we can park the cursor there)
                        ClickOptions e2 = new ClickOptions();
                        e2.FixedPoint = false;
                        e2.MinInterval = 30; e2.MaxInterval = 30; e2.StartDelay = 30;
                        engine.Start(e2);
                        Rectangle scr = Screen.FromPoint(Cursor.Position).Bounds;
                        Native.SetCursorPos(scr.Left, scr.Top);
                        phase = 6; phaseStart = now;
                        break;

                    case 6:
                        if (engine.Running && now - phaseStart < 1.0) return;
                        allPassed &= Check(report, "E  cursor in the top-left corner stops it before any click",
                            !engine.Running && engine.Count == 0 && engine.StopReason.StartsWith("Emergency"),
                            engine.Count + " clicks, reason: " + engine.StopReason);
                        engine.Stop();
                        Native.SetCursorPos(cursorBefore.X, cursorBefore.Y);
                        report.AppendLine(allPassed ? "RESULT: PASS" : "RESULT: FAIL");
                        File.WriteAllText(reportPath, report.ToString());
                        timer.Stop();
                        form.Close();
                        break;
                }
            };
            form.Shown += delegate { timer.Start(); };
            Application.Run(form);
            return allPassed ? 0 : 1;
        }

        /// <summary>Statistical checks on the timing model itself. Pure arithmetic, no mouse involved.</summary>
        static bool RhythmChecks(StringBuilder report)
        {
            bool ok = true;
            const int n = 50000;

            // --- human-like, 1 to 2 s, pauses on
            ClickOptions h = new ClickOptions();
            h.MinInterval = 1; h.MaxInterval = 2; h.Humanize = true; h.HumanPauses = true;
            h.JitterRadius = 20; h.HoldMinMs = 60; h.HoldMaxMs = 130;
            Rhythm rhythm = new Rhythm(h, new Random(7));
            List<double> waits = new List<double>(n);
            int pauses = 0, outOfRange = 0, shortPauses = 0;
            double longestPause = 0;
            for (int i = 0; i < n; i++)
            {
                bool pause;
                double t = rhythm.NextInterval(out pause);
                if (pause)
                {
                    pauses++;
                    if (t <= 2.0) shortPauses++;
                    longestPause = Math.Max(longestPause, t);
                }
                else
                {
                    if (t < 1.0 || t > 2.0) outOfRange++;
                    waits.Add(t);
                }
            }
            double mean = 0;
            foreach (double w in waits) mean += w;
            mean /= waits.Count;
            double variance = 0, covariance = 0;
            for (int i = 0; i < waits.Count; i++)
            {
                variance += (waits[i] - mean) * (waits[i] - mean);
                if (i > 0) covariance += (waits[i] - mean) * (waits[i - 1] - mean);
            }
            double sd = Math.Sqrt(variance / waits.Count);
            double lag1 = covariance / variance;
            List<double> sorted = new List<double>(waits);
            sorted.Sort();
            double median = sorted[sorted.Count / 2];
            double p10 = sorted[sorted.Count / 10], p90 = sorted[sorted.Count * 9 / 10];
            HashSet<double> distinct = new HashSet<double>(waits);

            ok &= Check(report, "R  human: ordinary waits stay inside 1-2 s", outOfRange == 0, outOfRange + " outside");
            ok &= Check(report, "R  human: real spread", sd >= 0.15 && p90 - p10 >= 0.4,
                "sd " + F(sd) + " s, 10th-90th pct " + F(p10) + " to " + F(p90) + " s");
            ok &= Check(report, "R  human: right-skewed (slow tail)", mean > median + 0.01 && median < 1.45,
                "median " + F(median) + " s, mean " + F(mean) + " s");
            ok &= Check(report, "R  human: pace drifts (neighbouring waits related)", lag1 > 0.15 && lag1 < 0.6,
                "lag-1 correlation " + F(lag1));
            ok &= Check(report, "R  human: no repeated values", distinct.Count > waits.Count * 0.999,
                distinct.Count + " distinct of " + waits.Count);
            double pauseShare = pauses / (double)n;
            ok &= Check(report, "R  human: occasional pauses, all past the maximum",
                pauseShare > 0.02 && pauseShare < 0.05 && shortPauses == 0,
                (pauseShare * 100).ToString("0.0", CultureInfo.InvariantCulture) + "% of waits, longest " + F(longestPause) + " s");

            // --- human-like aim and hold
            double sumR = 0, maxR = 0;
            int holdLow = int.MaxValue, holdHigh = 0;
            double holdSum = 0;
            for (int i = 0; i < n; i++)
            {
                int dx, dy;
                rhythm.NextOffset(out dx, out dy);
                double r = Math.Sqrt(dx * dx + dy * dy);
                sumR += r; maxR = Math.Max(maxR, r);
                int hold = rhythm.NextHoldMs();
                holdLow = Math.Min(holdLow, hold); holdHigh = Math.Max(holdHigh, hold); holdSum += hold;
            }
            ok &= Check(report, "R  human: aim clusters near the spot, never past the radius",
                maxR <= 20.8 && sumR / n < 20 * 0.6, "mean " + F(sumR / n) + " px, furthest " + F(maxR) + " px of 20");
            ok &= Check(report, "R  human: hold times stay in 60-130 ms, skewed short",
                holdLow >= 60 && holdHigh <= 130 && holdSum / n < 95,
                holdLow + " to " + holdHigh + " ms, mean " + F(holdSum / n));

            // --- human-like with min = max still varies
            ClickOptions same = new ClickOptions();
            same.MinInterval = 1; same.MaxInterval = 1; same.Humanize = true; same.HumanPauses = false;
            Rhythm steady = new Rhythm(same, new Random(11));
            double low = double.MaxValue, high = 0;
            for (int i = 0; i < n; i++)
            {
                bool pause;
                double t = steady.NextInterval(out pause);
                low = Math.Min(low, t); high = Math.Max(high, t);
            }
            ok &= Check(report, "R  human: equal min and max still varies (0.90-1.25 s)",
                low >= 0.9 && high <= 1.25 && high - low > 0.25, F(low) + " to " + F(high) + " s");

            // --- even spread
            ClickOptions even = new ClickOptions();
            even.MinInterval = 1; even.MaxInterval = 2; even.Humanize = false;
            Rhythm flat = new Rhythm(even, new Random(13));
            double evenMean = 0; int evenOut = 0;
            for (int i = 0; i < n; i++)
            {
                bool pause;
                double t = flat.NextInterval(out pause);
                if (t < 1 || t > 2 || pause) evenOut++;
                evenMean += t / n;
            }
            ok &= Check(report, "R  even: uniform across 1-2 s, no pauses", evenOut == 0 && Math.Abs(evenMean - 1.5) < 0.01,
                "mean " + F(evenMean) + " s, " + evenOut + " outside");
            return ok;
        }

        static string F(double v)
        {
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }

        static bool Check(StringBuilder report, string name, bool ok, string detail)
        {
            report.AppendLine((ok ? "PASS  " : "FAIL  ") + name + "   [" + detail + "]");
            return ok;
        }
    }
}
