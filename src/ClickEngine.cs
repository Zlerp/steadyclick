using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SteadyClick
{
    enum MouseButtonChoice { Left = 0, Right = 1, Middle = 2 }

    /// <summary>A snapshot of everything one run needs. Copied at Start, never changed mid-run.</summary>
    sealed class ClickOptions
    {
        public bool FixedPoint = true;      // false = click wherever the cursor happens to be
        public int X;
        public int Y;
        public int JitterRadius;            // px; each click lands at a random point within this radius
        public double MinInterval = 1.0;    // seconds between clicks, picked at random in [min, max]
        public double MaxInterval = 2.0;
        public bool Humanize = true;        // human-like rhythm instead of an even spread (see Rhythm)
        public bool HumanPauses = true;     // human-like only: an occasional wait well past the maximum
        public int HoldMinMs = 60;          // how long the button stays down
        public int HoldMaxMs = 130;
        public double StartDelay;           // seconds before the first click
        public MouseButtonChoice Button = MouseButtonChoice.Left;
        public bool DoubleClick;
        public bool RestoreCursor;          // put the cursor back where it was after each click
        public long MaxClicks;              // 0 = no limit
        public double MaxSeconds;           // 0 = no limit
        public bool CornerFailsafe = true;  // cursor in a screen's top-left corner stops the run

        public ClickOptions Clone()
        {
            return (ClickOptions)MemberwiseClone();
        }
    }

    /// <summary>
    /// Runs the click loop on its own thread. The UI polls <see cref="Running"/>, <see cref="Count"/>
    /// and <see cref="SecondsUntilNext"/>; nothing here touches a control.
    /// </summary>
    sealed class ClickEngine
    {
        enum WaitResult { Elapsed, Stopped, Failsafe, TimeLimit }

        const int SettleMs = 15;   // pause after moving the cursor so the target app sees the hover
        const int PollMs = 20;     // how often a wait wakes up to check the stop conditions

        readonly object gate = new object();
        readonly ManualResetEvent stopSignal = new ManualResetEvent(false);
        readonly Stopwatch clock = new Stopwatch();

        Thread worker;
        ClickOptions options;
        volatile bool running;
        volatile string stopReason = "";
        long count;
        long nextDueMs = -1;
        double runStartSeconds;

        readonly ClickLog log = new ClickLog();

        /// <summary>Every click of the current (or most recent) run.</summary>
        public ClickLog Log { get { return log; } }

        public bool Running { get { return running; } }

        public long Count { get { return Interlocked.Read(ref count); } }

        /// <summary>Why the last run ended, in plain words. Empty while running.</summary>
        public string StopReason { get { return stopReason; } }

        public double SecondsUntilNext
        {
            get
            {
                long due = Interlocked.Read(ref nextDueMs);
                if (due < 0 || !running) return 0;
                double left = (due - clock.ElapsedMilliseconds) / 1000.0;
                return left < 0 ? 0 : left;
            }
        }

        long lastClickMs = -1;

        public double SecondsSinceLastClick
        {
            get
            {
                long at = Interlocked.Read(ref lastClickMs);
                return at < 0 || !running ? 0 : Math.Max(0, (clock.ElapsedMilliseconds - at) / 1000.0);
            }
        }

        public void Start(ClickOptions o)
        {
            lock (gate)
            {
                if (running) return;
                options = o.Clone();
                stopSignal.Reset();
                Interlocked.Exchange(ref count, 0);
                Interlocked.Exchange(ref nextDueMs, -1);
                Interlocked.Exchange(ref lastClickMs, -1);
                stopReason = "";
                log.Begin();
                running = true;
                clock.Restart();
                worker = new Thread(Run);
                worker.IsBackground = true;
                worker.Name = "ClickEngine";
                worker.Start();
            }
        }

        /// <summary>Stops the run and waits briefly for the worker so a restart is always clean.</summary>
        public void Stop()
        {
            Thread t;
            lock (gate)
            {
                if (!running) return;
                stopSignal.Set();
                t = worker;
            }
            if (t != null && t != Thread.CurrentThread) t.Join(1000);
        }

        void Run()
        {
            string reason = "Stopped";
            try
            {
                Random rng = new Random();
                ClickOptions o = options;
                Rhythm rhythm = new Rhythm(o, rng);

                if (o.StartDelay > 0)
                {
                    WaitResult first = Wait(o.StartDelay, false);
                    if (first != WaitResult.Elapsed) { reason = Describe(first); return; }
                }

                runStartSeconds = clock.Elapsed.TotalSeconds;
                double firstClickAt = -1, previousClickAt = -1;
                bool cameAfterPause = false;
                while (true)
                {
                    double clickAt = clock.Elapsed.TotalSeconds;
                    ClickRecord record = new ClickRecord();
                    record.Time = DateTime.Now;
                    string failure = Click(o, rng, rhythm, record);
                    if (failure != null) { reason = failure; return; }

                    long done = Interlocked.Increment(ref count);
                    if (firstClickAt < 0) firstClickAt = clickAt;
                    record.Index = (int)done;
                    record.SinceStart = clickAt - firstClickAt;
                    record.Wait = previousClickAt < 0 ? 0 : clickAt - previousClickAt;
                    record.Pause = cameAfterPause;
                    record.Button = o.Button;
                    record.Double = o.DoubleClick;
                    log.Add(record);
                    previousClickAt = clickAt;
                    Interlocked.Exchange(ref lastClickMs, (long)(clickAt * 1000));
                    if (o.MaxClicks > 0 && done >= o.MaxClicks)
                    {
                        reason = "Finished: reached " + o.MaxClicks + " clicks";
                        return;
                    }

                    // The interval is measured click-start to click-start, so the hold time
                    // does not stretch it.
                    bool pause;
                    double gap = rhythm.NextInterval(out pause);
                    cameAfterPause = pause;
                    double remaining = gap - (clock.Elapsed.TotalSeconds - clickAt);
                    WaitResult w = Wait(remaining, true);
                    if (w != WaitResult.Elapsed) { reason = Describe(w); return; }
                }
            }
            catch (Exception ex)
            {
                reason = "Stopped by an error: " + ex.Message;
            }
            finally
            {
                Interlocked.Exchange(ref nextDueMs, -1);
                log.AutoSave();
                stopReason = reason;
                running = false;
            }
        }

        static string Describe(WaitResult r)
        {
            switch (r)
            {
                case WaitResult.Failsafe: return "Emergency stop: cursor hit the top-left corner";
                case WaitResult.TimeLimit: return "Finished: reached the time limit";
                default: return "Stopped";
            }
        }

        /// <summary>Sleeps for the given time while watching every stop condition.</summary>
        WaitResult Wait(double seconds, bool countsTowardLimit)
        {
            long now = clock.ElapsedMilliseconds;
            long due = now + (long)Math.Round(Math.Max(0, seconds) * 1000.0);
            Interlocked.Exchange(ref nextDueMs, due);

            while (true)
            {
                if (options.CornerFailsafe && CursorInCorner()) return WaitResult.Failsafe;
                if (countsTowardLimit && options.MaxSeconds > 0 &&
                    clock.Elapsed.TotalSeconds - runStartSeconds >= options.MaxSeconds)
                {
                    return WaitResult.TimeLimit;
                }

                long left = due - clock.ElapsedMilliseconds;
                if (left <= 0) return stopSignal.WaitOne(0) ? WaitResult.Stopped : WaitResult.Elapsed;
                if (stopSignal.WaitOne((int)Math.Min(left, PollMs))) return WaitResult.Stopped;
            }
        }

        static bool CursorInCorner()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return false;
            Rectangle screen = Screen.FromPoint(new Point(p.X, p.Y)).Bounds;
            return p.X <= screen.Left + 2 && p.Y <= screen.Top + 2;
        }

        /// <summary>Performs one click (or double click). Returns null on success, otherwise why it failed.</summary>
        string Click(ClickOptions o, Random rng, Rhythm rhythm, ClickRecord record)
        {
            Native.POINT original;
            bool haveOriginal = Native.GetCursorPos(out original);
            record.X = original.X;
            record.Y = original.Y;

            int x = o.X, y = o.Y;
            if (o.FixedPoint)
            {
                int dx, dy;
                rhythm.NextOffset(out dx, out dy);
                x += dx;
                y += dy;
                record.X = x;
                record.Y = y;
                record.Offset = Math.Sqrt((double)dx * dx + (double)dy * dy);
                if (!Native.SetCursorPos(x, y)) return "Stopped: Windows would not move the cursor";
                if (stopSignal.WaitOne(SettleMs)) { Restore(o, haveOriginal, original); return "Stopped"; }
            }

            uint down, up;
            Flags(o.Button, out down, out up);

            int presses = o.DoubleClick ? 2 : 1;
            for (int i = 0; i < presses; i++)
            {
                if (i > 0 && stopSignal.WaitOne(rng.Next(50, 91))) { Restore(o, haveOriginal, original); return "Stopped"; }

                // With a fixed spot, the press and the release each carry the position themselves.
                // If the user's hand nudges the mouse mid-click, the click still lands on the spot
                // and never turns into a drag.
                if (!(o.FixedPoint ? Native.SendMouseAt(down, x, y) : Native.SendMouse(down)))
                {
                    Restore(o, haveOriginal, original);
                    return "Stopped: Windows blocked the click. If the target app runs as administrator, " +
                           "run SteadyClick as administrator too";
                }
                bool cancelled;
                try
                {
                    int hold = rhythm.NextHoldMs();
                    if (i == 0) record.HoldMs = hold;
                    cancelled = stopSignal.WaitOne(hold);
                }
                finally
                {
                    // never leave the button held down
                    if (o.FixedPoint) Native.SendMouseAt(up, x, y);
                    else Native.SendMouse(up);
                }
                if (cancelled) { Restore(o, haveOriginal, original); return "Stopped"; }
            }

            if (o.FixedPoint && o.RestoreCursor && haveOriginal)
            {
                stopSignal.WaitOne(SettleMs);
                Native.SetCursorPos(original.X, original.Y);
            }
            return null;
        }

        static void Restore(ClickOptions o, bool haveOriginal, Native.POINT original)
        {
            if (o.FixedPoint && o.RestoreCursor && haveOriginal) Native.SetCursorPos(original.X, original.Y);
        }

        static void Flags(MouseButtonChoice button, out uint down, out uint up)
        {
            // SendInput works on physical buttons. If the user swapped buttons in Windows,
            // swap here too so "Left" still means their primary button.
            if (Native.ButtonsSwapped)
            {
                if (button == MouseButtonChoice.Left) button = MouseButtonChoice.Right;
                else if (button == MouseButtonChoice.Right) button = MouseButtonChoice.Left;
            }
            switch (button)
            {
                case MouseButtonChoice.Right: down = Native.RIGHTDOWN; up = Native.RIGHTUP; break;
                case MouseButtonChoice.Middle: down = Native.MIDDLEDOWN; up = Native.MIDDLEUP; break;
                default: down = Native.LEFTDOWN; up = Native.LEFTUP; break;
            }
        }
    }
}
