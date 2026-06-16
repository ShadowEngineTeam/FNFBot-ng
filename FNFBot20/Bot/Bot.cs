using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FridayNightFunkin;

namespace FNFBot20
{
    public class Bot
    {
        public static bool Playing = false;

        public static Stopwatch watch { get; set; }

        public string sngDir { get; set; }

        public static bool ended = false;
        public KeyBot kBot;
        public MapBot mBot;
        public RenderBot rBot;

        public double[] holdTimes = {0, 0, 0, 0};

        public List<FNFSong.FNFNote> nPlay = new List<FNFSong.FNFNote>();

        private volatile bool _stop;
        private List<FNFSong.FNFNote> allNotes = new List<FNFSong.FNFNote>();
        private double sectionLenMs = 1;

        public Thread currentPlayThread { get; set; }

        public Bot()
        {
            // Create keyhooks with KeyBot
            kBot = new KeyBot();
            kBot.InitHooks();
        }

        public void Load(string songDirectory, string difficulty = null)
        {
            Form1.WriteToConsole("attempting to load " + songDirectory);
            if (!File.Exists(songDirectory))
            {
                Form1.WriteToConsole("Path doesn't exist");
                return;
            }

            _stop = true;
            Form1.currentThreads?.Remove(currentPlayThread);

            sngDir = songDirectory;

            mBot = new MapBot(songDirectory, difficulty);

            rBot = new RenderBot((int) mBot.song.Bpm);

            allNotes = new List<FNFSong.FNFNote>();
            foreach (FNFSong.FNFSection sec in mBot.song.Sections)
                allNotes.AddRange(mBot.GetHitNotes(sec));
            allNotes.Sort((a, b) => a.Time.CompareTo(b.Time));

            double crochet = mBot.song.Bpm > 0 ? 60.0 / mBot.song.Bpm * 1000.0 : 600.0;
            sectionLenMs = crochet * 4.0; // 4 beats per section

            watch = new Stopwatch();

            currentPlayThread = new Thread(PlayThread) { IsBackground = true };
            currentPlayThread.Start();
            Form1.currentThreads?.Add(currentPlayThread);

            string diffInfo = string.IsNullOrEmpty(mBot.song.Difficulty) ? "" : $" [{mBot.song.Difficulty}]";
            Form1.WriteToConsole($"Loaded {mBot.song.SongName}{diffInfo} ({mBot.song.Format}) — {allNotes.Count} notes to hit. Press F1 to start.");
            Form1.WriteToConsole($"Timing — offset {kBot.offset}ms (F2/F3), press {kBot.PressMs}ms (F4/F5), overhold {kBot.HoldReleaseMs}ms (F6/F7).");
            Form1.offset.Text = "Offset: " + kBot.offset;
        }

        private void PlayThread()
        {
            ended = false;
            _stop = false;
            Form1.WriteToConsole("Play Thread created...");

            int hitIndex = 0;
            int lastRenderedSection = -1;
            bool completedLogged = false;

            void ResetPlayback()
            {
                hitIndex = 0;
                lastRenderedSection = -1;
                completedLogged = false;
                nPlay.Clear();
                ReleaseAllHolds();
            }

            ResetPlayback();

            try
            {
                while (true)
                {
                    if (_stop)
                    {
                        ReleaseAllHolds();
                        return;
                    }

                    if (!Playing)
                    {
                        if (watch.IsRunning)
                            watch.Reset();
                        ReleaseAllHolds();
                        Thread.Sleep(40);
                        continue;
                    }

                    if (!watch.IsRunning)
                    {
                        ResetPlayback();
                        Form1.watchTime.Text = "Time: 0";
                        watch.Start();
                    }

                    double t = watch.Elapsed.TotalMilliseconds;

                    while (hitIndex < allNotes.Count &&
                           t + kBot.offset >= allNotes[hitIndex].Time - 22)
                    {
                        HandleNote(allNotes[hitIndex], t);
                        hitIndex++;
                    }

                    ReleaseExpiredHolds(t);

                    if (Form1.Rendering)
                    {
                        int sec = SectionAtTime(t);
                        if (sec != lastRenderedSection && sec >= 0 && sec < mBot.song.Sections.Count)
                        {
                            lastRenderedSection = sec;
                            Form1.watchTime.Text = "Time: " + (t / 1000.0).ToString("0.00");
                            rBot.ListNotes(mBot.GetHitNotes(mBot.song.Sections[sec]));
                        }
                    }

                    if (hitIndex >= allNotes.Count && !completedLogged)
                    {
                        completedLogged = true;
                        ReleaseAllHolds();
                        Playing = false;
                        ended = true;
                        Form1.WriteToConsole("Completed!");
                    }

                    Thread.Sleep(2);
                }
            }
            catch (Exception e)
            {
                Form1.WriteToConsole("Exception on Play Thread\n" + e);
            }
        }

        private int SectionAtTime(double t)
        {
            if (t <= 0) return 0;
            return (int) Math.Floor(t / sectionLenMs);
        }

        private void ReleaseExpiredHolds(double t)
        {
            for (int dir = 0; dir < 4; dir++)
            {
                if (holdTimes[dir] != 0 && t > holdTimes[dir])
                {
                    holdTimes[dir] = 0;
                    kBot.KeyUp(dir);
                }
            }
        }

        private void ReleaseAllHolds()
        {
            for (int dir = 0; dir < 4; dir++)
            {
                if (holdTimes[dir] != 0)
                {
                    holdTimes[dir] = 0;
                    kBot.KeyUp(dir);
                }
            }
        }

        public void HandleNote(FNFSong.FNFNote n, double now)
        {
            int dir = (int) n.Type % 4;
            bool shouldHold = n.Length > 0;

            if (holdTimes[dir] != 0)
            {
                kBot.KeyUp(dir);
                holdTimes[dir] = 0;
            }

            kBot.KeyDown(dir);

            holdTimes[dir] = shouldHold
                ? n.Time + n.Length + kBot.HoldReleaseMs
                : now + kBot.PressMs;
        }
    }
}
