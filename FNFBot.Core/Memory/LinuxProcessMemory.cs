using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace FNFBot.Core.Memory
{
    /// <summary>Linux backend: /proc/PID/maps + process_vm_readv. Needs sudo or relaxed ptrace_scope.</summary>
    public sealed class LinuxProcessMemory : ProcessMemory
    {
        private readonly string _exePath;
        private bool _disposed;

        private LinuxProcessMemory(int pid, string name, string exePath) : base(pid, name)
        {
            _exePath = exePath;
        }

        public static ProcessMemory Open(int pid, Action<string> log)
        {
            if (!Directory.Exists($"/proc/{pid}"))
            {
                log?.Invoke($"Process {pid} is no longer running.");
                return null;
            }

            string name = ReadComm(pid) ?? pid.ToString();
            string exe = null;
            try { exe = File.ResolveLinkTarget($"/proc/{pid}/exe", true)?.FullName; } catch { }

            var mem = new LinuxProcessMemory(pid, name, exe);
            mem.DetectBitness(log);

            string loaderMsg = LoaderHint(exe);
            if (loaderMsg != null)
            {
                log?.Invoke(loaderMsg);
                return null;
            }

            mem.ResolveModule(log);

            if (!mem.PermissionOk(out ulong probe))
            {
                log?.Invoke($"Permission denied reading {name}. Run the bot with sudo, or: sudo sysctl -w kernel.yama.ptrace_scope=0");
                return null;
            }

            log?.Invoke($"Attached to {name} (pid {pid}, {mem.PointerSize * 8}-bit){(mem.HasModule ? $", module 0x{mem.ModuleBase:X}-0x{mem.ModuleEnd:X}" : ", no module range (will scan all writable memory)")}.");
            return mem;
        }

        private static string ReadComm(int pid)
        {
            try { return File.ReadAllText($"/proc/{pid}/comm").Trim(); }
            catch { return null; }
        }

        private void DetectBitness(Action<string> log)
        {
            PointerSize = 8;
            try
            {
                using var fs = File.OpenRead($"/proc/{Pid}/exe");
                var h = new byte[5];
                if (fs.Read(h, 0, 5) == 5 && h[0] == 0x7F && h[1] == (byte)'E' && h[2] == (byte)'L' && h[3] == (byte)'F')
                    PointerSize = h[4] == 1 ? 4 : 8;
            }
            catch { }

            if (IntPtr.Size == 4 && PointerSize == 8)
                log?.Invoke($"{Name} is a 64-bit game but this is the 32-bit bot build, so it cannot read 64-bit memory. Use the 64-bit build, or play with manual F2.");
            if (IntPtr.Size == 4)
                PointerSize = 4;
        }

        private void ResolveModule(Action<string> log)
        {
            if (string.IsNullOrEmpty(_exePath))
                return;

            ulong min = ulong.MaxValue, max = 0;
            foreach (var (start, end, _, path) in Maps())
            {
                if (path != _exePath)
                    continue;
                if (start < min) min = start;
                if (end > max) max = end;
            }
            if (max <= min)
                return;

            // .bss is anonymous writable memory right after the exe's file-backed mappings.
            ExtendBss(ref max);

            ModuleBase = min;
            ModuleEnd = max;
        }

        /// <summary>Extends end past contiguous anonymous writable regions (.bss).</summary>
        private void ExtendBss(ref ulong end)
        {
            ulong extended = 0;
            bool grew = true;
            while (grew && extended < 512UL * 1024 * 1024)
            {
                grew = false;
                foreach (var (regionStart, regionEnd, perms, path) in Maps())
                {
                    if (regionStart == end && string.IsNullOrEmpty(path)
                        && perms.Length >= 2 && perms[1] == 'w'
                        && regionEnd - regionStart <= 256UL * 1024 * 1024)
                    {
                        extended += regionEnd - end;
                        end = regionEnd;
                        grew = true;
                        break;
                    }
                }
            }
        }

        /// <summary>Returns a message if process runs under a compatibility layer, otherwise null.</summary>
        private static string LoaderHint(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;
            string n = Path.GetFileName(exePath).ToLowerInvariant();
            if (n.Contains("wine") || n.Contains("hangover"))
                return $"The process runs through {Path.GetFileName(exePath)} (Wine). Attaching to Wine-hosted games isn't supported. Use the Windows-native version of FNFBot matching the process architecture.";
            if (n.Contains("box64"))
                return $"The process runs through Box64 (x86_64 emulation). Attaching to emulated processes isn't supported. Use the x86_64 version of FNFBot matching the process architecture.";
            if (n.Contains("box86"))
                return $"The process runs through Box86 (x86 emulation). Attaching to emulated processes isn't supported. Use the x86 (32-bit) version of FNFBot matching the process architecture.";
            if (n.Contains("qemu"))
                return $"The process runs through QEMU. Attaching to emulated processes isn't supported. Use the version of FNFBot matching the emulated process's architecture.";
            if (n.Contains("fex"))
                return $"The process runs through FEX (x86 emulation on ARM). Attaching to emulated processes isn't supported. Use the x86 version of FNFBot matching the process architecture.";
            if (n.Contains("muvm") || n.Contains("preloader"))
                return $"The process runs through a compatibility layer ({Path.GetFileName(exePath)}). Attaching to emulated processes isn't supported. Use the version of FNFBot matching the process architecture.";
            return null;
        }

        private bool PermissionOk(out ulong probeAddr)
        {
            probeAddr = 0;
            if (HasModule)
                probeAddr = ModuleBase;
            else
                foreach (var (a, _) in ReadableRegions()) { probeAddr = a; break; }
            if (probeAddr == 0)
                return false;
            var b = new byte[8];
            return Read(probeAddr, b, 8);
        }

        public override void Dispose() => _disposed = true;

        public override bool IsAlive => !_disposed && Directory.Exists($"/proc/{Pid}");

        public override unsafe bool Read(ulong address, byte[] buffer, int count)
        {
            if (_disposed || buffer == null || count <= 0 || count > buffer.Length)
                return false;
            fixed (byte* p = buffer)
            {
                var local = new IoVec { Base = (IntPtr)p, Len = (IntPtr)count };
                var remote = new IoVec { Base = (IntPtr)(long)address, Len = (IntPtr)count };
                nint n = process_vm_readv(Pid, ref local, (nuint)1, ref remote, (nuint)1, (nuint)0);
                return (long)n == count;
            }
        }

        public override IEnumerable<(ulong addr, ulong size)> WritableRegions(bool moduleOnly)
        {
            // Module-only uses the resolved range; otherwise sweep all writable regions.
            bool useRange = moduleOnly && HasModule;
            foreach (var (start, end, perms, _) in Maps())
            {
                if (perms.Length < 2 || perms[1] != 'w')
                    continue;
                if (useRange)
                {
                    ulong a = Math.Max(start, ModuleBase);
                    ulong b = Math.Min(end, ModuleEnd);
                    if (b > a)
                        yield return (a, b - a);
                }
                else if (end > start)
                {
                    yield return (start, end - start);
                }
            }
        }

        public override IEnumerable<(ulong addr, ulong size)> ReadableRegions()
        {
            foreach (var (start, end, perms, _) in Maps())
                if (perms.Length >= 1 && perms[0] == 'r' && end > start)
                    yield return (start, end - start);
        }

        /// <summary>Yields (start, end, perms, pathname) from /proc/PID/maps.</summary>
        private IEnumerable<(ulong start, ulong end, string perms, string path)> Maps()
        {
            string file = $"/proc/{Pid}/maps";
            IEnumerator<string> lines;
            try { lines = File.ReadLines(file).GetEnumerator(); }
            catch { yield break; }

            using (lines)
            {
                while (true)
                {
                    string line;
                    try { if (!lines.MoveNext()) break; line = lines.Current; }
                    catch { break; }

                    if (TryParseMapsLine(line, out ulong start, out ulong end, out string perms, out string path))
                        yield return (start, end, perms, path);
                }
            }
        }

        /// <summary>Parses one /proc/PID/maps line. Returns false on malformed input.</summary>
        public static bool TryParseMapsLine(string line, out ulong start, out ulong end, out string perms, out string path)
        {
            start = end = 0;
            perms = "----";
            path = "";
            if (string.IsNullOrEmpty(line))
                return false;

            int dash = line.IndexOf('-');
            int sp = dash >= 0 ? line.IndexOf(' ', dash) : -1;
            if (dash <= 0 || sp <= dash)
                return false;
            if (!ulong.TryParse(line.AsSpan(0, dash), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out start))
                return false;
            if (!ulong.TryParse(line.AsSpan(dash + 1, sp - dash - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out end))
                return false;

            perms = sp + 5 <= line.Length ? line.Substring(sp + 1, 4) : "----";

            // Pathname starts at the first '/' or '[' past the permission field.
            int slash = line.IndexOf('/', sp);
            int brack = line.IndexOf('[', sp);
            int pi = slash >= 0 && (brack < 0 || slash < brack) ? slash : brack;
            if (pi >= 0)
                path = line.Substring(pi).Trim();

            return true;
        }

        // ----- P/Invoke ----------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct IoVec
        {
            public IntPtr Base;
            public IntPtr Len;
        }

        // Native-width types for correctness on 32-bit ARM.
        [DllImport("libc", SetLastError = true)]
        private static extern nint process_vm_readv(int pid, ref IoVec localIov, nuint liovcnt, ref IoVec remoteIov, nuint riovcnt, nuint flags);
    }
}
