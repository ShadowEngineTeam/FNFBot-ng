using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FridayNightFunkin;

namespace FNFBot.Core
{
    public class ChartEntry
    {
        public string Label { get; set; }       // shown in the tree
        public string Path { get; set; }         // chart json path
        public string Difficulty { get; set; }   // V-Slice difficulty, else null
    }

    public class SongEntry
    {
        public string Name { get; set; }
        public List<ChartEntry> Charts { get; } = new List<ChartEntry>();
    }

    /// <summary>
    /// Scans a game/engine/mod folder for playable charts. Portable (pure IO + parsers),
    /// so any UI can build its song browser from the result.
    /// </summary>
    public static class ChartLibrary
    {
        public static List<SongEntry> Scan(string root)
        {
            var songs = new List<SongEntry>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return songs;

            foreach (string dataRoot in GetDataRoots(root))
                ScanDataRoot(dataRoot, null, songs);

            // Mod containers: Psych/base use "mods", Troll Engine uses "content". Each holds
            // one folder per mod, with charts under the mod's "songs" (Codename/Troll) or
            // "data" (Psych) subfolder.
            foreach (string container in new[] { "mods", "content" })
            {
                string containerDir = Path.Combine(root, container);
                if (!Directory.Exists(containerDir))
                    continue;

                foreach (string mod in Directory.GetDirectories(containerDir))
                {
                    string modData = Path.Combine(mod, "data");
                    if (Directory.Exists(modData))
                        ScanDataRoot(modData, Path.GetFileName(mod), songs);

                    string modSongs = Path.Combine(mod, "songs"); // Codename / Troll mods
                    if (Directory.Exists(modSongs))
                        ScanDataRoot(modSongs, Path.GetFileName(mod), songs);
                }
            }

            return songs;
        }

        public static int CountCharts(List<SongEntry> songs)
            => songs?.Sum(s => s.Charts.Count) ?? 0;

        private static IEnumerable<string> GetDataRoots(string root)
        {
            string[] candidates =
            {
                Path.Combine(root, "assets", "data", "songs"),                       // FNF 1.8
                Path.Combine(root, "assets", "data"),                                // base game / Psych
                Path.Combine(root, "assets", "shared", "data"),                      // some forks
                Path.Combine(root, "assets", "funkin_resources", "shared", "data"),  // Shadow Engine
                Path.Combine(root, "assets", "songs"),                               // Codename Engine
                Path.Combine(root, "data"),
                Path.Combine(root, "songs"),
                root
            };

            foreach (string c in candidates)
                if (Directory.Exists(c))
                    yield return c;
        }

        private static void ScanDataRoot(string dataRoot, string label, List<SongEntry> songs)
        {
            foreach (string songDir in Directory.GetDirectories(dataRoot))
            {
                string chartsDir = Path.Combine(songDir, "charts");
                bool isCodename = Directory.Exists(chartsDir);

                string[] charts = isCodename
                    ? Directory.GetFiles(chartsDir, "*.json").Where(IsChartJson).ToArray()
                    : Directory.GetFiles(songDir, "*.json")
                        .Where(f => !Path.GetFileName(f).StartsWith("events", StringComparison.OrdinalIgnoreCase))
                        .Where(f => Path.GetFileName(f).IndexOf("-metadata", StringComparison.OrdinalIgnoreCase) < 0)
                        .Where(IsChartJson)
                        .ToArray();

                if (charts.Length == 0)
                    continue;

                string songName = Path.GetFileName(songDir);
                var song = new SongEntry { Name = label == null ? songName : $"{songName} [{label}]" };

                foreach (string chart in charts)
                {
                    string fileName = Path.GetFileName(chart);
                    string[] difficulties = VSliceParser.ListDifficulties(chart);

                    if (difficulties.Length > 0)
                    {
                        foreach (string diff in difficulties)
                            song.Charts.Add(new ChartEntry { Label = $"{fileName} ({diff})", Path = chart, Difficulty = diff });
                    }
                    else
                    {
                        song.Charts.Add(new ChartEntry { Label = fileName, Path = chart, Difficulty = null });
                    }
                }

                if (song.Charts.Count > 0)
                    songs.Add(song);
            }
        }

        private static bool IsChartJson(string path)
        {
            try
            {
                using var doc = ChartUtils.LoadJson(path);
                var root = doc.RootElement;
                return (PsychParser.IsPsychRoot(root) && HasPsychNotes(root))
                    || (VSliceParser.IsVSliceRoot(root) && HasVSliceNotes(root))
                    || (CodenameParser.IsCodenameRoot(root) && HasCodenameNotes(root));
            }
            catch { return false; }
        }

        /// <summary>True if the Psych song object has at least one section with notes.</summary>
        private static bool HasPsychNotes(JsonElement root)
        {
            var song = root.GetProperty("song");
            if (song.ValueKind == JsonValueKind.String)
                return root.TryGetProperty("notes", out var secs) && secs.ValueKind == JsonValueKind.Array && secs.GetArrayLength() > 0;
            return song.TryGetProperty("notes", out var sections) && sections.ValueKind == JsonValueKind.Array && sections.GetArrayLength() > 0;
        }

        /// <summary>True if the V-Slice chart has at least one difficulty with notes.</summary>
        private static bool HasVSliceNotes(JsonElement root)
        {
            if (!root.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var kv in notes.EnumerateObject())
                if (kv.Value.ValueKind == JsonValueKind.Array && kv.Value.GetArrayLength() > 0)
                    return true;
            return false;
        }

        /// <summary>True if the Codename chart has at least one strum line with notes.</summary>
        private static bool HasCodenameNotes(JsonElement root)
        {
            if (!root.TryGetProperty("strumLines", out var sl) || sl.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var line in sl.EnumerateArray())
                if (line.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array && notes.GetArrayLength() > 0)
                    return true;
            return false;
        }
    }
}
