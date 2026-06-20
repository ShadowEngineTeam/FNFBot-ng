using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FNFBot.Core.Input;
using FNFBot.Core.Memory;
using FridayNightFunkin;

namespace FNFBot.Core
{
    public enum EngineType
    {
        Auto,
        Psych,
        Codename,
        Kade,
        NightmareVision,
        Troll,
        VSlice,
        CDev,
        Generic
    }

    /// <summary>
    /// The platform-independent bot: parses a chart, then on F2 plays it by injecting key
    /// presses in time with its own stopwatch. Reports via events and reads nothing from any
    /// windowing toolkit.
    /// </summary>
    public sealed class BotEngine : IDisposable
    {
        private readonly IInputBackend _input;
        private readonly IHotkeyListener _hotkeys;
        private readonly Random _rnd = new Random();
        public BotSettings Settings { get; }

        private List<FNFSong.FNFNote> _notes = new List<FNFSong.FNFNote>();
        private double[] _noteJitter = Array.Empty<double>();
        private double[] _holdTimes = Array.Empty<double>();
        private readonly Stopwatch _watch = new Stopwatch();

        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _playing;
        private volatile bool _ended;

        private string _chartPath;
        private string _difficulty;

        // --- memory attach / Conductor following ---------------------------------
        private const double CountdownDeepMs = -800;  // a real "3-2-1" dips below this
        private const double CountdownNearMs = -350;  // ...then climbs back up to here
        // Backward jump within ReentryGuardMs of 0 means song ended / exited to menu. Disarms
        // so Codename's freeplay autoplay can't fire notes.
        private const double ReentryGuardMs = 5000;

        private ISongClock _mem;
        private Thread _memWatch;
        private volatile bool _shutdown;
        private volatile int _attachPid;       // 0 = detached
        private volatile string _attachName;
        private int _lastAttachPid;
        private EngineType _engineType = EngineType.Auto;

        private bool _memArmed;        // countdown confirmed -> bot may press
        private bool _cdSawDeep;       // saw the deep negative dip of a real countdown
        private bool _memWasRunning;   // running last memory frame (for resync on (re)start)
        private double _memLastT;      // last memory time (for seek/jump detection)

        public string SongName { get; private set; } = "";
        public string Format { get; private set; } = "";
        public string Difficulty { get; private set; } = "";
        public double Bpm { get; private set; }
        public double Speed { get; private set; } = 1;
        public double SectionLenMs { get; private set; } = 1;
        public bool OpponentMode { get; set; }

        public IReadOnlyList<FNFSong.FNFNote> Notes => _notes;

        /// <summary>
        /// Current song time in ms. Sourced from the attached game's Conductor when
        /// locked on, otherwise from the manual (F2) stopwatch.
        /// </summary>
        public double CurrentTimeMs => MemActive ? _mem.InterpolatedMs : _watch.Elapsed.TotalMilliseconds;
        public bool IsPlaying => _playing;

        /// <summary>True when attached to a process AND locked onto its songPosition.</summary>
        public bool MemActive => _attachPid != 0 && _mem != null && _mem.Located;

        /// <summary>True when a process is selected, regardless of lock state.</summary>
        public bool MemAttached => _attachPid != 0;

        /// <summary>Display name of the attached process, or null.</summary>
        public string AttachedProcess => _attachPid != 0 ? _attachName : null;

        public event Action<string> Log;
        public event Action Loaded;
        public event Action Completed;
        public event Action SettingsChanged;

        /// <summary>Raised when attach/lock state changes, so the UI can refresh its label.</summary>
        public event Action MemoryStatusChanged;

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
            _mem = new ModuleStaticSongClock(Emit);
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

            _memWatch = new Thread(MemoryWatch) { IsBackground = true, Name = "FNFBot-mem" };
            _memWatch.Start();
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

            int keyCount = song.KeyCount;
            if (keyCount < 1) keyCount = 4;

            // Ensure key names match the chart's key count.
            if (Settings.KeyNames == null || Settings.KeyNames.Length != keyCount)
                Settings.KeyNames = Input.KeyMap.DefaultNames(keyCount);
            ApplyKeyMapping();

            var notes = new List<FNFSong.FNFNote>();
            foreach (var sec in song.Sections)
                foreach (var n in sec.Notes)
                {
                    if (OpponentMode ? !n.IsPlayer : n.IsPlayer)
                        notes.Add(n);
                }
            notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            _notes = notes;
            _holdTimes = new double[keyCount];

            _noteJitter = new double[_notes.Count];
            int j = 0;
            while (j < _notes.Count)
            {
                if (_notes[j].Length > 0)
                {
                    _noteJitter[j] = 0;
                    j++;
                    continue;
                }
                double jitter = ComputeJitter();
                double t0 = _notes[j].Time;
                int end = j;
                while (end < _notes.Count && _notes[end].Time - t0 < 5)
                {
                    _noteJitter[end] = _notes[end].Length > 0 ? 0 : jitter;
                    end++;
                }
                j = end;
            }

