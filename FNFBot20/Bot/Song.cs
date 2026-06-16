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
        public string Format { get; set; } = "psych_legacy";
        public string Difficulty { get; set; } = "";
        public List<FNFSection> Sections { get; set; } = new List<FNFSection>();

        public FNFSong(string path) : this(path, null) { }

        public FNFSong(string path, string difficulty)
        {
            using var doc = ChartUtils.LoadJson(path);
            var root = doc.RootElement;

            if (PsychParser.IsPsychRoot(root))
            {
                PsychParser.Parse(this, root);
            }
            else if (VSliceParser.IsVSliceRoot(root))
            {
                string companion = VSliceParser.FindCompanionFile(path);
                JsonElement? metaRoot = null;
                if (companion != null)
                {
                    using var compDoc = ChartUtils.LoadJson(companion);
                    metaRoot = compDoc.RootElement;
                }
                VSliceParser.PopulateFNFSong(this, root, metaRoot, difficulty);
                Difficulty = difficulty ?? "normal";
            }
            else if (CodenameParser.IsCodenameRoot(root))
            {
                string[] metaCandidates = CodenameParser.GetMetaCandidates(path);
                JsonElement? metaRoot = null;
                foreach (string candidate in metaCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        using var metaDoc = ChartUtils.LoadJson(candidate);
                        metaRoot = metaDoc.RootElement;
                        break;
                    }
                }
                CodenameParser.PopulateFNFSong(this, root, metaRoot);
            }
            else
            {
                throw new InvalidDataException("No song data found in chart (missing \"song\" or V-Slice format).");
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
