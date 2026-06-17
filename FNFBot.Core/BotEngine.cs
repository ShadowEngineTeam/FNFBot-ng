using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FNFBot.Core.Input;
using FridayNightFunkin;

namespace FNFBot.Core
{
    /// <summary>
    /// The platform-independent bot: parses a chart, then on F1 plays it by injecting key
    /// presses in time with its own stopwatch. UI-agnostic — it reports via events and reads
    /// nothing from any windowing toolkit.
    /// </summary>
    public sealed class BotEngine : IDisposable
    {
        private readonly IInputBackend _input;
        private readonly IHotkeyListener _hotkeys;
        public BotSettings Settings { get; }

        private List<FNFSong.FNFNote> _notes = new List<FNFSong.FNFNote>();
        private readonly double[] _holdTimes = new double[4];
        private readonly Stopwatch _watch = new Stopwatch();

        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _playing;
        private volatile bool _ended;

        private string _chartPath;
        private string _difficulty;

        public string SongName { get; private set; } = "";
        public string Format { get; private set; } = "";
        public string Difficulty { get; private set; } = "";
        public double Bpm { get; private set; }
        public double Speed { get; private set; } = 1;
        public double SectionLenMs { get; private set; } = 1;

        public IReadOnlyList<FNFSong.FNFNote> Notes => _notes;
        public double CurrentTimeMs => _watch.Elapsed.TotalMilliseconds;
        public bool IsPlaying => _playing;

        public event Action<string> Log;
        public event Action Loaded;
        public event Action Completed;
        public event Action SettingsChanged;

        public BotEngine() : this(null, null) { }

        /// <summary>
        /// Optional injection point for custom input / hotkey backends (testing, or a future
        /// platform head). Null falls back to the OS default from <see cref="PlatformInput"/>.
        /// </summary>
        public BotEngine(IInputBackend input, IHotkeyListener hotkeys)
        {
            Settings = BotSettings.Load();
            _input = input ?? PlatformInput.CreateBackend();
            _hotkeys = hotkeys ?? PlatformInput.CreateHotkeyListener();
            _hotkeys.Pressed += HandleHotkey;
        }

        /// <summary>
        /// Begins listening for hotkeys. Call after subscribing to <see cref="Log"/> so the
        /// listener's startup diagnostics are visible, and report any input-backend failure.
        /// </summary>
        public void Start()
        {
            _hotkeys.Start();
            if (PlatformInput.LastBackendError != null)
                Emit("Input unavailable: " + PlatformInput.LastBackendError);
        }

        private void Emit(string msg) => Log?.Invoke(msg);

        public void Load(string chartPath, string difficulty = null)
        {
            Emit("attempting to load " + chartPath);
            if (!File.Exists(chartPath))
            {
                Emit("Path doesn't exist");
                return;
            }

            _stop = true;
            _thread?.Join(200);

            _chartPath = chartPath;
            _difficulty = difficulty;

            FNFSong song;
            try
            {
                song = new FNFSong(chartPath, difficulty);
            }
            catch (Exception e)
            {
                Emit("Failed to parse chart: " + e.Message);
                return;
            }

            SongName = song.SongName;
            Format = song.Format;
            Difficulty = song.Difficulty;
            Bpm = song.Bpm;
            Speed = song.Speed;

            var notes = new List<FNFSong.FNFNote>();
            foreach (var sec in song.Sections)
                foreach (var n in sec.Notes)
                    if ((int)n.Type < 4) // normalized: player notes are 0-3
                        notes.Add(n);
            notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            _notes = notes;

            double crochet = Bpm > 0 ? 60000.0 / Bpm : 600.0;
            SectionLenMs = crochet * 4.0;

            _watch.Reset();
            _playing = false;
            _ended = false;

            _stop = false;
            _thread = new Thread(PlayLoop) { IsBackground = true, Name = "FNFBot-play" };
            _thread.Start();

            string diffInfo = string.IsNullOrEmpty(Difficulty) ? "" : $" [{Difficulty}]";
            Emit($"Loaded {SongName}{diffInfo} ({Format}) — {_notes.Count} notes to hit. Press F1 to start.");
            Emit($"Timing — offset {Settings.Offset}ms (F2/F3), press {Settings.PressMs}ms (F4/F5), overhold {Settings.HoldReleaseMs}ms (F6/F7).");
            Loaded?.Invoke();
        }

