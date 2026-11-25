using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SteadyClick
{
    /// <summary>What happened on one click.</summary>
    sealed class ClickRecord
    {
        public int Index;              // 1-based
        public DateTime Time;          // wall-clock time of the click
        public double SinceStart;      // seconds since the first click of the run
        public double Wait;            // seconds since the previous click; 0 for the first
        public bool Pause;             // that wait was one of the human-like long pauses
        public int HoldMs;             // how long the button was held
        public int X;
        public int Y;
        public double Offset;          // px from the exact spot (0 when following the cursor)
        public MouseButtonChoice Button;
        public bool Double;
    }

    /// <summary>Totals for one run, worked out from its records.</summary>
    sealed class SessionStats
    {
        public int Clicks;
        public DateTime Started;
        public double Elapsed;         // seconds from first to last click
        public double ClicksPerMinute;
        public int Waits;              // Clicks - 1
        public double AverageWait;
        public double MedianWait;
        public double ShortestWait;
        public double LongestWait;
        public int Pauses;
        public double AverageHoldMs;
        public double FurthestOffset;
    }

    /// <summary>
    /// The record of the current (or most recent) run. Written by the click thread, read by the
    /// window, saved to a CSV file when the run ends.
    /// </summary>
    sealed class ClickLog
    {
        const int Capacity = 500000;   // about 6 days at one click a second; keeps memory bounded
        const int KeepFiles = 200;

        /// <summary>Tests turn this off so they do not leave files behind.</summary>
        public static bool AutoSaveEnabled = true;

        readonly object gate = new object();
        readonly List<ClickRecord> items = new List<ClickRecord>();
        DateTime started = DateTime.Now;
        string savedPath = "";

        public static string Folder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SteadyClick", "logs");
            }
        }

        public int Count { get { lock (gate) return items.Count; } }

        /// <summary>Where the last finished run was saved, or empty.</summary>
        public string SavedPath { get { lock (gate) return savedPath; } }

        public ClickRecord this[int index]
        {
            get { lock (gate) return index >= 0 && index < items.Count ? items[index] : null; }
        }

        public void Begin()
        {
            lock (gate)
            {
                items.Clear();
                started = DateTime.Now;
                savedPath = "";
            }
        }

        public void Add(ClickRecord record)
        {
            lock (gate)
            {
                if (items.Count < Capacity) items.Add(record);
            }
        }

        public SessionStats Stats()
        {
            ClickRecord[] copy;
            SessionStats s = new SessionStats();
            lock (gate)
            {
                copy = items.ToArray();
                s.Started = started;
            }
            s.Clicks = copy.Length;
            if (copy.Length == 0) return s;

            s.Elapsed = copy[copy.Length - 1].SinceStart;
            s.ClicksPerMinute = s.Elapsed > 0 ? (copy.Length - 1) / s.Elapsed * 60.0 : 0;

            double holdSum = 0;
            List<double> waits = new List<double>(copy.Length);
            for (int i = 0; i < copy.Length; i++)
            {
                holdSum += copy[i].HoldMs;
                s.FurthestOffset = Math.Max(s.FurthestOffset, copy[i].Offset);
                if (copy[i].Pause) s.Pauses++;
                if (i > 0) waits.Add(copy[i].Wait);
            }
            s.AverageHoldMs = holdSum / copy.Length;
            s.Waits = waits.Count;
            if (waits.Count > 0)
            {
                double sum = 0;
                foreach (double w in waits) sum += w;
                s.AverageWait = sum / waits.Count;
                waits.Sort();
                s.ShortestWait = waits[0];
                s.LongestWait = waits[waits.Count - 1];
                s.MedianWait = waits.Count % 2 == 1
                    ? waits[waits.Count / 2]
                    : (waits[waits.Count / 2 - 1] + waits[waits.Count / 2]) / 2;
            }
            return s;
        }

        /// <summary>Writes every record as CSV. Throws on I/O failure.</summary>
        public void SaveCsv(string path)
        {
            ClickRecord[] copy;
            lock (gate) copy = items.ToArray();

            CultureInfo inv = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder(copy.Length * 64 + 128);
            sb.AppendLine("click,time,seconds_since_start,wait_before_s,long_pause,hold_ms,x,y,offset_px,button,double_click");
            foreach (ClickRecord r in copy)
            {
                sb.Append(r.Index.ToString(inv)).Append(',');
                sb.Append(r.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", inv)).Append(',');
                sb.Append(r.SinceStart.ToString("0.000", inv)).Append(',');
                sb.Append(r.Wait.ToString("0.000", inv)).Append(',');
                sb.Append(r.Pause ? "yes" : "no").Append(',');
                sb.Append(r.HoldMs.ToString(inv)).Append(',');
                sb.Append(r.X.ToString(inv)).Append(',');
                sb.Append(r.Y.ToString(inv)).Append(',');
                sb.Append(r.Offset.ToString("0.0", inv)).Append(',');
                sb.Append(r.Button.ToString().ToLowerInvariant()).Append(',');
                sb.AppendLine(r.Double ? "yes" : "no");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Saves the finished run to the log folder and trims old files. Never throws.</summary>
        public void AutoSave()
        {
            if (!AutoSaveEnabled || Count == 0) return;
            try
            {
                Directory.CreateDirectory(Folder);
                DateTime when;
                lock (gate) when = started;
                string path = Path.Combine(Folder,
                    "clicks-" + when.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".csv");
                SaveCsv(path);
                lock (gate) savedPath = path;

                string[] files = Directory.GetFiles(Folder, "clicks-*.csv");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);   // the names sort by date
                for (int i = 0; i < files.Length - KeepFiles; i++) File.Delete(files[i]);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
