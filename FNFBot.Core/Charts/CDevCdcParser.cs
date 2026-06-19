using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FridayNightFunkin
{
    public static class CDevCdcParser
    {
        public static bool IsCdcRoot(JsonElement root)
        {
            return root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array;
        }

        public static bool HasCdcNotes(JsonElement root)
        {
            if (!root.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array)
                return false;
            return notes.GetArrayLength() > 0;
        }

        public static void Parse(FNFSong song, JsonElement root)
        {
            song.Format = "cdev_cdc";

            if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
            {
                song.SongName = ChartUtils.GetString(info, "name", "Unknown");
                song.Bpm = ChartUtils.GetDouble(info, "bpm", 100);
                song.Speed = ChartUtils.GetDouble(info, "speed", 1);
            }

            if (!root.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array)
                return;

            var allNotes = new List<(double time, double length, int lane)>();

            foreach (var n in notes.EnumerateArray())
            {
                if (n.ValueKind != JsonValueKind.Array || n.GetArrayLength() < 2)
                    continue;

                double time = ChartUtils.ElementToDouble(n[0]);
                int lane = (int)ChartUtils.ElementToDouble(n[1]);

                if (lane < 0)
                    continue;

                double length = n.GetArrayLength() > 2 ? ChartUtils.ElementToDouble(n[2]) : 0;

                allNotes.Add((time, length, lane));
            }

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
                        var (time, length, lane) = allNotes[idx];
                        int dir = lane % 4;
                        secNotes.Add(new FNFSong.FNFNote
                        {
                            Time = time,
                            Length = length,
                            Type = (FNFSong.NoteType)(lane >= 4 ? dir + 4 : dir)
                        });
                        idx++;
                    }

                    song.Sections.Add(new FNFSong.FNFSection
                    {
                        MustHitSection = true,
                        Notes = secNotes
                    });
                }
            }
        }
    }
}
