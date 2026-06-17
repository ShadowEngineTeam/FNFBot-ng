using System;
using System.Collections.Generic;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// Finds and follows <c>Conductor.songPosition</c> for engines that keep it as a
    /// module static (Psych, Shadow, Codename), exposing a smooth play clock.
    ///
    /// <para><b>How it locks on.</b> <c>Conductor.songPosition</c> is a Haxe
    /// <c>static var ... :Float</c>, which HXCPP lays out as a 64-bit double inside the
    /// main module's writable data. We can't match it by value (zeroed RAM reads as
    /// 0.0, and the plausible time range matches millions of doubles), so we match it
    /// by <i>behaviour</i>: take two snapshots of every in-range double in the module's
    /// writable pages a fixed interval apart and keep only the ones advancing at roughly
    /// 1&#160;ms per real ms, the signature of a song clock. A handful of confirmations
    /// pins the address.</para>
    ///
    /// <para><b>Why a wrong lock is harmless.</b> The bot only starts pressing on the
    /// negative countdown ramp (songPosition climbing from a deep negative toward 0); a
    /// stray monotonic counter never goes negative, so it never arms play.</para>
    ///
    /// <para>This is the shared base; per-engine subclasses (<see cref="PsychSongClock"/>,
    /// <see cref="CodenameSongClock"/>) only customise the label and process-name match.
    /// Funkin V-Slice is different (heap singleton) and uses <see cref="VSliceSongClock"/>.</para>
    /// </summary>
    public class ModuleStaticSongClock : ISongClock
    {
        // Plausible songPosition window: a long countdown for a slow song reaches a few
        // thousand ms negative; a 20-minute song is ~1.2M ms. Anything outside is noise.
        private const double RangeMin = -12_000;
        private const double RangeMax = 1_200_000;

        // Two snapshot reads must be this far apart (ms) to measure a clean slope.
        private const long MinDtMs = 40;
        private const long MaxDtMs = 600;

        // A candidate advancing within this slope band (ms moved / ms elapsed) is a
        // clock. Wide enough to tolerate the engine's playbackRate and per-frame lerp.
        private const double SlopeMin = 0.40;
        private const double SlopeMax = 2.50;

        private const int LockHits = 4;        // confirmations needed to lock
        private const int LockHitsIfNeg = 2;   // fewer if we caught it going negative
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
            bool moduleOnly = _mem.HasModule;
            var snap = BuildSnapshot(moduleOnly);
            long now = Now;

            if (snap == null)
                return;

            if (_prevSnap != null)
            {
                long dt = now - _prevSnapTicks;
                if (dt >= MinDtMs && dt <= MaxDtMs)
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
                            if (kv.Value < 0)
                                _sawNeg.Add(kv.Key);
                        }
                    }

                    _scanCycles++;
                    TryLock();
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
            int bestScore = 0;
            bool bestNeg = false;

            foreach (var kv in _hits)
            {
                bool neg = _sawNeg.Contains(kv.Key);
                int need = neg ? LockHitsIfNeg : LockHits;
                if (kv.Value < need)
                    continue;

                // Prefer the most-confirmed candidate; tie-break toward one we saw go
                // negative (a true song clock with a real countdown).
                int score = kv.Value + (neg ? 100 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = kv.Key;
                    bestNeg = neg;
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
            _log?.Invoke($"Locked {EngineName} songPosition @ 0x{best:X} = {v:0}ms after {_scanCycles} scans" + (bestNeg ? " (saw countdown)." : "."));
            ResetScan();
        }
    }
}
