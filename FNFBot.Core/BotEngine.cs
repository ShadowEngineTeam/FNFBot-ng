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
        private readonly Random _rnd = new Random();
        public BotSettings Settings { get; }

        private List<FNFSong.FNFNote> _notes = new List<FNFSong.FNFNote>();
        private double[] _noteJitter = Array.Empty<double>();
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

            _noteJitter = new double[_notes.Count];
            for (int i = 0; i < _notes.Count; i++)
                _noteJitter[i] = _notes[i].Length > 0 ? 0 : ComputeJitter();

            double crochet = Bpm > 0 ? 60000.0 / Bpm : 600.0;
            SectionLenMs = crochet * 4.0;

            _watch.Reset();
            _playing = false;
            _ended = false;

            _stop = false;
            _thread = new Thread(PlayLoop) { IsBackground = true, Name = "FNFBot-play" };
            _thread.Start();

            string diffInfo = string.IsNullOrEmpty(Difficulty) ? "" : $" [{Difficulty}]";
            Emit($"Loaded {SongName}{diffInfo} ({Format}) — {_notes.Count} notes to hit. Press F2 to start.");
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

                    while (hitIndex < _notes.Count && t + Settings.Offset >= _notes[hitIndex].Time + _noteJitter[hitIndex] - 22)
                    {
                        HandleNote(_notes[hitIndex], t, _noteJitter[hitIndex]);
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

        private double ComputeJitter()
        {
            if (Settings.PressRate >= 100)
                return 0;

            double maxJitter = (100 - Settings.PressRate) * 1.6;
            double j = _rnd.NextDouble() * maxJitter;
            if (_rnd.NextDouble() < 0.3)
                j = -Math.Min(j, 60);
            return j;
        }

        private void HandleNote(FNFSong.FNFNote n, double now, double jitter)
        {
            int dir = (int)n.Type % 4;
            bool shouldHold = n.Length > 0;

            if (Settings.AutoFail && !shouldHold && _rnd.Next(100) < 10)
                return;

            if (_holdTimes[dir] != 0)
            {
                _input.KeyUp(dir);
                _holdTimes[dir] = 0;
            }

            _input.KeyDown(dir);

            double pressLen = _rnd.Next(Settings.PressMinMs, Settings.PressMaxMs + 1);
            double holdExtra = _rnd.Next(Settings.HoldMinMs, Settings.HoldMaxMs + 1);
            _holdTimes[dir] = shouldHold
                ? n.Time + n.Length + holdExtra + jitter
                : now + pressLen;
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

        public void Rewind()
        {
            _playing = false;
            _watch.Reset();
            if (_chartPath != null)
                Load(_chartPath, _difficulty);
            else
                Emit("No chart loaded.");
        }

        public void PlayPause()
        {
            _playing = !_playing;
            Emit("Playing: " + _playing);
            if (_playing && _ended && _chartPath != null)
                Load(_chartPath, _difficulty); // replay
        }

        public void FastForward()
        {
            Emit("Fast-forward — skipping to end.");
            _playing = false;
            _ended = true;
            Completed?.Invoke();
        }

        public void CloseChart()
        {
            _playing = false;
            _stop = true;
            _thread?.Join(200);
            _stop = false;
            _chartPath = null;
            _difficulty = null;
            _notes = new List<FNFSong.FNFNote>();
            _watch.Reset();
            _ended = false;
            ReleaseAllHolds();
            Emit("Chart closed.");
            Loaded?.Invoke();
        }

        public void HandleHotkey(BotHotkey hk)
        {
            switch (hk)
            {
                case BotHotkey.Rewind: Rewind(); break;
                case BotHotkey.PlayPause: PlayPause(); break;
                case BotHotkey.FastForward: FastForward(); break;
                case BotHotkey.CloseChart: CloseChart(); break;
                default: break;
            }
        }

        public void ApplySettings()
        {
            Settings.Save();
            for (int i = 0; i < _noteJitter.Length && i < _notes.Count; i++)
                _noteJitter[i] = _notes[i].Length > 0 ? 0 : ComputeJitter();
            SettingsChanged?.Invoke();
        }

        private void AfterSettingChange(string msg)
        {
            Emit(msg);
            ApplySettings();
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
