using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// A thin, read-only window into another process's memory (Windows only).
    /// Used to find and follow the FNF engine's <c>Conductor.songPosition</c> static.
    /// On non-Windows platforms every operation is a safe no-op and
    /// <see cref="IsSupported"/> is false.
    /// </summary>
    public sealed class ProcessMemory : IDisposable
    {
        public static bool IsSupported => OperatingSystem.IsWindows();

        public int Pid { get; }
        public string Name { get; }

        /// <summary>Base address of the target's main module (the .exe image).</summary>
        public ulong ModuleBase { get; private set; }

        /// <summary>One-past-the-end address of the main module image.</summary>
        public ulong ModuleEnd { get; private set; }

        public bool HasModule => ModuleEnd > ModuleBase;

        private IntPtr _handle;
        private bool _disposed;

        private ProcessMemory(int pid, string name, IntPtr handle)
        {
            Pid = pid;
            Name = name;
            _handle = handle;
        }

        // ----- process discovery -------------------------------------------------

        /// <summary>
        /// All windowed processes the user could attach to, excluding this app.
        /// </summary>
        public static List<ProcessPick> ListProcesses()
        {
            var list = new List<ProcessPick>();
            if (!IsSupported)
                return list;

            int self = Environment.ProcessId;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self)
                        continue;
                    if (p.MainWindowHandle == IntPtr.Zero)
                        continue;
                    string title = p.MainWindowTitle ?? "";
                    if (title.Length == 0)
                        continue;
                    list.Add(new ProcessPick(p.Id, p.ProcessName, title));
                }
                catch
                {
                    // Access denied / exited mid-enumeration; skip.
                }
                finally
                {
                    p.Dispose();
                }
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>
        /// Opens a process for memory reading. Returns null on failure (e.g. denied,
        /// or running on a non-Windows OS).
        /// </summary>
        public static ProcessMemory OpenByPid(int pid, Action<string> log = null)
        {
            if (!IsSupported)
            {
                log?.Invoke("Memory attach is only supported on Windows.");
                return null;
            }

            string name;
            try
            {
                using var p = Process.GetProcessById(pid);
                name = p.ProcessName;
            }
            catch
            {
                log?.Invoke($"Process {pid} is no longer running.");
                return null;
            }

            IntPtr h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
                h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
            {
                log?.Invoke($"OpenProcess failed for {name} (error {Marshal.GetLastWin32Error()}). Try running the bot as administrator.");
                return null;
            }

            var mem = new ProcessMemory(pid, name, h);
            mem.ResolveMainModule(log);
            mem.DetectBitness(log);
            return mem;
        }

        /// <summary>Pointer width of the target: 8 (x64) or 4 (a 32-bit/WOW64 process).</summary>
        public int PointerSize { get; private set; } = 8;

        private void DetectBitness(Action<string> log)
        {
            try
            {
                if (IsWow64Process(_handle, out bool wow64) && wow64)
                {
                    PointerSize = 4;
                    log?.Invoke($"{Name} is a 32-bit process (pointer size 4).");
                }
            }
            catch
            {
                // Default to 64-bit.
            }
        }

        private void ResolveMainModule(Action<string> log)
        {
            try
            {
                using var p = Process.GetProcessById(Pid);
                var m = p.MainModule;
                if (m != null)
                {
                    ModuleBase = (ulong)m.BaseAddress.ToInt64();
                    ModuleEnd = ModuleBase + (ulong)m.ModuleMemorySize;
                }
            }
            catch
            {
                // Cross-bitness or access-denied: leave module range empty and the
                // scanner will fall back to a full writable-memory sweep.
                log?.Invoke($"Could not read {Name}'s main module range; will scan all writable memory.");
            }
        }

        // ----- liveness ----------------------------------------------------------

        public bool IsAlive
        {
            get
            {
                if (_disposed || _handle == IntPtr.Zero)
                    return false;
                if (GetExitCodeProcess(_handle, out uint code))
                    return code == STILL_ACTIVE;
                return false;
            }
        }

        // ----- reading -----------------------------------------------------------

        public bool Read(ulong address, byte[] buffer, int count)
        {
            if (_disposed || _handle == IntPtr.Zero || buffer == null || count <= 0)
                return false;
            bool ok = ReadProcessMemory(_handle, (IntPtr)address, buffer, (IntPtr)count, out IntPtr read);
            return ok && read.ToInt64() == count;
        }

        private readonly byte[] _d8 = new byte[8];
        private readonly byte[] _p8 = new byte[8];

        public bool ReadDouble(ulong address, out double value)
        {
            value = 0;
            if (!Read(address, _d8, 8))
                return false;
            value = BitConverter.ToDouble(_d8, 0);
            return true;
        }

        /// <summary>Reads a pointer-sized value (for following static-&gt;heap chains).</summary>
        public bool ReadPointer(ulong address, out ulong value)
        {
            value = 0;
            if (!Read(address, _p8, PointerSize))
                return false;
            value = PointerSize == 8 ? BitConverter.ToUInt64(_p8, 0) : BitConverter.ToUInt32(_p8, 0);
            return true;
        }

        /// <summary>
        /// Enumerates committed, writable, non-guard memory regions. When the main
        /// module range is known and <paramref name="moduleOnly"/> is true, the sweep
        /// is restricted to the .exe image (where HXCPP statics such as
        /// <c>Conductor.songPosition</c> live): a few MB instead of gigabytes.
        /// </summary>
        public IEnumerable<(ulong addr, ulong size)> WritableRegions(bool moduleOnly)
        {
            if (_disposed || _handle == IntPtr.Zero)
                yield break;

            ulong start = moduleOnly && HasModule ? ModuleBase : 0x10000UL;
            ulong stop = moduleOnly && HasModule ? ModuleEnd : 0x7FFFFFFFFFFFUL;

            ulong cur = start;
            int mbiSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION64>();

            while (cur < stop)
            {
                if (VirtualQueryEx(_handle, (IntPtr)cur, out MEMORY_BASIC_INFORMATION64 mbi, (IntPtr)mbiSize) == IntPtr.Zero)
                    break;

                ulong regionBase = mbi.BaseAddress;
                ulong regionSize = mbi.RegionSize;
                if (regionSize == 0)
                    break;

                bool committed = mbi.State == MEM_COMMIT;
                bool writable =
                    (mbi.Protect & (PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
                bool guarded = (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0;

                if (committed && writable && !guarded)
                {
                    ulong a = Math.Max(regionBase, start);
                    ulong end = Math.Min(regionBase + regionSize, stop);
                    if (end > a)
                        yield return (a, end - a);
                }

                ulong next = regionBase + regionSize;
                if (next <= cur)
                    break;
                cur = next;
            }
        }

        /// <summary>
        /// Enumerates committed, readable, non-guard regions across the whole address
        /// space. Used to validate that a candidate pointer points at a real object
        /// (its target, and the object's vtable, must land in readable memory).
        /// </summary>
        public IEnumerable<(ulong addr, ulong size)> ReadableRegions()
        {
            if (_disposed || _handle == IntPtr.Zero)
                yield break;

            ulong cur = 0x10000UL;
            ulong stop = 0x7FFFFFFFFFFFUL;
            int mbiSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION64>();

            while (cur < stop)
            {
                if (VirtualQueryEx(_handle, (IntPtr)cur, out MEMORY_BASIC_INFORMATION64 mbi, (IntPtr)mbiSize) == IntPtr.Zero)
                    break;

                ulong regionBase = mbi.BaseAddress;
                ulong regionSize = mbi.RegionSize;
                if (regionSize == 0)
                    break;

                bool committed = mbi.State == MEM_COMMIT;
                bool readable = (mbi.Protect & (PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY
                    | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
                bool guarded = (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0;

                if (committed && readable && !guarded)
                    yield return (regionBase, regionSize);

                ulong next = regionBase + regionSize;
                if (next <= cur)
                    break;
                cur = next;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        // ----- P/Invoke ----------------------------------------------------------

        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint STILL_ACTIVE = 259;

        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_READONLY = 0x02;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_WRITECOPY = 0x08;
        private const uint PAGE_EXECUTE_READ = 0x20;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint PAGE_EXECUTE_WRITECOPY = 0x80;
        private const uint PAGE_GUARD = 0x100;
        private const uint PAGE_NOACCESS = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION64
        {
            public ulong BaseAddress;
            public ulong AllocationBase;
            public uint AllocationProtect;
            public uint __alignment1;
            public ulong RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
            public uint __alignment2;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadProcessMemory(IntPtr handle, IntPtr baseAddress, byte[] buffer, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualQueryEx(IntPtr handle, IntPtr address, out MEMORY_BASIC_INFORMATION64 mbi, IntPtr length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr handle, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process(IntPtr handle, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);
    }
}
