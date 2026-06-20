using System;
using System.IO;
using System.Text.Json;

namespace FridayNightFunkin
{
    public static class PsychParser
    {
        /// <summary>Read keyCount from a song JSON object. Supports keyCount, mania (legacy Psych), and defaults to 4.</summary>
        public static int ReadKeyCount(JsonElement songObj)
        {
            if (songObj.TryGetProperty("keyCount", out var kc) && kc.ValueKind == JsonValueKind.Number)
            {
                int v = kc.GetInt32();
                if (v > 0) return v;
            }
            if (songObj.TryGetProperty("playerKeyCount", out var pkc) && pkc.ValueKind == JsonValueKind.Number)
            {
                int v = pkc.GetInt32();
                if (v > 0) return v;
            }
            if (songObj.TryGetProperty("mania", out var mania) && mania.ValueKind == JsonValueKind.Number)
            {
                // Psych v1_convert stores mania as (keyCount - 1); old format uses:
                // mania 0=4K, 1=6K, 2=7K, 3=9K, 4=5K
                int m = mania.GetInt32();
                // Detect v1_convert: mania matches the PsychOnline pattern (mania = keyCount - 1)
                // If format starts with "psych_v1", mania = keyCount - 1
                // Otherwise use legacy mapping
                return m switch
                {
                    0 => 4,
                    1 => 6,
                    2 => 7,
                    3 => 9,
                    4 => 5,
                    _ => m // direct value as fallback
                };
            }
            return 4;
        }

        public static bool IsPsychRoot(JsonElement root)
        {
            return root.TryGetProperty("song", out _);
        }

        public static void Parse(FNFSong song, JsonElement root)
        {
            JsonElement songObj;
            bool isV1;

            if (root.TryGetProperty("song", out var songProp))
            {
                if (songProp.ValueKind == JsonValueKind.String)
                {
                    isV1 = true;
                    songObj = root;
                    song.SongName = songProp.GetString() ?? "Unknown";
                }
                else
                {
                    isV1 = false;
                    songObj = songProp;
                    if (songObj.TryGetProperty("song", out var inner) && inner.ValueKind == JsonValueKind.String)
                        song.SongName = inner.GetString() ?? "Unknown";
                }
            }
            else
            {
                throw new InvalidDataException("No song data found in chart (missing \"song\").");
            }

            if (songObj.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.String)
            {
                song.Format = fmt.GetString();
                if (song.Format != null && song.Format.StartsWith("psych_v1"))
                    isV1 = true;
                else if (song.Format == "psych_legacy")
                    song.Format = "legacy";
            }
            else
            {
                song.Format = isV1 ? "psych_v1" : "legacy";
            }

            song.Bpm = ChartUtils.GetDouble(songObj, "bpm", 100);
            song.Speed = ChartUtils.GetDouble(songObj, "speed", 1);
            song.KeyCount = ReadKeyCount(songObj);

            if (!songObj.TryGetProperty("notes", out var sections) || sections.ValueKind != JsonValueKind.Array)
                return;

            int kc = song.KeyCount;

            foreach (var sec in sections.EnumerateArray())
            {
                bool mustHit = true;
                if (sec.TryGetProperty("mustHitSection", out var mh) &&
                    (mh.ValueKind == JsonValueKind.True || mh.ValueKind == JsonValueKind.False))
                    mustHit = mh.GetBoolean();

                var section = new FNFSong.FNFSection();

                if (sec.TryGetProperty("sectionNotes", out var notes) && notes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var n in notes.EnumerateArray())
                    {
                        if (n.ValueKind != JsonValueKind.Array || n.GetArrayLength() < 2)
                            continue;

                        double time = ChartUtils.ElementToDouble(n[0]);
                        int lane = (int)ChartUtils.ElementToDouble(n[1]);

                        if (lane < 0)
                            continue;

                        double length = n.GetArrayLength() > 2 ? ChartUtils.ElementToDouble(n[2]) : 0;

                        bool playerNote = isV1 ? lane < kc : (mustHit ? lane < kc : lane >= kc);

                        section.Notes.Add(new FNFSong.FNFNote
                        {
                            Time = time,
                            Length = length,
                            Lane = lane % kc,
                            IsPlayer = playerNote
                        });
                    }
                }

                song.Sections.Add(section);
            }
        }
    }
}
