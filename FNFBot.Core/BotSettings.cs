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

        public int Offset = 0;         // ms to press before/after the note time
        public int FailCount = 0;       // reserved, not yet enforced
        public int PressMinMs = 56;     // press low bound
        public int PressMaxMs = 110;    // press high bound
        public int HoldMinMs = 44;      // hold release low bound
        public int HoldMaxMs = 90;      // hold release high bound
        public bool AutoFail = false;   // randomly miss notes
        public int PressRate = 100;     // accuracy 0-100 (100 = perfect)

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
                        case "failcount": s.FailCount = Math.Max(0, num); break;
                        case "pressmin": s.PressMinMs = Math.Max(1, Math.Min(num, s.PressMaxMs)); break;
                        case "pressmax": s.PressMaxMs = Math.Max(s.PressMinMs, num); break;
                        case "holdmin": s.HoldMinMs = Math.Max(0, Math.Min(num, s.HoldMaxMs)); break;
                        case "holdmax": s.HoldMaxMs = Math.Max(s.HoldMinMs, num); break;
                        case "autofail": s.AutoFail = num != 0; break;
                        case "pressrate": s.PressRate = Math.Max(0, Math.Min(100, num)); break;
                        default: break;
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
                    $"offset={Offset}\nfailcount={FailCount}\n" +
                    $"pressmin={PressMinMs}\npressmax={PressMaxMs}\n" +
                    $"holdmin={HoldMinMs}\nholdmax={HoldMaxMs}\n" +
                    $"autofail={(AutoFail ? 1 : 0)}\npressrate={PressRate}\n");
            }
            catch { }
        }
    }
}
