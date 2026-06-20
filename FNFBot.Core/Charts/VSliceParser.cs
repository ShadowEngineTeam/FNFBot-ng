using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FridayNightFunkin
{
    public class VSliceNote
    {
        public double Time { get; set; }
        public int Data { get; set; }
        public double Length { get; set; }
        public string Kind { get; set; } = "";
    }

    public class VSliceTimeChange
    {
        public double Time { get; set; }
        public double Bpm { get; set; }
        public int TimeSignatureNum { get; set; } = 4;
        public int TimeSignatureDen { get; set; } = 4;
    }

    public class VSliceMetadata
    {
        public string SongName { get; set; } = "Unknown";
        public string Artist { get; set; } = "Unknown";
        public List<VSliceTimeChange> TimeChanges { get; set; } = new();
    }

    public static class VSliceParser
    {
        public static bool IsVSliceRoot(JsonElement root)
        {
            if (root.TryGetProperty("timeChanges", out _)) return true;
            if (root.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Object) return true;
            return false;
        }

        public static string FindCompanionFile(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            string companion = null;

            if (name.EndsWith("-metadata"))
                companion = name[..^"-metadata".Length] + "-chart";
            else if (name.Contains("-metadata-"))
                companion = name.Replace("-metadata-", "-chart-");
            else if (name.EndsWith("-chart"))
                companion = name[..^"-chart".Length] + "-metadata";
            else if (name.Contains("-chart-"))
                companion = name.Replace("-chart-", "-metadata-");

            if (companion != null)
            {
                string candidate = Path.Combine(dir, companion + ext);
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        public static string[] ListDifficulties(string path)
        {
            try
            {
                using var doc = ChartUtils.LoadJson(path);
                var root = doc.RootElement;

                if (root.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Object)
                {
                    var result = new List<string>();
                    foreach (var kv in notes.EnumerateObject())
                        result.Add(kv.Name);
                    return result.ToArray();
                }

                if (root.TryGetProperty("timeChanges", out _))
                {
                    string companion = FindCompanionFile(path);
                    if (companion != null)
                        return ListDifficulties(companion);
                }
            }
            catch { }

            return Array.Empty<string>();
        }

        public static void PopulateFNFSong(FNFSong song, JsonElement chartRoot, JsonElement? metaRoot, string difficulty = null)
        {
            song.Format = "vslice";

            if (metaRoot != null)
            {
                var meta = metaRoot.Value;
                song.SongName = ChartUtils.GetString(meta, "songName", "Unknown");

                if (meta.TryGetProperty("timeChanges", out var tcs) && tcs.ValueKind == JsonValueKind.Array && tcs.GetArrayLength() > 0)
                    song.Bpm = ChartUtils.GetDouble(tcs[0], "bpm", 100);
            }

            if (difficulty == null || !chartRoot.TryGetProperty("notes", out var notesObj) || !notesObj.TryGetProperty(difficulty, out _))
            {
                if (chartRoot.TryGetProperty("notes", out var fallbackNotes) && fallbackNotes.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in fallbackNotes.EnumerateObject())
                    { difficulty = kv.Name; break; }
                }
            }

            if (chartRoot.TryGetProperty("scrollSpeed", out var ss) && ss.ValueKind == JsonValueKind.Object)
            {
                if (ss.TryGetProperty(difficulty, out var speedVal))
                    song.Speed = speedVal.GetDouble();
                else
                {
                    foreach (var kv in ss.EnumerateObject())
                    { song.Speed = kv.Value.GetDouble(); break; }
                }
            }

            double crochet = song.Bpm > 0 ? 60000.0 / song.Bpm : 600.0;
            double sectionLen = crochet * 4.0;

            if (chartRoot.TryGetProperty("notes", out var notes) && notes.TryGetProperty(difficulty, out var diffNotes) && diffNotes.ValueKind == JsonValueKind.Array)
            {
                var sorted = new List<(double time, double length, int lane)>();
                int maxLane = 0;
                foreach (var n in diffNotes.EnumerateArray())
                {
                    int lane = (int)ChartUtils.GetDouble(n, "d", 0);
                    sorted.Add((ChartUtils.GetDouble(n, "t", 0), ChartUtils.GetDouble(n, "l", 0), lane));
                    if (lane > maxLane) maxLane = lane;
                }
                sorted.Sort((a, b) => a.time.CompareTo(b.time));

                // Infer key count: half of total unique lanes, or use chart metadata.
                int kc = song.KeyCount;
                if (kc <= 0 || kc == 4)
                {
                    int uniqueLanes = maxLane + 1;
                    kc = uniqueLanes > 8 ? uniqueLanes / 2 : 4;
                    if (kc < 1) kc = 4;
                }
                song.KeyCount = kc;

                if (sorted.Count > 0)
                {
                    int sectionCount = (int)Math.Ceiling(sorted[^1].time / sectionLen) + 1;
                    int idx = 0;

                    for (int s = 0; s < sectionCount; s++)
                    {
                        double start = s * sectionLen;
                        double end = (s + 1) * sectionLen;

                        var secNotes = new List<FNFSong.FNFNote>();
                        while (idx < sorted.Count && sorted[idx].time < end)
                        {
                            var (time, length, lane) = sorted[idx];
                            secNotes.Add(new FNFSong.FNFNote
                            {
                                Time = time,
                                Length = length,
                                Lane = lane % kc,
                                IsPlayer = lane < kc
                            });
                            idx++;
                        }

                        song.Sections.Add(new FNFSong.FNFSection { Notes = secNotes });
                    }
                }
            }
        }
    }
}