        private void PlayLoop()
        {
            int hitIndex = 0;
            bool completedLogged = false;

            void Reset()
            {
                hitIndex = 0;
                completedLogged = false;
                ReleaseAllHolds();
            }

            Reset();
            Emit("Play thread ready.");

            try
            {
                while (!_stop)
                {
                    if (!_playing)
                    {
                        if (_watch.IsRunning)
                            _watch.Reset();
                        ReleaseAllHolds();
                        Thread.Sleep(40);
                        continue;
                    }

                    if (!_watch.IsRunning)
                    {
                        Reset();
                        _watch.Start();
                    }

                    double t = _watch.Elapsed.TotalMilliseconds;

                    while (hitIndex < _notes.Count && t + Settings.Offset >= _notes[hitIndex].Time - 22)
                    {
                        HandleNote(_notes[hitIndex], t);
                        hitIndex++;
                    }

                    ReleaseExpiredHolds(t);

                    if (hitIndex >= _notes.Count && !completedLogged && !AnyHoldActive())
                    {
                        completedLogged = true;
                        ReleaseAllHolds();
                        _playing = false;
                        _ended = true;
                        Emit("Completed!");
                        Completed?.Invoke();
                    }

                    Thread.Sleep(1);
                }
            }
            catch (Exception e)
            {
                Emit("Exception on play thread\n" + e);
            }
            finally
            {
                ReleaseAllHolds();
            }
        }

        private void HandleNote(FNFSong.FNFNote n, double now)
        {
            int dir = (int)n.Type % 4;
            bool shouldHold = n.Length > 0;

            if (_holdTimes[dir] != 0)
            {
                _input.KeyUp(dir);
                _holdTimes[dir] = 0;
            }

            _input.KeyDown(dir);
            _holdTimes[dir] = shouldHold
                ? n.Time + n.Length + Settings.HoldReleaseMs
                : now + Settings.PressMs;
        }

        private void ReleaseExpiredHolds(double t)
        {
            for (int dir = 0; dir < 4; dir++)
                if (_holdTimes[dir] != 0 && t > _holdTimes[dir])
                {
                    _holdTimes[dir] = 0;
                    _input.KeyUp(dir);
                }
        }

        private void ReleaseAllHolds()
        {
            for (int dir = 0; dir < 4; dir++)
                if (_holdTimes[dir] != 0)
                {
                    _holdTimes[dir] = 0;
                    _input.KeyUp(dir);
                }
        }

        private bool AnyHoldActive()
        {
            for (int dir = 0; dir < 4; dir++)
                if (_holdTimes[dir] != 0)
                    return true;
            return false;
        }

        /// <summary>Start/stop playback — the same action as pressing F1 on desktop.</summary>
        public void TogglePlay()
        {
            _playing = !_playing;
            Emit("Playing: " + _playing);
            if (_playing && _ended && _chartPath != null)
                Load(_chartPath, _difficulty); // replay
        }

        public void Play() { if (!_playing) TogglePlay(); }
        public void Stop() { _playing = false; }

        public void HandleHotkey(BotHotkey hk)
        {
            switch (hk)
            {
                case BotHotkey.TogglePlay:
                    TogglePlay();
                    break;
                case BotHotkey.OffsetUp: Settings.Offset++; AfterSettingChange($"Offset: {Settings.Offset}"); break;
                case BotHotkey.OffsetDown: Settings.Offset--; AfterSettingChange($"Offset: {Settings.Offset}"); break;
                case BotHotkey.PressUp: Settings.PressMs += 5; AfterSettingChange($"Press hold: {Settings.PressMs}ms"); break;
                case BotHotkey.PressDown: Settings.PressMs = Math.Max(1, Settings.PressMs - 5); AfterSettingChange($"Press hold: {Settings.PressMs}ms"); break;
                case BotHotkey.HoldUp: Settings.HoldReleaseMs += 5; AfterSettingChange($"Sustain overhold: {Settings.HoldReleaseMs}ms"); break;
                case BotHotkey.HoldDown: Settings.HoldReleaseMs -= 5; AfterSettingChange($"Sustain overhold: {Settings.HoldReleaseMs}ms"); break;
            }
        }

        private void AfterSettingChange(string msg)
        {
            Emit(msg);
            Settings.Save();
            SettingsChanged?.Invoke();
        }

        public void Dispose()
        {
            _stop = true;
            _hotkeys.Pressed -= HandleHotkey;
            _hotkeys.Stop();
            _thread?.Join(200);
            ReleaseAllHolds();
        }
    }
}
