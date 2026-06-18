using System;
using System.Collections.Generic;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// Finds and follows <c>Conductor.songPosition</c> for engines that keep it as a module
    /// static (Psych, Shadow, Codename), exposing a smooth play clock.
    ///
    /// <para>songPosition is a Haxe static Float (a 64-bit double).  It lives in the native
    /// executable's .bss section, which the Linux backend resolves as a tail extension of the
    /// file-backed module range (<see cref="LinuxProcessMemory.ExtendBss"/>).  Matching by
    /// value is impossible (zeroed RAM is 0.0, and the time range matches millions of doubles),
    /// so the clock matches by <em>behaviour</em>: two snapshots of every in-range double in
    /// the module's writable pages, keeping those that advance at ~1 ms/ms.  Locking on a
    /// false positive is harmless because the bot never presses without a confirmed negative
    /// countdown — see <see cref="BotEngine.UpdateArm"/>.</para>
    ///
    /// <para>Shared base; subclasses (<see cref="PsychSongClock"/>, <see cref="CodenameSongClock"/>)
    /// only set the label and name match. V-Slice's heap singleton uses <see cref="VSliceSongClock"/>.</para>
    /// </summary>
    public class ModuleStaticSongClock : ISongClock
    {
        // Plausible songPosition window: a long countdown for a slow song reaches a few
        // thousand ms negative; a 20-minute song is ~1.2M ms. Anything outside is noise.
        private const double RangeMin = -12_000;
        private const double RangeMax = 1_200_000;

        // Two snapshot reads must be this far apart (ms) to measure a clean slope. The full
        // scan reads far more memory per pass (and a Wine/Box64-hosted game is large), so it
        // tolerates a much wider interval; the slope band still rejects junk.
        private const long MinDtMs = 40;
        private const long MaxDtMs = 600;
        private const long MaxDtFullMs = 30_000;

        // If the module-only scan hasn't locked after this many cycles, widen to a full
        // writable-memory sweep.  This mainly covers games running through Wine/Box64/FEX
        // where /proc/<pid>/exe is the loader rather than the game binary.
        private const int EscalateAfterCycles = 160; // ~19 s of module scan before widening

        // In full-scan mode, only candidates seen going through the negative countdown are
        // the real songPosition (everything else is a decoy like uptime).  If no negative
        // candidate appears within this many cycles, fall back to the best non-negative
        // candidate so the bot doesn't wait forever when attached mid-song or in freeplay.
        private const int FullScanNegCycles = 60;

        // A candidate advancing within this slope band (ms moved / ms elapsed) is a
        // clock. Wide enough to tolerate the engine's playbackRate and per-frame lerp.
        private const double SlopeMin = 0.40;
        private const double SlopeMax = 2.50;

        private const int LockHits = 4;        // confirmations needed to lock
        private const int LockHitsIfNeg = 1;   // one slope match + countdown dip is enough
        private const double NegThreshold = -250; // deep countdown, not noise
        private const long FreshMs = 150;      // value changed this recently => advancing
        private const int MaxReadFails = 12;   // dropped lock after this many failed reads

        private readonly Action<string> _log;
        private ProcessMemory _mem;

        // Locked state.
        private bool _located;
        private ulong _addr;
        private double _value;
        private long _lastChangeTicks;
        private int _readFails;

        // Scan accumulation.
        private Dictionary<ulong, double> _prevSnap;
        private long _prevSnapTicks;
        private readonly Dictionary<ulong, int> _hits = new Dictionary<ulong, int>();
        private readonly HashSet<ulong> _sawNeg = new HashSet<ulong>();
        private int _scanCycles;
        private bool _fullScan;

        public ModuleStaticSongClock(Action<string> log)
        {
            _log = log;
        }

        /// <summary>Human-readable engine name, used only in the lock log.</summary>
        protected virtual string EngineName => "FNF engine";

        public bool Located => _located;
        public ulong Address => _addr;
        public string AttachedName => _mem?.Name;
        public bool HasProcess => _mem != null;
        public bool IsProcessAlive => _mem != null && _mem.IsAlive;

        /// <summary>Last raw <c>songPosition</c> read, in ms.</summary>
        public double PositionMs => _value;

        /// <summary>True while the value is changing (gameplay running, not paused).</summary>
        public bool Advancing => _located && (Now - _lastChangeTicks) < FreshMs;

        /// <summary>
        /// Play clock in ms: the last read value, nudged by the wall-clock time since it
        /// changed (capped) so rendering and note timing stay smooth between the engine's
        /// per-frame updates. Frozen value (paused) returns as-is.
        /// </summary>
        public double InterpolatedMs
        {
            get
            {
                double v = _value;
                long el = Now - _lastChangeTicks;
                if (el < FreshMs)
                    return v + Math.Min(el, 50);
                return v;
            }
        }

        private static long Now => Environment.TickCount64;

        public void Attach(ProcessMemory mem)
        {
            _mem?.Dispose();
            _mem = mem;
            ResetScan();
            _fullScan = false;
            _located = false;
            _addr = 0;
            _value = 0;
            _readFails = 0;
        }

        public void Detach()
        {
            _mem?.Dispose();
            _mem = null;
            _located = false;
            _addr = 0;
            ResetScan();
        }

        private void ResetScan()
        {
            _prevSnap = null;
            _prevSnapTicks = 0;
            _hits.Clear();
            _sawNeg.Clear();
            _scanCycles = 0;
        }

        /// <summary>
        /// One step of work. While unlocked, samples memory and tries to lock; once
        /// locked, re-reads the address. Cheap when locked (a single 8-byte read), so the
        /// caller can poll it fast for tight following.
        /// </summary>
        public void Tick()
        {
            if (_mem == null)
                return;

            if (_located)
                FollowTick();
            else
                ScanTick();
        }

        // ----- locked: follow the address ---------------------------------------

        private void FollowTick()
        {
            if (!_mem.ReadDouble(_addr, out double v))
            {
                if (++_readFails >= MaxReadFails)
                {
                    _log?.Invoke($"Lost {EngineName} songPosition (read failures), re-scanning.");
                    _located = false;
                    _addr = 0;
                    ResetScan();
                }
                return;
            }

            _readFails = 0;
            if (v != _value)
            {
                _value = v;
                _lastChangeTicks = Now;
            }
        }

        // ----- unlocked: scan for the address -----------------------------------

        private void ScanTick()
        {
            bool moduleOnly = _mem.HasModule && !_fullScan;
            var snap = BuildSnapshot(moduleOnly);
            long now = Now;

            if (snap == null)
                return;

            if (_prevSnap != null)
            {
                long dt = now - _prevSnapTicks;
                long maxDt = moduleOnly ? MaxDtMs : MaxDtFullMs;
                if (dt >= MinDtMs && dt <= maxDt)
                {
                    foreach (var kv in snap)
                    {
                        if (!_prevSnap.TryGetValue(kv.Key, out double before))
                            continue;
                        double slope = (kv.Value - before) / dt;
                        if (slope >= SlopeMin && slope <= SlopeMax)
                        {
                            _hits.TryGetValue(kv.Key, out int h);
                            _hits[kv.Key] = h + 1;
                            if (kv.Value < NegThreshold)
                                _sawNeg.Add(kv.Key);
                        }
                    }

                    _scanCycles++;
                    TryLock();

                    // Module-only scan hasn't locked: widen to all writable memory.  Needed
                    // for games running through Wine/Box64/FEX where the loader is the module.
                    if (!_located && moduleOnly && _scanCycles >= EscalateAfterCycles)
                    {
                        _fullScan = true;
                        _log?.Invoke($"{EngineName}: module scan timed out; widening to a full-sweep scan. Start or restart a song so the countdown can be caught.");
                        ResetScan();
                        return;
                    }
                }
            }

            _prevSnap = snap;
            _prevSnapTicks = now;
        }

        private Dictionary<ulong, double> BuildSnapshot(bool moduleOnly)
        {
            var dict = new Dictionary<ulong, double>();
            byte[] buf = null;

            foreach (var (addr, size) in _mem.WritableRegions(moduleOnly))
            {
                // Skip absurdly large regions in the full-memory fallback to stay responsive.
                if (!moduleOnly && size > 256UL * 1024 * 1024)
                    continue;

                int len = (int)Math.Min(size, int.MaxValue);
                if (buf == null || buf.Length < len)
                    buf = new byte[len];

                if (!_mem.Read(addr, buf, len))
                    continue;

                int limit = len - 8;
                for (int off = 0; off <= limit; off += 8)
                {
                    double v = BitConverter.ToDouble(buf, off);
                    if (v < RangeMin || v > RangeMax)
                        continue;
                    if (v > -1e-6 && v < 1e-6)
                        continue; // zeroed RAM / unset
                    if (double.IsNaN(v) || double.IsInfinity(v))
                        continue;
                    dict[addr + (ulong)off] = v;
                }

                // Safety valve: a pathological set means our filter is too loose.
                if (dict.Count > 4_000_000)
                    break;
            }

            return dict;
        }

        private void TryLock()
        {
            ulong best = 0;
            int bestScore = int.MinValue;
            bool bestNeg = false, bestInModule = false;

            foreach (var kv in _hits)
            {
                bool neg = _sawNeg.Contains(kv.Key);

                // In full-scan / no-module mode, the only reliable signal distinguishing
                // songPosition from decoys (uptime counters, etc.) is the negative countdown.
                // Require it, but fall back to the best candidate after enough cycles.
                bool requireNeg = (_fullScan || !_mem.HasModule)
                               && _scanCycles < FullScanNegCycles;
                if (requireNeg && !neg)
                    continue;

                int need = neg ? LockHitsIfNeg : LockHits;
                if (kv.Value < need)
                    continue;

                // Strongest signal first: a value we saw go negative is a real countdown.
                // Next, prefer a value inside the executable's module (where the static lives)
                // over a stray counter in some library/heap region. Then most confirmations.
                bool inModule = _mem.HasModule && kv.Key >= _mem.ModuleBase && kv.Key < _mem.ModuleEnd;
                int score = kv.Value + (neg ? 1_000_000 : 0) + (inModule ? 1_000 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = kv.Key;
                    bestNeg = neg;
                    bestInModule = inModule;
                }
            }

            if (best == 0)
                return;

            if (!_mem.ReadDouble(best, out double v))
                return;

            _located = true;
            _addr = best;
            _value = v;
            _lastChangeTicks = Now;
            _readFails = 0;
            string where = bestInModule ? "in module" : (_mem.HasModule ? "outside module" : "no module");
            _log?.Invoke($"Locked {EngineName} songPosition @ 0x{best:X} = {v:0}ms ({where}){(bestNeg ? ", saw countdown" : "")} after {_scanCycles} scans.");
            ResetScan();
        }
    }
}
