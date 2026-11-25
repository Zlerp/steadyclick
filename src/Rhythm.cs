using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SteadyClick
{
    /// <summary>
    /// Decides how long to wait, how long to hold, and how far off-centre to land for each click.
    ///
    /// "Even" mode draws every value uniformly from its range.
    ///
    /// "Human-like" mode copies what repeated clicking by a person actually looks like:
    ///   * Waits are right-skewed (log-normal shaped): bunched around a typical pace near the
    ///     quick end of the range, with a thinner tail of slower ones.
    ///   * Consecutive waits are related. A slow drift rides under the click-to-click noise, so the
    ///     pace speeds up and eases off over several clicks instead of jumping around independently.
    ///   * Now and then attention lapses and one wait runs well past the usual maximum.
    ///   * Hold times follow the same skewed, drifting shape.
    ///   * Aim scatters like a bell curve around the spot and the centre of that scatter wanders,
    ///     rather than filling the circle evenly.
    /// </summary>
    sealed class Rhythm
    {
        const double Cut = 2.4;            // the bell curve is trimmed at +/- this many deviations
        const double Skew = 0.35;          // how lopsided the wait distribution is
        const double DriftWeight = 0.6;    // share of the variation that comes from the slow drift
        const double NoiseWeight = 0.8;    // share from click-to-click noise (0.6^2 + 0.8^2 = 1)
        public const double PauseChance = 0.035;

        readonly ClickOptions o;
        readonly Random rng;
        readonly double lo, hi;
        double intervalDrift, holdDrift, aimDriftX, aimDriftY;
        bool lastWasPause;
        double spare;
        bool hasSpare;

        public Rhythm(ClickOptions options, Random random)
        {
            o = options;
            rng = random;
            EffectiveRange(o, out lo, out hi);
            intervalDrift = Gauss();
            holdDrift = Gauss();
            aimDriftX = Gauss();
            aimDriftY = Gauss();
        }

        /// <summary>
        /// The wait range actually used. Nobody clicks to a metronome, so in human-like mode a
        /// range narrower than 15% is opened up around its middle.
        /// </summary>
        public static void EffectiveRange(ClickOptions o, out double lo, out double hi)
        {
            lo = Math.Min(o.MinInterval, o.MaxInterval);
            hi = Math.Max(o.MinInterval, o.MaxInterval);
            if (o.Humanize && hi < lo * 1.15)
            {
                double middle = (lo + hi) / 2;
                lo = middle * 0.90;
                hi = middle * 1.25;
            }
        }

        /// <summary>Seconds until the next click. <paramref name="pause"/> is true for a long lapse.</summary>
        public double NextInterval(out bool pause)
        {
            pause = false;
            if (!o.Humanize) return lo + rng.NextDouble() * (hi - lo);

            double wait = lo + (hi - lo) * Shape(Correlated(ref intervalDrift, 0.92));

            if (o.HumanPauses && !lastWasPause && rng.NextDouble() < PauseChance)
            {
                double typical = lo + (hi - lo) * Shape(0);
                double r = rng.NextDouble();
                // Mostly short lapses, occasionally a long one; never less than a noticeable beat.
                wait = hi + Math.Max(0.25 + 0.5 * r, typical * (0.5 + 3.5 * r * r));
                pause = true;
            }
            lastWasPause = pause;
            return wait;
        }

        public int NextHoldMs()
        {
            int min = Math.Min(o.HoldMinMs, o.HoldMaxMs), max = Math.Max(o.HoldMinMs, o.HoldMaxMs);
            if (!o.Humanize) return rng.Next(min, max + 1);
            return (int)Math.Round(min + (max - min) * Shape(Correlated(ref holdDrift, 0.85)));
        }

        /// <summary>How far from the exact spot this click lands. Never further than the radius.</summary>
        public void NextOffset(out int dx, out int dy)
        {
            dx = 0; dy = 0;
            double radius = o.JitterRadius;
            if (radius <= 0) return;

            if (!o.Humanize)
            {
                // sqrt keeps the points evenly spread over the disc instead of bunched in the middle
                double r = radius * Math.Sqrt(rng.NextDouble());
                double a = rng.NextDouble() * Math.PI * 2;
                dx = (int)Math.Round(r * Math.Cos(a));
                dy = (int)Math.Round(r * Math.Sin(a));
                return;
            }

            const double rho = 0.9;
            double step = Math.Sqrt(1 - rho * rho);
            aimDriftX = rho * aimDriftX + step * Gauss();
            aimDriftY = rho * aimDriftY + step * Gauss();
            for (int attempt = 0; attempt < 12; attempt++)
            {
                double x = radius * (0.22 * aimDriftX + 0.33 * Gauss());
                double y = radius * (0.22 * aimDriftY + 0.33 * Gauss());
                if (x * x + y * y <= radius * radius)
                {
                    dx = (int)Math.Round(x);
                    dy = (int)Math.Round(y);
                    return;
                }
            }
        }

        /// <summary>A standard-normal value made of slow drift plus fresh noise, trimmed to +/- Cut.</summary>
        double Correlated(ref double drift, double rho)
        {
            drift = rho * drift + Math.Sqrt(1 - rho * rho) * Gauss();
            for (int attempt = 0; attempt < 12; attempt++)
            {
                double z = DriftWeight * drift + NoiseWeight * Gauss();
                if (Math.Abs(z) <= Cut) return z;
            }
            return Math.Max(-Cut, Math.Min(Cut, DriftWeight * drift));
        }

        /// <summary>Maps a trimmed normal value onto 0..1 with a log-normal (right-skewed) shape.</summary>
        static double Shape(double z)
        {
            double low = Math.Exp(-Skew * Cut), high = Math.Exp(Skew * Cut);
            return (Math.Exp(Skew * z) - low) / (high - low);
        }

        double Gauss()
        {
            if (hasSpare) { hasSpare = false; return spare; }
            double u, v, s;
            do
            {
                u = rng.NextDouble() * 2 - 1;
                v = rng.NextDouble() * 2 - 1;
                s = u * u + v * v;
            } while (s >= 1 || s == 0);
            double m = Math.Sqrt(-2 * Math.Log(s) / s);
            spare = v * m;
            hasSpare = true;
            return u * m;
        }
    }

    /// <summary>A small histogram of the waits the current settings will produce.</summary>
    sealed class RhythmPreview : Control
    {
        const int Bins = 30;
        const int Samples = 20000;

        static readonly Color BarColor = Color.FromArgb(52, 168, 113);
        static readonly Color PauseColor = Color.FromArgb(217, 119, 6);
        static readonly Color AxisColor = Color.FromArgb(209, 213, 219);
        static readonly Color LabelColor = Color.FromArgb(107, 114, 128);

        readonly double[] share = new double[Bins];
        double pauseShare, lo = 1, hi = 2;

        /// <summary>One or two plain sentences describing the same thing the chart shows.</summary>
        public string Caption { get; private set; }

        public RhythmPreview()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Caption = "";
        }

        public static string Secs(double v)
        {
            return v.ToString(v < 10 ? "0.00" : "0.0", CultureInfo.CurrentCulture);
        }

        public void Recalculate(ClickOptions o)
        {
            Rhythm.EffectiveRange(o, out lo, out hi);
            Array.Clear(share, 0, Bins);
            pauseShare = 0;

            if (!o.Humanize)
            {
                for (int i = 0; i < Bins; i++) share[i] = 1.0 / Bins;
                Caption = hi - lo < 1e-9
                    ? "Every wait is exactly " + Secs(lo) + " s. No variation."
                    : "Every wait from " + Secs(lo) + " to " + Secs(hi) + " s is equally likely.";
            }
            else
            {
                // Fixed seed: the picture only changes when the settings do.
                Rhythm rhythm = new Rhythm(o, new Random(20240613));
                List<double> normal = new List<double>(Samples);
                int pauses = 0;
                double longest = 0;
                for (int i = 0; i < Samples; i++)
                {
                    bool pause;
                    double t = rhythm.NextInterval(out pause);
                    if (pause) { pauses++; longest = Math.Max(longest, t); continue; }
                    normal.Add(t);
                    int bin = (int)((t - lo) / (hi - lo) * Bins);
                    share[Math.Max(0, Math.Min(Bins - 1, bin))] += 1.0 / Samples;
                }
                pauseShare = pauses / (double)Samples;
                normal.Sort();
                Caption = "Usually about " + Secs(normal[normal.Count / 2]) + " s, mostly " +
                          Secs(normal[normal.Count / 10]) + " to " + Secs(normal[normal.Count * 9 / 10]) + " s.";
                if (pauses > 0)
                {
                    Caption += " About 1 wait in " + Math.Round(Samples / (double)pauses) +
                               " is a longer pause, up to " + longest.ToString("0.#") + " s.";
                }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int w = ClientSize.Width, h = ClientSize.Height;
            int labelH = Font.Height + 2;
            int chartH = h - labelH - 1;
            if (w < 40 || chartH < 8) return;

            bool showPause = pauseShare > 0;
            int pauseW = showPause ? w / 7 : 0;
            int gap = showPause ? w / 24 : 0;
            int mainW = w - pauseW - gap;

            double max = pauseShare;
            for (int i = 0; i < Bins; i++) max = Math.Max(max, share[i]);
            if (max <= 0) return;

            Color bar = Enabled ? BarColor : Color.FromArgb(170, 205, 188);
            Color pauseBar = Enabled ? PauseColor : Color.FromArgb(226, 196, 160);
            using (SolidBrush brush = new SolidBrush(bar))
            {
                float barW = mainW / (float)Bins;
                for (int i = 0; i < Bins; i++)
                {
                    float barH = (float)(share[i] / max * (chartH - 2));
                    if (share[i] > 0 && barH < 1) barH = 1;
                    g.FillRectangle(brush, i * barW, chartH - barH, Math.Max(1f, barW - 1f), barH);
                }
            }
            if (showPause)
            {
                using (SolidBrush brush = new SolidBrush(pauseBar))
                {
                    float barH = (float)(pauseShare / max * (chartH - 2));
                    g.FillRectangle(brush, mainW + gap, chartH - barH, pauseW, barH);
                }
            }
            using (Pen axis = new Pen(AxisColor))
            {
                g.DrawLine(axis, 0, chartH, mainW - 1, chartH);
                if (showPause) g.DrawLine(axis, mainW + gap, chartH, w - 1, chartH);
            }

            TextFormatFlags baseFlags = TextFormatFlags.NoPadding | TextFormatFlags.Top;
            Rectangle axisRow = new Rectangle(0, chartH + 2, mainW, labelH);
            TextRenderer.DrawText(g, Secs(lo) + " s", Font, axisRow, LabelColor, baseFlags | TextFormatFlags.Left);
            TextRenderer.DrawText(g, Secs(hi) + " s", Font, axisRow, LabelColor, baseFlags | TextFormatFlags.Right);
            if (showPause)
            {
                Rectangle pauseRow = new Rectangle(mainW + gap - 20, chartH + 2, pauseW + 20, labelH);
                TextRenderer.DrawText(g, "pauses", Font, pauseRow, LabelColor, baseFlags | TextFormatFlags.Right);
            }
        }
    }
}
