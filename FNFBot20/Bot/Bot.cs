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
        private static double sectionLenMs = 1;

        public static double SectionLenMs => sectionLenMs;

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

            // Reuse one RenderBot so we don't attach a fresh Paint handler to the play field
            // every time a song loads (which stacks handlers and causes flicker).
            if (rBot == null)
                rBot = new RenderBot();
            rBot.SetScrollSpeed((float)mBot.song.Speed);

            allNotes = new List<FNFSong.FNFNote>();
            foreach (FNFSong.FNFSection sec in mBot.song.Sections)
                allNotes.AddRange(mBot.GetHitNotes(sec));
            allNotes.Sort((a, b) => a.Time.CompareTo(b.Time));

            double crochet = mBot.song.Bpm > 0 ? 60.0 / mBot.song.Bpm * 1000.0 : 600.0;
            sectionLenMs = crochet * 4.0; // 4 beats per section

            // Hand the whole note list to the renderer once; it scrolls on its own 60 fps
            // timer, so the play loop below never has to touch rendering.
            rBot.SetNotes(allNotes);

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
            bool completedLogged = false;

            void ResetPlayback()
            {
                hitIndex = 0;
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

                    // Don't finish until every note is hit AND any final hold has run its
                    // full length — otherwise the last sustain gets released the instant it's
                    // pressed and registers as a miss.
                    if (hitIndex >= allNotes.Count && !completedLogged && !AnyHoldActive())
                    {
                        completedLogged = true;
                        ReleaseAllHolds();
                        Playing = false;
                        ended = true;
                        Form1.WriteToConsole("Completed!");
                    }

                    // 1ms cadence (timer resolution is raised in Program.Main), so notes are
                    // checked ~1000x/sec and pressed within ~1ms of their target time.
                    Thread.Sleep(1);
                }
            }
            catch (Exception e)
            {
                Form1.WriteToConsole("Exception on Play Thread\n" + e);
            }
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

        private bool AnyHoldActive()
        {
            for (int dir = 0; dir < 4; dir++)
                if (holdTimes[dir] != 0)
                    return true;
            return false;
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
