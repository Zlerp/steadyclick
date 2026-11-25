using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace SteadyClick
{
    /// <summary>Everything the window remembers between launches, kept in %APPDATA%\SteadyClick\settings.ini.</summary>
    sealed class Settings
    {
        public ClickOptions Options = new ClickOptions();
        public Keys ToggleKey = Keys.F6;
        public Keys PickKey = Keys.F7;
        public bool AlwaysOnTop;

        static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SteadyClick", "settings.ini");
            }
        }

        public static Settings Load()
        {
            Settings s = new Settings();

            // Default target: the middle of the main display (never the emergency-stop corner).
            Rectangle primary = Screen.PrimaryScreen.Bounds;
            s.Options.X = primary.Left + primary.Width / 2;
            s.Options.Y = primary.Top + primary.Height / 2;

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            ClickOptions o = s.Options;
            o.FixedPoint = Bool(map, "FixedPoint", o.FixedPoint);
            o.X = Int(map, "X", o.X);
            o.Y = Int(map, "Y", o.Y);
            o.JitterRadius = Int(map, "JitterRadius", o.JitterRadius);
            o.MinInterval = Dbl(map, "MinInterval", o.MinInterval);
            o.MaxInterval = Dbl(map, "MaxInterval", o.MaxInterval);
            o.Humanize = Bool(map, "Humanize", o.Humanize);
            o.HumanPauses = Bool(map, "HumanPauses", o.HumanPauses);
            o.HoldMinMs = Int(map, "HoldMinMs", o.HoldMinMs);
            o.HoldMaxMs = Int(map, "HoldMaxMs", o.HoldMaxMs);
            o.StartDelay = Dbl(map, "StartDelay", o.StartDelay);
            o.Button = (MouseButtonChoice)Math.Max(0, Math.Min(2, Int(map, "Button", (int)o.Button)));
            o.DoubleClick = Bool(map, "DoubleClick", o.DoubleClick);
            o.RestoreCursor = Bool(map, "RestoreCursor", o.RestoreCursor);
            o.MaxClicks = Int(map, "MaxClicks", (int)o.MaxClicks);
            o.MaxSeconds = Dbl(map, "MaxSeconds", o.MaxSeconds);
            o.CornerFailsafe = Bool(map, "CornerFailsafe", o.CornerFailsafe);
            s.ToggleKey = Key(map, "ToggleKey", s.ToggleKey);
            s.PickKey = Key(map, "PickKey", s.PickKey);
            s.AlwaysOnTop = Bool(map, "AlwaysOnTop", s.AlwaysOnTop);
            return s;
        }

        public void Save()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            ClickOptions o = Options;
            string[] lines =
            {
                "FixedPoint=" + o.FixedPoint,
                "X=" + o.X.ToString(inv),
                "Y=" + o.Y.ToString(inv),
                "JitterRadius=" + o.JitterRadius.ToString(inv),
                "MinInterval=" + o.MinInterval.ToString("R", inv),
                "MaxInterval=" + o.MaxInterval.ToString("R", inv),
                "Humanize=" + o.Humanize,
                "HumanPauses=" + o.HumanPauses,
                "HoldMinMs=" + o.HoldMinMs.ToString(inv),
                "HoldMaxMs=" + o.HoldMaxMs.ToString(inv),
                "StartDelay=" + o.StartDelay.ToString("R", inv),
                "Button=" + ((int)o.Button).ToString(inv),
                "DoubleClick=" + o.DoubleClick,
                "RestoreCursor=" + o.RestoreCursor,
                "MaxClicks=" + o.MaxClicks.ToString(inv),
                "MaxSeconds=" + o.MaxSeconds.ToString("R", inv),
                "CornerFailsafe=" + o.CornerFailsafe,
                "ToggleKey=" + ToggleKey,
                "PickKey=" + PickKey,
                "AlwaysOnTop=" + AlwaysOnTop
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, lines);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static bool Bool(Dictionary<string, string> map, string key, bool fallback)
        {
            string v; bool parsed;
            return map.TryGetValue(key, out v) && bool.TryParse(v, out parsed) ? parsed : fallback;
        }

        static int Int(Dictionary<string, string> map, string key, int fallback)
        {
            string v; int parsed;
            return map.TryGetValue(key, out v) &&
                   int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        static double Dbl(Dictionary<string, string> map, string key, double fallback)
        {
            string v; double parsed;
            return map.TryGetValue(key, out v) &&
                   double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
                   !double.IsNaN(parsed) && !double.IsInfinity(parsed) ? parsed : fallback;
        }

        static Keys Key(Dictionary<string, string> map, string key, Keys fallback)
        {
            string v; Keys parsed;
            return map.TryGetValue(key, out v) && Enum.TryParse(v, true, out parsed) ? parsed : fallback;
        }
    }
}
