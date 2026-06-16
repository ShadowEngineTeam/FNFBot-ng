using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FridayNightFunkin;

namespace FNFBot20
{
    public partial class Form1 : Form
    {
        public static List<Thread> currentThreads = new List<Thread>();
        public static Bot bot { get; set; }

        public static Form1 instance;

        public static RichTextBox console { get; set; }
        public static Label watchTime { get; set; }
        
        public static Label offset { get; set; }
        
        public static Panel pnlField { get; set; }

        public static bool Rendering = true;

        public static bool LightShow = false;

        public static int SectionSee = 1;
        
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImportAttribute("user32.dll")]
        public static extern bool ReleaseCapture();
        
        public Form1()
        {
            instance = this;
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
            bot = new Bot();
            console = rchConsole;
            offset = label2;
            watchTime = label1;
            pnlField = pnlPlayField;
            checkBox1.Checked = true;
        }


        public static void WriteToConsole(string text)
        {
            if (console == null)
                return;

            string line = "[" + DateTime.Now.ToShortTimeString() + "] " + text + "\n";
            try
            {
                // Must run on the UI thread — the play thread calls this too, and touching
                // RichTextBox.Text cross-thread corrupts/blanks the control (that was why the
                // log cleared at song end). AppendText also avoids reallocating the buffer.
                if (console.InvokeRequired)
                    console.BeginInvoke((MethodInvoker)(() => AppendLog(line)));
                else
                    AppendLog(line);
            }
            catch { }
        }

        private static void AppendLog(string line)
        {
            // Keep the log from growing without bound over long sessions.
            if (console.TextLength > 16000)
                console.Text = console.Text.Substring(console.TextLength - 8000);

            console.AppendText(line);
            console.SelectionStart = console.TextLength;
            console.ScrollToCaret();
        }
        
        private void button1_Click(object sender, EventArgs e)
        {
            bot.kBot.StopHooks();
            Environment.Exit(0);
        }


        private void txtbxDir_Enter(object sender, EventArgs e)
        {
            if (txtbxDir.Text == "FNF Game Directory (ex: C:/Users/user/Documents/FNF)")
                txtbxDir.Text = "";
        }

        private void txtbxDir_Leave(object sender, EventArgs e)
        {
            if (txtbxDir.Text == "")
                txtbxDir.Text = "FNF Game Directory (ex: C:/Users/user/Documents/FNF)";
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(txtbxDir.Text))
            {
                Form1.WriteToConsole("Directory does not exist");
                return;
            }

            Form1.WriteToConsole("Directory found! Retrieving data...");
            treSngSelect.Nodes.Clear();

            try
            {
                string root = txtbxDir.Text;
                int found = 0;

                foreach (string dataRoot in GetDataRoots(root))
                    found += ScanDataRoot(dataRoot, null);

                string modsDir = Path.Combine(root, "mods");
                if (Directory.Exists(modsDir))
                {
                    foreach (string mod in Directory.GetDirectories(modsDir))
                    {
                        string modData = Path.Combine(mod, "data");
                        if (Directory.Exists(modData))
                            found += ScanDataRoot(modData, Path.GetFileName(mod));

                        string modSongs = Path.Combine(mod, "songs"); // Codename mods
                        if (Directory.Exists(modSongs))
                            found += ScanDataRoot(modSongs, Path.GetFileName(mod));
                    }
                }

                if (found == 0)
                    WriteToConsole("No charts found. Point at the engine/mod folder, an 'assets' folder, or a 'data' folder.");
                else
                    WriteToConsole($"Found {found} chart(s).");
            }
            catch (Exception ee)
            {
                WriteToConsole("Failed to retrieve data.\n" + ee);
            }
        }

        private static IEnumerable<string> GetDataRoots(string root)
        {
            string[] candidates =
            {
                Path.Combine(root, "assets", "data", "songs"),          // FNF 1.8
                Path.Combine(root, "assets", "data"),                    // base game / Psych
                Path.Combine(root, "assets", "shared", "data"),          // some forks
                Path.Combine(root, "assets", "funkin_resources", "shared", "data"), // Shadow Engine
                Path.Combine(root, "assets", "songs"),                   // Codename Engine
                Path.Combine(root, "data"),                              // pointed straight at data
                Path.Combine(root, "songs"),                             // pointed near songs
                root                                                     // pointed straight at songs
            };

            foreach (string c in candidates)
                if (Directory.Exists(c))
                    yield return c;
        }

        private static bool IsChartJson(string path)
        {
            try
            {
                using var doc = ChartUtils.LoadJson(path);
                var root = doc.RootElement;
                return PsychParser.IsPsychRoot(root)
                    || VSliceParser.IsVSliceRoot(root)
                    || CodenameParser.IsCodenameRoot(root);
            }
            catch { return false; }
        }

        private int ScanDataRoot(string dataRoot, string label)
        {
            int found = 0;
            foreach (string songDir in Directory.GetDirectories(dataRoot))
            {
                // Codename Engine: charts live in a charts/ subdirectory
                string chartsDir = Path.Combine(songDir, "charts");
                bool isCodename = Directory.Exists(chartsDir);

                string[] charts;

                if (isCodename)
                {
                    charts = Directory.GetFiles(chartsDir, "*.json")
                        .Where(f => IsChartJson(f))
                        .ToArray();
                }
                else
                {
                    charts = Directory.GetFiles(songDir, "*.json")
                        .Where(f => !Path.GetFileName(f).StartsWith("events", StringComparison.OrdinalIgnoreCase))
                        .Where(f => !Path.GetFileName(f).EndsWith("-metadata.json", StringComparison.OrdinalIgnoreCase))
                        .Where(f => IsChartJson(f))
                        .ToArray();
                }

                if (charts.Length == 0)
                    continue;

                string songName = Path.GetFileName(songDir);
                var songNode = new TreeNode(label == null ? songName : $"{songName} [{label}]");

                foreach (string chart in charts)
                {
                    string fileName = Path.GetFileName(chart);

                    // Check if this is a V-Slice chart with multiple difficulties
                    string[] difficulties = VSliceParser.ListDifficulties(chart);
                    if (difficulties.Length > 0)
                    {
                        foreach (string diff in difficulties)
                        {
                            var child = new TreeNode($"{fileName} ({diff})") { Tag = new ChartTag(chart, diff) };
                            songNode.Nodes.Add(child);
                            found++;
                        }
                    }
                    else
                    {
                        var child = new TreeNode(fileName) { Tag = chart };
                        songNode.Nodes.Add(child);
                        found++;
                    }
                }

                treSngSelect.Nodes.Add(songNode);
            }
            return found;
        }

        private void treSngSelect_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (treSngSelect.SelectedNode?.Tag == null)
                return; // a song folder, not a chart

            WriteToConsole("Selecting " + treSngSelect.SelectedNode.Text);

            Play();
        }

        public void Play()
        {
            var tag = treSngSelect.SelectedNode?.Tag;

            string chartPath = tag as string;
            if (chartPath != null)
            {
                bot.Load(chartPath);
                return;
            }

            var chartTag = tag as ChartTag;
            if (chartTag != null)
            {
                bot.Load(chartTag.Path, chartTag.Difficulty);
                return;
            }

            WriteToConsole("Select a chart (.json) to play.");
        }

        private void pnlTop_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); 
        }

        private void lblVer_MouseDown(object sender, MouseEventArgs e)
        {
             ReleaseCapture();
             SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); 
        }

        private void pnlLogo_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); 
        }

        private void label1_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); 
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            Rendering = checkBox1.Checked;
            pnlField.Controls.Clear();
        }

    }
}