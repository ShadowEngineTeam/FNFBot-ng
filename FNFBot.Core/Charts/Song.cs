using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FridayNightFunkin
{
    public class FNFSong
    {
        public class FNFNote
        {
            public double Time { get; set; }
            public double Length { get; set; }
            /// <summary>Local lane index within the strum line (0 .. KeyCount-1).</summary>
            public int Lane { get; set; }
            /// <summary>True when this note belongs to the player's strum line.</summary>
            public bool IsPlayer { get; set; }
        }

        public class FNFSection
        {
            public List<FNFNote> Notes { get; set; } = new List<FNFNote>();
        }

        public double Bpm { get; set; }
        public string SongName { get; set; } = "Unknown";
        public double Speed { get; set; } = 1;
        public string Format { get; set; } = "legacy";
        public string Difficulty { get; set; } = "";
        public int KeyCount { get; set; } = 4;
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
                JsonDocument compDoc = null;
                JsonElement? metaRoot = null;
                try
                {
                    if (companion != null)
                    {
                        compDoc = ChartUtils.LoadJson(companion);
                        metaRoot = compDoc.RootElement;
                    }
                    VSliceParser.PopulateFNFSong(this, root, metaRoot, difficulty);
                    Difficulty = difficulty ?? "normal";
                }
                finally
                {
                    compDoc?.Dispose();
                }
            }
            else if (CodenameParser.IsCodenameRoot(root))
            {
                string[] metaCandidates = CodenameParser.GetMetaCandidates(path);
                JsonDocument metaDoc = null;
                JsonElement? metaRoot = null;
                try
                {
                    foreach (string candidate in metaCandidates)
                    {
                        if (File.Exists(candidate))
                        {
                            metaDoc = ChartUtils.LoadJson(candidate);
                            metaRoot = metaDoc.RootElement;
                            break;
                        }
                    }
                    CodenameParser.PopulateFNFSong(this, root, metaRoot);
                }
                finally
                {
                    metaDoc?.Dispose();
                }
            }
            else if (CDevCdcParser.IsCdcRoot(root))
            {
                CDevCdcParser.Parse(this, root);
            }
            else
            {
                throw new InvalidDataException("No song data found in chart (missing \"song\" or V-Slice or CDev format).");
            }
        }
    }
}
