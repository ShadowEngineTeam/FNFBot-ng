using System;
using System.IO;
using System.Text.Json;

namespace FridayNightFunkin
{
    public static class PsychParser
    {
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
            }
            else
            {
                song.Format = isV1 ? "psych_v1" : "psych_legacy";
            }

            song.Bpm = ChartUtils.GetDouble(songObj, "bpm", 100);
            song.Speed = ChartUtils.GetDouble(songObj, "speed", 1);

            if (!songObj.TryGetProperty("notes", out var sections) || sections.ValueKind != JsonValueKind.Array)
                return;

            foreach (var sec in sections.EnumerateArray())
            {
                bool mustHit = true;
                if (sec.TryGetProperty("mustHitSection", out var mh) &&
                    (mh.ValueKind == JsonValueKind.True || mh.ValueKind == JsonValueKind.False))
                    mustHit = mh.GetBoolean();

                var section = new FNFSong.FNFSection { MustHitSection = true };

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
                        int direction = ((lane % 4) + 4) % 4;

                        bool playerNote = isV1 ? lane < 4 : (mustHit ? lane < 4 : lane >= 4);

                        section.Notes.Add(new FNFSong.FNFNote
                        {
                            Time = time,
                            Length = length,
                            Type = (FNFSong.NoteType)(playerNote ? direction : direction + 4)
                        });
                    }
                }

                song.Sections.Add(section);
            }
        }
    }
}
