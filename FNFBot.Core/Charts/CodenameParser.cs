using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FridayNightFunkin
{
    public static class CodenameParser
    {
        public static bool IsCodenameRoot(JsonElement root)
        {
            if (root.TryGetProperty("codenameChart", out var flag) && flag.ValueKind == JsonValueKind.True)
                return true;
            if (root.TryGetProperty("strumLines", out var sl) && sl.ValueKind == JsonValueKind.Array)
                return sl.GetArrayLength() > 0;
            return false;
        }

        public static string[] GetMetaCandidates(string chartPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(chartPath)) ?? ".";
            string name = Path.GetFileNameWithoutExtension(chartPath);
            string parentDir = Path.GetDirectoryName(dir) ?? ".";

            return new[]
            {
                Path.Combine(parentDir, "meta.json"),
                Path.Combine(parentDir, "meta-" + name + ".json"),
                Path.Combine(dir, "meta.json"),
                Path.Combine(dir, "meta-" + name + ".json")
            };
        }

        public static void PopulateFNFSong(FNFSong song, JsonElement chartRoot, JsonElement? metaRoot)
        {
            song.Format = "codename";

            if (metaRoot != null)
            {
                var meta = metaRoot.Value;
                if (meta.TryGetProperty("bpm", out var bpm))
                    song.Bpm = bpm.GetDouble();
                if (meta.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String)
                    song.SongName = dn.GetString() ?? "Unknown";
            }

            if (chartRoot.TryGetProperty("meta", out var inlineMeta) && inlineMeta.ValueKind == JsonValueKind.Object)
            {
                if (song.Bpm <= 0 && inlineMeta.TryGetProperty("bpm", out var bpm))
                    song.Bpm = bpm.GetDouble();
                if (string.IsNullOrEmpty(song.SongName) && inlineMeta.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    song.SongName = name.GetString() ?? "Unknown";
            }

            if (chartRoot.TryGetProperty("scrollSpeed", out var speed))
                song.Speed = speed.GetDouble();

            if (chartRoot.TryGetProperty("meta", out var metaElem) && metaElem.ValueKind == JsonValueKind.Object)
            {
                if (metaElem.TryGetProperty("keyCount", out var keyCountVal) && keyCountVal.ValueKind == JsonValueKind.Number)
                    song.KeyCount = Math.Max(1, keyCountVal.GetInt32());
                else if (metaElem.TryGetProperty("mania", out var maniaVal) && maniaVal.ValueKind == JsonValueKind.Number)
                    song.KeyCount = PsychParser.ReadKeyCount(chartRoot);
            }

            if (!chartRoot.TryGetProperty("strumLines", out var sl) || sl.ValueKind != JsonValueKind.Array)
                return;

            int kc = song.KeyCount;
            var allNotes = new List<(double time, double length, int lane, bool isPlayer)>();

            foreach (var strumLine in sl.EnumerateArray())
            {
                int type = (int)ChartUtils.GetDouble(strumLine, "type");
                bool isPlayer = type == 1;

                if (kc < 2 && strumLine.TryGetProperty("notes", out var sample) && sample.ValueKind == JsonValueKind.Array)
                {
                    foreach (var n in sample.EnumerateArray())
                    {
                        int id = (int)ChartUtils.GetDouble(n, "id", 0);
                        if (id + 1 > kc) kc = id + 2;
                    }
                }

                if (!strumLine.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var n in notes.EnumerateArray())
                {
                    allNotes.Add((
                        ChartUtils.GetDouble(n, "time"),
                        ChartUtils.GetDouble(n, "sLen"),
                        (int)ChartUtils.GetDouble(n, "id"),
                        isPlayer
                    ));
                }
            }

            song.KeyCount = kc;

            allNotes.Sort((a, b) => a.time.CompareTo(b.time));

            double crochet = song.Bpm > 0 ? 60000.0 / song.Bpm : 600.0;
            double sectionLen = crochet * 4.0;

            if (allNotes.Count > 0)
            {
                int sectionCount = (int)Math.Ceiling(allNotes[^1].time / sectionLen) + 1;
                int idx = 0;

                for (int s = 0; s < sectionCount; s++)
                {
                    double start = s * sectionLen;
                    double end = (s + 1) * sectionLen;

                    var secNotes = new List<FNFSong.FNFNote>();
                    while (idx < allNotes.Count && allNotes[idx].time < end)
                    {
                        var (time, length, lane, isPlayer) = allNotes[idx];
                        secNotes.Add(new FNFSong.FNFNote
                        {
                            Time = time,
                            Length = length,
                            Lane = lane % kc,
                            IsPlayer = isPlayer
                        });
                        idx++;
                    }

                    song.Sections.Add(new FNFSong.FNFSection { Notes = secNotes });
                }
            }
        }
    }
}