            double crochet = Bpm > 0 ? 60000.0 / Bpm : 600.0;
            SectionLenMs = crochet * 4.0;

            _watch.Reset();
            _playing = false;
            _ended = false;

            // Fresh chart: reset arm state for the next countdown.
            _memArmed = false;
            _cdSawDeep = false;
            _memWasRunning = false;
            _memLastT = 0;

            _stop = false;
            _thread = new Thread(PlayLoop) { IsBackground = true, Name = "FNFBot-play" };
            _thread.Start();

            string diffInfo = string.IsNullOrEmpty(Difficulty) ? "" : $" [{Difficulty}]";
            Emit($"Loaded {SongName}{diffInfo} ({Format}), {_notes.Count} notes to hit. Press F2 to start.");
            Loaded?.Invoke();
        }

        private void PlayLoop()
        {
            int hitIndex = 0;
            bool completedLogged = false;

            ReleaseAllHolds();
            Emit("Play thread ready.");

            try
            {
                while (!_stop)
                {
                    if (MemActive)
                        MemoryStep(ref hitIndex);
                    else
                        ManualStep(ref hitIndex, ref completedLogged);
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

        /// <summary>Manual path: F2 starts a stopwatch and we press to it.</summary>
        private void ManualStep(ref int hitIndex, ref bool completedLogged)
        {
            if (!_playing)
            {
                if (_watch.IsRunning)
                    _watch.Reset();
                ReleaseAllHolds();
                Thread.Sleep(40);
                return;
            }

            if (!_watch.IsRunning)
            {
                hitIndex = 0;
                completedLogged = false;
                ReleaseAllHolds();
                _watch.Start();
            }

            double t = _watch.Elapsed.TotalMilliseconds;
            FireDueNotes(t, ref hitIndex);
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

        /// <summary>
        /// The attached path: the clock comes from the game's <c>Conductor.songPosition</c>.
        /// The bot arms only on the negative countdown, follows pauses (frozen clock) and
        /// resumes, and re-syncs silently on seeks instead of machine-gunning skipped notes.
        /// </summary>
        private void MemoryStep(ref int hitIndex)
        {
            if (_watch.IsRunning)
                _watch.Reset(); // memory owns the clock now

            double pos = _mem.InterpolatedMs;
            bool advancing = _mem.Advancing;

            // Backward jump >1 beat means exit to menu/freeplay. Disarm so autoplay
            // previews can't fire notes. Don't disarm if landing in negative range:
            // that's a restart, not an exit.
            if (_memArmed && pos - _memLastT < -250 && pos >= 0 && pos < ReentryGuardMs)
            {
                Disarm("song ended or exited gameplay");
                _memLastT = pos;
                Thread.Sleep(4);
                return;
            }

            UpdateArm(pos, ref hitIndex);

            bool running = _memArmed && advancing;
            if (!running)
            {
                if (_memWasRunning)
                    ReleaseAllHolds(); // paused or stalled: let go of every key
                _playing = false;
                _memWasRunning = false;
                _memLastT = pos;
                Thread.Sleep(4);
                return;
            }

            if (!_memWasRunning)
            {
                // (Re)entering play: armed countdown, or resume mid-song. Jump silently
                // to the live position and re-grab any sustain we're already inside.
                SeekTo(pos, ref hitIndex);
                _memWasRunning = true;
                _playing = true;
                _memLastT = pos;
            }

            double t = pos;
            double dt = t - _memLastT;

            if (dt > 250 || dt < -250)
                SeekTo(t, ref hitIndex); // jump within the song (skip/seek): resync, don't burst
            else
                FireDueNotes(t, ref hitIndex);

            ReleaseExpiredHolds(t);

            if (hitIndex >= _notes.Count && !AnyHoldActive())
            {
                // Chart body done; disarm so the outro (same global songPosition, still
                // climbing) can't press and the next song's countdown re-arms cleanly.
                Disarm("chart complete");
                Completed?.Invoke();
            }

            _memLastT = t;
            Thread.Sleep(1);
        }

        private void Disarm(string why)
        {
            bool was = _memArmed;
            _memArmed = false;
            _cdSawDeep = false;
            _memWasRunning = false;
            _playing = false;
            ReleaseAllHolds();
            if (was)
                Emit($"Disarmed: {why}, waiting for the next countdown.");
        }

        /// <summary>
        /// Confirmed-countdown arming: arm only after songPosition dips below
        /// <see cref="CountdownDeepMs"/> and then climbs, staying negative, into
        /// <see cref="CountdownNearMs"/>, the shape of a real "3-2-1" ramp. Any positive
        /// reading resets the tracker, so a menu dip-then-jump can't fake it.
        /// </summary>
        private void UpdateArm(double pos, ref int hitIndex)
        {
            if (_memArmed)
                return;

            if (pos >= 0)
            {
                _cdSawDeep = false;
                return;
            }
            if (pos <= CountdownDeepMs)
                _cdSawDeep = true;

            if (_cdSawDeep && pos >= CountdownNearMs)
            {
                _memArmed = true;
                _memWasRunning = false; // force a clean seek on the first play frame
                hitIndex = 0;
                ReleaseAllHolds();
                Emit($"Countdown confirmed ({pos:0}ms), auto-playing.");
            }
        }

        private void FireDueNotes(double t, ref int hitIndex)
        {
            while (hitIndex < _notes.Count && t + Settings.Offset >= _notes[hitIndex].Time + _noteJitter[hitIndex] - 22)
            {
                HandleNote(_notes[hitIndex], t, _noteJitter[hitIndex]);
                hitIndex++;
            }
        }

        /// <summary>
        /// Move the play cursor to <paramref name="t"/> without pressing the notes we
        /// skip over, then re-establish any sustains whose body covers <paramref name="t"/>.
        /// Used on seeks, resumes, and the armed countdown.
        /// </summary>
        private void SeekTo(double t, ref int hitIndex)
        {
            ReleaseAllHolds();
            int i = 0;
            while (i < _notes.Count && _notes[i].Time + _noteJitter[i] <= t + Settings.Offset)
                i++;
            hitIndex = i;
            ResyncHolds(t);
        }

        private void ResyncHolds(double t)
        {
            for (int idx = 0; idx < _notes.Count; idx++)
            {
                var n = _notes[idx];
                if (n.Time > t)
                    break; // sorted: nothing further can already cover t
                if (n.Length <= 0 || n.Time + n.Length <= t)
                    continue;
                int dir = n.Lane;
                if (_holdTimes[dir] != 0)
                    _input.KeyUp(dir);
                _input.KeyDown(dir);
                _holdTimes[dir] = n.Time + n.Length + Settings.HoldMinMs;
            }
        }

        // ----- memory attach -----------------------------------------------------

        /// <summary>Windowed processes the user can attach to.</summary>
        public static List<ProcessPick> ListProcesses() => ProcessMemory.ListProcesses();

        /// <summary>Attach to a process; the watcher thread opens it and starts scanning.</summary>
        public void AttachTo(int pid, string name, EngineType engineType = EngineType.Auto)
        {
            if (pid <= 0)
                return;
            if (_attachPid != 0 && pid != _attachPid)
            {
                // Switching to a different process while still attached: mark for
                // detachment so MemoryWatch picks it up before opening the new one.
                _attachPid = 0;
                _lastAttachPid = 0;
                _memArmed = false;
                _cdSawDeep = false;
                _memWasRunning = false;
                ReleaseAllHolds();
            }
            _attachName = name;
            _engineType = engineType;
            _attachPid = pid;
            Emit($"Attaching to {name} (pid {pid})... start a song in-game and the bot follows its countdown.");
            MemoryStatusChanged?.Invoke();
        }

        public void DetachMemory()
        {
            if (_attachPid == 0)
                return;
            _attachPid = 0;
            _lastAttachPid = 0;
            _memArmed = false;
            _cdSawDeep = false;
            _memWasRunning = false;
            ReleaseAllHolds();
            Emit("Detached, back to manual (F2).");
            MemoryStatusChanged?.Invoke();
        }

        private ISongClock CreateClock()
        {
            switch (_engineType)
            {
                case EngineType.Psych:
                    Emit("Using Psych/Shadow Engine clock.");
                    return new PsychSongClock(Emit);
                case EngineType.Codename:
                    Emit("Using Codename Engine clock.");
                    return new CodenameSongClock(Emit);
                case EngineType.Kade:
                    Emit("Using Kade Engine clock.");
                    return new KadeSongClock(Emit);
                case EngineType.NightmareVision:
                    Emit("Using NightmareVision clock.");
                    return new NightmareVisionSongClock(Emit);
                case EngineType.Troll:
                    Emit("Using Troll Engine clock.");
                    return new TrollSongClock(Emit);
                case EngineType.VSlice:
                    Emit("Using Funkin V-Slice clock (heap pointer-chain).");
                    return new VSliceSongClock(Emit);
                case EngineType.CDev:
                    Emit("Using CDev Engine clock.");
                    return new CDevSongClock(Emit);
                case EngineType.Generic:
                    Emit("Using generic module-static clock.");
                    return new ModuleStaticSongClock(Emit);
                default:
                    // Auto: pick by process name.
                    string name = _attachName ?? "";
                    if (CodenameSongClock.Matches(name))
                    {
                        Emit("Detected Codename Engine.");
                        return new CodenameSongClock(Emit);
                    }
                    if (KadeSongClock.Matches(name))
                    {
                        Emit("Detected Kade Engine.");
                        return new KadeSongClock(Emit);
                    }
                    if (NightmareVisionSongClock.Matches(name))
                    {
                        Emit("Detected NightmareVision.");
                        return new NightmareVisionSongClock(Emit);
                    }
                    if (TrollSongClock.Matches(name))
                    {
                        Emit("Detected Troll Engine.");
                        return new TrollSongClock(Emit);
                    }
                    if (VSliceSongClock.Matches(name))
                    {
                        Emit("Detected Funkin (V-Slice), using the heap pointer-chain clock.");
                        return new VSliceSongClock(Emit);
                    }
                    if (CDevSongClock.Matches(name))
                    {
                        Emit("Detected CDev Engine.");
                        return new CDevSongClock(Emit);
                    }
                    if (PsychSongClock.Matches(name))
                    {
                        Emit("Detected Psych/Shadow Engine.");
                        return new PsychSongClock(Emit);
                    }
                    Emit("Unrecognised engine name, using the generic module-static clock.");
                    return new ModuleStaticSongClock(Emit);
            }
        }

        private void MemoryWatch()
        {
            while (!_shutdown)
            {
                try
                {
                    int pid = _attachPid;

                    // Detect pid change: detach the old process when user picks a different one.
                    if (pid != _lastAttachPid && _mem.HasProcess)
                    {
                        _mem.Detach();
                        _lastAttachPid = pid;
                        MemoryStatusChanged?.Invoke();
                    }
                    _lastAttachPid = pid;

                    if (pid == 0)
                    {
                        if (_mem.HasProcess)
                        {
                            _mem.Detach();
                            MemoryStatusChanged?.Invoke();
                        }
                        Thread.Sleep(150);
                        continue;
                    }

                    if (!_mem.HasProcess)
                    {
                        var proc = ProcessMemory.OpenByPid(pid, Emit);
                        if (proc == null)
                        {
                            _attachPid = 0;
                            _lastAttachPid = 0;
                            MemoryStatusChanged?.Invoke();
                            Thread.Sleep(200);
                            continue;
                        }
                        _attachName = proc.Name;
                        _mem = CreateClock();
                        _mem.Attach(proc);
                        Emit($"Attached to {proc.Name} (pid {pid}). Scanning for Conductor.songPosition...");
                        MemoryStatusChanged?.Invoke();
                    }

                    if (!_mem.IsProcessAlive)
                    {
                        Emit($"{_attachName} exited, detached.");
                        _mem.Detach();
                        _attachPid = 0;
                        MemoryStatusChanged?.Invoke();
                        continue;
                    }

                    bool wasLocated = _mem.Located;
                    _mem.Tick();
                    if (_mem.Located != wasLocated)
                        MemoryStatusChanged?.Invoke();

                    Thread.Sleep(_mem.Located ? 3 : 120);
                }
                catch (Exception e)
                {
                    Emit("Memory watch error: " + e.Message);
                    Thread.Sleep(300);
                }
            }
        }

        private double ComputeJitter()
        {
            if (Settings.PressRate >= 100)
                return 0;

            double t = (100 - Settings.PressRate) / 100.0;
            double maxJitter = t * t * t * 160; // cubic: tiny at high PR
            double j = _rnd.NextDouble() * maxJitter;
            if (_rnd.NextDouble() < 0.3)
                j = -Math.Min(j, 60);
            return j;
        }

        private void HandleNote(FNFSong.FNFNote n, double now, double jitter)
        {
            int dir = n.Lane;
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
            for (int dir = 0; dir < _holdTimes.Length; dir++)
                if (_holdTimes[dir] != 0 && t > _holdTimes[dir])
                {
                    _holdTimes[dir] = 0;
                    _input.KeyUp(dir);
                }
        }

        private void ReleaseAllHolds()
        {
            for (int dir = 0; dir < _holdTimes.Length; dir++)
                if (_holdTimes[dir] != 0)
                {
                    _holdTimes[dir] = 0;
                    _input.KeyUp(dir);
                }
        }

        private bool AnyHoldActive()
        {
            for (int dir = 0; dir < _holdTimes.Length; dir++)
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
            Emit("Fast-forward, skipping to end.");
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

        /// <summary>
        /// Push the current key name mapping from settings to the input backend.
        /// </summary>
        public void ApplyKeyMapping()
        {
            _input.SetKeyCodes(KeyMap.ToPlatformCodes(Settings.KeyNames));
        }

        public void Dispose()
        {
            _stop = true;
            _shutdown = true;
            _hotkeys.Pressed -= HandleHotkey;
            _hotkeys.Stop();
            _thread?.Join(200);
            _memWatch?.Join(300);
            _mem?.Detach();
            ReleaseAllHolds();
        }
    }
}
