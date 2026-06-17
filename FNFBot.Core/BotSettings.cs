using System;
using System.IO;

namespace FNFBot.Core
{
    /// <summary>
    /// Tunable timing values, persisted to <c>bot.settings</c> next to the executable.
    /// Backwards compatible with the old single-number (offset only) file.
    /// </summary>
    public class BotSettings
    {
        private const string SettingsFile = "bot.settings";

        public int Offset = 25;        // ms to press before/after the note time
        public int PressMs = 40;       // how long a tapped note is held down
        public int HoldReleaseMs = 20; // extra ms a sustain is held past its end

        public static BotSettings Load()
        {
            var s = new BotSettings();
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    s.Save();
                    return s;
                }

                foreach (string raw in File.ReadAllLines(SettingsFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0)
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq < 0)
                    {
                        if (int.TryParse(line, out int legacy))
                            s.Offset = legacy;
                        continue;
                    }

                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    if (!int.TryParse(line.Substring(eq + 1).Trim(), out int num))
                        continue;

                    switch (key)
                    {
                        case "offset": s.Offset = num; break;
                        case "press": s.PressMs = Math.Max(1, num); break;
                        case "hold": s.HoldReleaseMs = num; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(SettingsFile,
                    $"offset={Offset}\npress={PressMs}\nhold={HoldReleaseMs}\n");
            }
            catch { }
        }
    }
}
