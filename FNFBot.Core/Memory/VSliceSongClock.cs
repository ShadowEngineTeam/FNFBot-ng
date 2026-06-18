using System;
using System.Collections.Generic;
using System.Threading;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// Finds and follows <c>Conductor.instance.songPosition</c> in Funkin V-Slice, whose
    /// Conductor is a heap singleton (<c>Conductor._instance</c>) rather than a module static.
    ///
    /// <para>A blind value-scan of a multi-GB game is slow and can't tell songPosition from
    /// preview timers, so this probes anchor-first: once, find the module statics that point
    /// at a real heap object (<c>Conductor._instance</c> is one), then each cycle read through
    /// those pointers twice ~140ms apart and keep the field advancing at ~1ms/ms. Reading
    /// through the static stays correct across GC moves and bounds the work to a few MB.</para>
    ///
    /// <para>Arming/pausing/disarming live in <c>BotEngine</c>, identical to the other engines:
    /// V-Slice's countdown dips to <c>-crochet*5</c> while playback clamps songPosition to
    /// roughly 0+, so freeplay previews never arm it.</para>
    /// </summary>
    public sealed class VSliceSongClock : ISongClock
    {
        private const double RangeMin = -12_000;
        private const double RangeMax = 1_200_000;
        private const double SlopeMin = 0.40;
        private const double SlopeMax = 2.50;

        private const int MaxFieldOffset = 512;          // songPosition's offset within the object
        private const int ProbeBytes = MaxFieldOffset + 8;
        private const int ProbeGapMs = 140;              // controlled dt between the two probe passes
        private const long ScanGapMs = 350;              // pace between scan attempts
        private const long FreshMs = 150;
        private const int MaxReadFails = 12;
        private const int MaxAnchors = 150_000;
        private const ulong MinPtr = 0x10000;

        private readonly Action<string> _log;
        private ProcessMemory _mem;

        // Locked state.
        private bool _located;
        private bool _anchored;
        private ulong _staticAddr;   // module address holding the Conductor pointer
        private int _fieldOffset;
        private ulong _directAddr;   // no-module fallback
        private double _value;
        private long _lastChangeTicks;
        private int _readFails;

        // Scan state.
        private ulong[] _anchorStatics;
        private long _lastScanTicks;
        private int _scanCycles;
        private bool _builtAnchors;
        private bool _strict = true;       // require a readable vtable when collecting anchors
        private int _probesSinceBuild;
        private byte[] _bufA;
        private byte[] _bufB;
        private readonly Dictionary<long, double> _probeA = new Dictionary<long, double>();

        public VSliceSongClock(Action<string> log) => _log = log;

        /// <summary>Recognises Funkin V-Slice by window/exe name.</summary>
        public static bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("funkin", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("codename", StringComparison.OrdinalIgnoreCase) < 0;
        }

        public bool Located => _located;
        public string AttachedName => _mem?.Name;
        public bool HasProcess => _mem != null;
        public bool IsProcessAlive => _mem != null && _mem.IsAlive;
        public double PositionMs => _value;
        public bool Advancing => _located && (Now - _lastChangeTicks) < FreshMs;

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
            ResetAll();
        }

        public void Detach()
        {
            _mem?.Dispose();
            _mem = null;
            ResetAll();
        }

        private void ResetAll()
        {
            _located = false;
            _anchored = false;
            _staticAddr = 0;
            _directAddr = 0;
            _fieldOffset = 0;
            _value = 0;
            _readFails = 0;
            _anchorStatics = null;
            _builtAnchors = false;
            _strict = true;
            _probesSinceBuild = 0;
            _scanCycles = 0;
            _lastScanTicks = 0;
            _probeA.Clear();
        }

        public void Tick()
        {
            if (_mem == null)
                return;
            if (_located)
                FollowTick();
            else
                ScanTick();
        }

        // ----- locked: follow the pointer chain ---------------------------------

        private bool ReadCurrent(out double v)
        {
            v = 0;
            if (_anchored)
            {
                if (!_mem.ReadPointer(_staticAddr, out ulong objBase) || objBase == 0)
                    return false;
                return _mem.ReadDouble(objBase + (ulong)_fieldOffset, out v);
            }
            return _mem.ReadDouble(_directAddr, out v);
        }

        private void FollowTick()
        {
            if (!ReadCurrent(out double v) || v < RangeMin || v > RangeMax)
            {
                if (++_readFails >= MaxReadFails)
                {
                    _log?.Invoke("Lost V-Slice songPosition, re-scanning.");
                    _located = false;
                    _builtAnchors = false;
                    _anchorStatics = null;
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

        // ----- unlocked: anchor-first scan --------------------------------------

        private void ScanTick()
        {
            long now = Now;
            if (now - _lastScanTicks < ScanGapMs)
                return;
            _lastScanTicks = now;

            if (!_mem.HasModule)
            {
                DirectScanFallback();
                return;
            }

            if (!_builtAnchors)
            {
                BuildAnchorStatics();
                _builtAnchors = true;
            }

            if (_anchorStatics == null || _anchorStatics.Length == 0)
            {
                _builtAnchors = false; // retry the build next cycle
                return;
            }

            AnchorProbe();
        }

        private void BuildAnchorStatics()
        {
            ulong modBase = _mem.ModuleBase, modEnd = _mem.ModuleEnd;
            _log?.Invoke($"V-Slice: scanning module 0x{modBase:X}-0x{modEnd:X} ({_mem.PointerSize * 8}-bit) for the Conductor pointer...");

            var heap = ToRangeSet(WritableOutsideModule(modBase, modEnd));
            var readable = ToRangeSet(ReadableList());

            // Strict (object's first word must be a readable vtable) keeps the probe set
            // small; if it finds nothing, fall back to "points into the heap".
            var list = CollectAnchors(heap, readable, strictVtable: _strict);
            if (_strict && list.Count == 0)
            {
                _strict = false;
                list = CollectAnchors(heap, readable, strictVtable: false);
            }

            _anchorStatics = list.ToArray();
            _probesSinceBuild = 0;
            _log?.Invoke($"V-Slice: {_anchorStatics.Length} candidate Conductor pointer(s) to probe{(_strict ? "" : " (broad)")}.");
        }

        private List<ulong> CollectAnchors(RangeSet heap, RangeSet readable, bool strictVtable)
        {
            var list = new List<ulong>();
            int ps = _mem.PointerSize;
            byte[] buf = null;

            foreach (var (addr, size) in _mem.WritableRegions(true))
            {
                int len = (int)Math.Min(size, int.MaxValue);
                if (buf == null || buf.Length < len)
                    buf = new byte[len];
                if (!_mem.Read(addr, buf, len))
                    continue;

                int limit = len - ps;
                for (int off = 0; off <= limit; off += ps)
                {
                    ulong p = ps == 8 ? BitConverter.ToUInt64(buf, off) : BitConverter.ToUInt32(buf, off);
                    if (p < MinPtr || !heap.Contains(p))
                        continue;
                    if (strictVtable)
                    {
                        if (!_mem.ReadPointer(p, out ulong vtable) || vtable < MinPtr || !readable.Contains(vtable))
                            continue;
                    }
                    list.Add(addr + (ulong)off);
                    if (list.Count >= MaxAnchors)
                        return list;
                }
            }
            return list;
        }

        private void AnchorProbe()
        {
            if (_bufA == null) _bufA = new byte[ProbeBytes];
            if (_bufB == null) _bufB = new byte[ProbeBytes];
            _probeA.Clear();

            // Pass A: read through each static, record in-range doubles.
            for (int i = 0; i < _anchorStatics.Length; i++)
            {
                if (!_mem.ReadPointer(_anchorStatics[i], out ulong objBase) || objBase == 0)
                    continue;
                if (!_mem.Read(objBase, _bufA, ProbeBytes))
                    continue;
                for (int off = 0; off <= MaxFieldOffset; off += 8)
                {
                    double v = BitConverter.ToDouble(_bufA, off);
                    if (InRange(v))
                        _probeA[((long)i << 20) | (uint)off] = v;
                }
            }

            long tA = Now;
            Thread.Sleep(ProbeGapMs);
            long dt = Now - tA;
            if (dt <= 0) dt = ProbeGapMs;

            // Pass B: re-read through each static and keep fields advancing ~1ms/ms.
            ulong bestStatic = 0; int bestOff = 0; bool bestNeg = false; double bestVal = 0;
            bool found = false; int candidates = 0; int probed = 0;

            for (int i = 0; i < _anchorStatics.Length; i++)
            {
                if (!_mem.ReadPointer(_anchorStatics[i], out ulong objBase) || objBase == 0)
                    continue;
                if (!_mem.Read(objBase, _bufB, ProbeBytes))
                    continue;
                probed++;
                for (int off = 0; off <= MaxFieldOffset; off += 8)
                {
                    if (!_probeA.TryGetValue(((long)i << 20) | (uint)off, out double before))
                        continue;
                    double v = BitConverter.ToDouble(_bufB, off);
                    if (!InRange(v))
                        continue;
                    double slope = (v - before) / dt;
                    if (slope < SlopeMin || slope > SlopeMax)
                        continue;

                    candidates++;
                    bool neg = v < 0 || before < 0;
                    // Prefer a candidate we caught going negative (a real countdown).
                    if (!found || (neg && !bestNeg))
                    {
                        found = true;
                        bestStatic = _anchorStatics[i];
                        bestOff = off;
                        bestNeg = neg;
                        bestVal = v;
                    }
                }
            }

            _scanCycles++;
            _probesSinceBuild++;

            if (found)
            {
                _located = true;
                _anchored = true;
                _staticAddr = bestStatic;
                _fieldOffset = bestOff;
                _value = bestVal;
                _lastChangeTicks = Now;
                _readFails = 0;
                _log?.Invoke($"Locked V-Slice songPosition via Conductor._instance @ 0x{bestStatic:X} +{bestOff} = {bestVal:0}ms" + (bestNeg ? " (saw countdown)." : "."));
                return;
            }

            // If the narrow (vtable-verified) set isn't catching it, broaden once.
            if (_strict && _probesSinceBuild >= 6)
            {
                _strict = false;
                _builtAnchors = false; // rebuild a broader anchor set next cycle
                _log?.Invoke("V-Slice: nothing via vtable-verified objects; broadening the scan.");
                return;
            }

            if (_scanCycles % 5 == 1)
                _log?.Invoke($"V-Slice: scanning... probed {probed} objects, 0 advancing songPosition yet (dt={dt}ms). Make sure a song or menu music is actually playing.");
        }

        // ----- no-module fallback (rare: 32-bit cross, or module unreadable) -----

        private bool _loggedFallback;

        private void DirectScanFallback()
        {
            if (!_loggedFallback)
            {
                _log?.Invoke("V-Slice: main module unreadable; falling back to a capped direct scan (slower, less precise).");
                _loggedFallback = true;
            }

            var a = BuildDirectSnapshot(out long tA);
            Thread.Sleep(ProbeGapMs);
            var b = BuildDirectSnapshot(out long tB);
            long dt = tB - tA;
            if (dt <= 0) return;

            ulong best = 0; double bestVal = 0; bool found = false, bestNeg = false;
            foreach (var kv in b)
            {
                if (!a.TryGetValue(kv.Key, out double before)) continue;
                double slope = (kv.Value - before) / dt;
                if (slope < SlopeMin || slope > SlopeMax) continue;
                bool neg = kv.Value < 0 || before < 0;
                if (!found || (neg && !bestNeg)) { found = true; best = kv.Key; bestVal = kv.Value; bestNeg = neg; }
            }

            if (found)
            {
                _located = true;
                _anchored = false;
                _directAddr = best;
                _value = bestVal;
                _lastChangeTicks = Now;
                _readFails = 0;
                _log?.Invoke($"Locked V-Slice songPosition (direct, no anchor) @ 0x{best:X} = {bestVal:0}ms.");
            }
        }

        private Dictionary<ulong, double> BuildDirectSnapshot(out long ticks)
        {
            ticks = Now;
            var dict = new Dictionary<ulong, double>();
            byte[] buf = null;
            ulong scanned = 0;
            foreach (var (addr, size) in _mem.WritableRegions(false))
            {
                if (size > 256UL * 1024 * 1024) continue;
                if (scanned > 800UL * 1024 * 1024) break;
                int len = (int)Math.Min(size, int.MaxValue);
                if (buf == null || buf.Length < len) buf = new byte[len];
                if (!_mem.Read(addr, buf, len)) continue;
                scanned += (ulong)len;
                int limit = len - 8;
                for (int off = 0; off <= limit; off += 8)
                {
                    double v = BitConverter.ToDouble(buf, off);
                    if (InRange(v)) dict[addr + (ulong)off] = v;
                    if (dict.Count > 3_000_000) return dict;
                }
            }
            return dict;
        }

        // ----- helpers ----------------------------------------------------------

        private static bool InRange(double v) =>
            v >= RangeMin && v <= RangeMax && !(v > -1e-6 && v < 1e-6) && !double.IsNaN(v) && !double.IsInfinity(v);

        private List<(ulong, ulong)> WritableOutsideModule(ulong modBase, ulong modEnd)
        {
            var list = new List<(ulong, ulong)>();
            foreach (var (addr, size) in _mem.WritableRegions(false))
                if (addr + size <= modBase || addr >= modEnd) // wholly outside the image
                    list.Add((addr, addr + size));
            return list;
        }

        private List<(ulong, ulong)> ReadableList()
        {
            var list = new List<(ulong, ulong)>();
            foreach (var (addr, size) in _mem.ReadableRegions())
                list.Add((addr, addr + size));
            return list;
        }

        private static RangeSet ToRangeSet(List<(ulong, ulong)> ranges) => new RangeSet(ranges);

        /// <summary>Sorted, non-overlapping address ranges with O(log n) containment.</summary>
        private sealed class RangeSet
        {
            private readonly ulong[] _starts;
            private readonly ulong[] _ends;

            public RangeSet(List<(ulong start, ulong end)> ranges)
            {
                ranges.Sort((x, y) => x.start.CompareTo(y.start));
                _starts = new ulong[ranges.Count];
                _ends = new ulong[ranges.Count];
                for (int i = 0; i < ranges.Count; i++)
                {
                    _starts[i] = ranges[i].start;
                    _ends[i] = ranges[i].end;
                }
            }

            public bool Contains(ulong p)
            {
                int lo = 0, hi = _starts.Length - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (p < _starts[mid]) hi = mid - 1;
                    else if (p >= _ends[mid]) lo = mid + 1;
                    else return true;
                }
                return false;
            }
        }
    }
}
