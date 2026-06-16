using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FridayNightFunkin
{
    public class FNFSong
    {
        public enum NoteType
        {
            Left = 0,
            Down = 1,
            Up = 2,
            Right = 3,
            RLeft = 4,
            RDown = 5,
            RUp = 6,
            RRight = 7
        }

        public class FNFNote
        {
            public double Time { get; set; }
            public double Length { get; set; }
            public NoteType Type { get; set; }
        }

        public class FNFSection
        {
            public List<FNFNote> Notes { get; set; } = new List<FNFNote>();
            public bool MustHitSection { get; set; } = true;
        }

        public double Bpm { get; set; }
        public string SongName { get; set; } = "Unknown";
        public double Speed { get; set; } = 1;
        public string Format { get; private set; } = "psych_legacy";
        public List<FNFSection> Sections { get; set; } = new List<FNFSection>();

        public FNFSong(string path)
        {
            string raw = File.ReadAllText(path).Trim();

            // Some charts have trailing garbage after the closing brace
            // (Psych strip it the same way before parsing).
            int lastBrace = raw.LastIndexOf('}');
            if (lastBrace >= 0 && lastBrace < raw.Length - 1)
                raw = raw.Substring(0, lastBrace + 1);

            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            Parse(doc.RootElement);
        }

        private void Parse(JsonElement root)
        {
            JsonElement songObj;
            bool isV1;

            if (root.TryGetProperty("song", out var songProp))
            {
                if (songProp.ValueKind == JsonValueKind.String)
                {
                    // psych_v1 - "song" is the name, data lives at the root.
                    isV1 = true;
                    songObj = root;
                    SongName = songProp.GetString() ?? "Unknown";
                }
                else
                {
                    // psych_legacy - everything nested under "song".
                    isV1 = false;
                    songObj = songProp;
                    if (songObj.TryGetProperty("song", out var inner) && inner.ValueKind == JsonValueKind.String)
                        SongName = inner.GetString() ?? "Unknown";
                }
            }
            else
            {
                // An events-only file (no playable notes) or unsupported layout.
                throw new InvalidDataException("No song data found in chart (missing \"song\").");
            }

            // An explicit format field wins if present.
            if (songObj.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.String)
            {
                Format = fmt.GetString();
                if (Format != null && Format.StartsWith("psych_v1"))
                    isV1 = true;
            }
            else
            {
                Format = isV1 ? "psych_v1" : "psych_legacy";
            }

            Bpm = GetDouble(songObj, "bpm", 100);
            Speed = GetDouble(songObj, "speed", 1);

            if (!songObj.TryGetProperty("notes", out var sections) || sections.ValueKind != JsonValueKind.Array)
                return;

            foreach (var sec in sections.EnumerateArray())
            {
                bool mustHit = true;
                if (sec.TryGetProperty("mustHitSection", out var mh) &&
                    (mh.ValueKind == JsonValueKind.True || mh.ValueKind == JsonValueKind.False))
                    mustHit = mh.GetBoolean();

                var section = new FNFSection { MustHitSection = true };

                if (sec.TryGetProperty("sectionNotes", out var notes) && notes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var n in notes.EnumerateArray())
                    {
                        if (n.ValueKind != JsonValueKind.Array || n.GetArrayLength() < 2)
                            continue; // malformed entry

                        double time = ElementToDouble(n[0]);
                        int lane = (int)ElementToDouble(n[1]);

                        // Negative lanes are events, not notes - skip them.
                        if (lane < 0)
                            continue;

                        double length = n.GetArrayLength() > 2 ? ElementToDouble(n[2]) : 0;
                        int direction = ((lane % 4) + 4) % 4;

                        bool playerNote = isV1 ? lane < 4 : (mustHit ? lane < 4 : lane >= 4);

                        section.Notes.Add(new FNFNote
                        {
                            Time = time,
                            Length = length,
                            Type = (NoteType)(playerNote ? direction : direction + 4)
                        });
                    }
                }

                Sections.Add(section);
            }
        }

        private static double GetDouble(JsonElement obj, string name, double fallback)
        {
            if (obj.TryGetProperty(name, out var v))
                return ElementToDouble(v, fallback);
            return fallback;
        }

        private static double ElementToDouble(JsonElement e, double fallback = 0)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Number:
                    return e.GetDouble();
                case JsonValueKind.String:
                    return double.TryParse(e.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                        ? d : fallback;
                case JsonValueKind.True:
                    return 1;
                case JsonValueKind.False:
                    return 0;
                default:
                    return fallback;
            }
        }

        public void SaveSong(string path)
        {
            var sb = new StringBuilder();
            sb.Append("{\"song\":{");
            sb.Append("\"song\":\"").Append(SongName).Append("\",");
            sb.Append("\"bpm\":").Append(Bpm.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"speed\":").Append(Speed.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"notes\":[");
            for (int s = 0; s < Sections.Count; s++)
            {
                var sec = Sections[s];
                sb.Append("{\"mustHitSection\":true,\"sectionNotes\":[");
                for (int i = 0; i < sec.Notes.Count; i++)
                {
                    var n = sec.Notes[i];
                    sb.Append('[')
                      .Append(n.Time.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append((int)n.Type).Append(',')
                      .Append(n.Length.ToString(CultureInfo.InvariantCulture))
                      .Append(']');
                    if (i < sec.Notes.Count - 1) sb.Append(',');
                }
                sb.Append("]}");
                if (s < Sections.Count - 1) sb.Append(',');
            }
            sb.Append("]}}");
            File.WriteAllText(path, sb.ToString());
        }
    }
}
