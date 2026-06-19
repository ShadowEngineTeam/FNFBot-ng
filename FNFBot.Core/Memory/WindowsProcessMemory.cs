using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FNFBot.Core.Memory
{
    /// <summary>Windows backend: OpenProcess / ReadProcessMemory / VirtualQueryEx.</summary>
    public sealed class WindowsProcessMemory : ProcessMemory
    {
        private IntPtr _handle;
        private bool _disposed;

        private WindowsProcessMemory(int pid, string name, IntPtr handle) : base(pid, name)
        {
            _handle = handle;
        }

        public static List<ProcessPick> List()
        {
            var list = new List<ProcessPick>();
            int self = Environment.ProcessId;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self || p.MainWindowHandle == IntPtr.Zero)
                        continue;
                    string title = p.MainWindowTitle ?? "";
                    if (title.Length == 0)
                        continue;
                    list.Add(new ProcessPick(p.Id, p.ProcessName, title));
                }
                catch { }
                finally { p.Dispose(); }
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static ProcessMemory Open(int pid, Action<string> log)
        {
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

            var mem = new WindowsProcessMemory(pid, name, h);
            mem.ResolveMainModule(log);
            mem.DetectBitness(log);
            return mem;
        }

        private void DetectBitness(Action<string> log)
        {
            // A 32-bit bot can only ever reach a 32-bit target.
            if (IntPtr.Size == 4)
            {
                PointerSize = 4;
                try
                {
                    if (IsWow64Process(_handle, out bool wow64) && !wow64)
                        log?.Invoke($"{Name} looks like a 64-bit game, but this is the 32-bit bot build, so it cannot read 64-bit memory. Use the 64-bit (x64/arm64) build, or play with manual F2.");
                }
                catch { }
                return;
            }

            try
            {
                if (IsWow64Process(_handle, out bool wow64) && wow64)
                {
                    PointerSize = 4;
                    log?.Invoke($"{Name} is a 32-bit process (pointer size 4).");
                }
            }
            catch { }
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
                log?.Invoke($"Could not read {Name}'s main module range; will scan all writable memory.");
            }
        }

        public override bool IsAlive
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

        public override bool Read(ulong address, byte[] buffer, int count)
        {
            if (_disposed || _handle == IntPtr.Zero || buffer == null || count <= 0)
                return false;
            bool ok = ReadProcessMemory(_handle, (IntPtr)(long)address, buffer, (IntPtr)count, out IntPtr read);
            return ok && read.ToInt64() == count;
        }

        // MBI size / address ceiling depend on the BOT's own bitness (28/4 GB vs 48/full).
        private static readonly ulong MaxUserAddr = IntPtr.Size == 8 ? 0x7FFFFFFFFFFFUL : 0xFFFFFFFFUL;
        private static readonly int MbiSize = IntPtr.Size == 8 ? 48 : 28;

        private IEnumerable<(ulong addr, ulong size, uint state, uint protect, uint type)> EnumRegions(ulong start, ulong stop)
        {
            if (_disposed || _handle == IntPtr.Zero)
                yield break;

            var mbi = new byte[48];
            ulong cur = start;
            while (cur < stop)
            {
                if (VirtualQueryEx(_handle, (IntPtr)(long)cur, mbi, (IntPtr)MbiSize) == IntPtr.Zero)
                    break;

                ulong regionBase, regionSize;
                uint state, protect;
                uint type;
                if (IntPtr.Size == 8)
                {
                    regionBase = BitConverter.ToUInt64(mbi, 0);
                    regionSize = BitConverter.ToUInt64(mbi, 24);
                    state = BitConverter.ToUInt32(mbi, 32);
                    protect = BitConverter.ToUInt32(mbi, 36);
                    type = BitConverter.ToUInt32(mbi, 40);
                }
                else
                {
                    regionBase = BitConverter.ToUInt32(mbi, 0);
                    regionSize = BitConverter.ToUInt32(mbi, 12);
                    state = BitConverter.ToUInt32(mbi, 16);
                    protect = BitConverter.ToUInt32(mbi, 20);
                    type = BitConverter.ToUInt32(mbi, 24);
                }

                if (regionSize == 0)
                    break;
                yield return (regionBase, regionSize, state, protect, type);

                ulong next = regionBase + regionSize;
                if (next <= cur)
                    break;
                cur = next;
            }
        }

        private static bool IsWritable(uint p) =>
            (p & (PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;

        private static bool IsReadable(uint p) =>
            (p & (PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY
                | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;

        private static bool IsGuarded(uint p) => (p & (PAGE_GUARD | PAGE_NOACCESS)) != 0;

        public override IEnumerable<(ulong addr, ulong size)> WritableRegions(bool moduleOnly)
        {
            if (!moduleOnly || !HasModule)
            {
                foreach (var (regionBase, regionSize, state, protect, _) in EnumRegions(0x10000UL, MaxUserAddr))
                {
                    if (state != MEM_COMMIT || !IsWritable(protect) || IsGuarded(protect))
                        continue;
                    yield return (regionBase, regionSize);
                }
                yield break;
            }

            foreach (var (regionBase, regionSize, state, protect, type) in EnumRegions(0x10000UL, MaxUserAddr))
            {
                if (state != MEM_COMMIT || !IsWritable(protect) || IsGuarded(protect))
                    continue;
                if (type != MEM_IMAGE)
                    continue;
                yield return (regionBase, regionSize);
            }
        }

        public override IEnumerable<(ulong addr, ulong size)> ReadableRegions()
        {
            foreach (var (regionBase, regionSize, state, protect, _) in EnumRegions(0x10000UL, MaxUserAddr))
                if (state == MEM_COMMIT && IsReadable(protect) && !IsGuarded(protect))
                    yield return (regionBase, regionSize);
        }

        public override void Dispose()
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
        private const uint MEM_IMAGE = 0x1000000;
        private const uint PAGE_READONLY = 0x02;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_WRITECOPY = 0x08;
        private const uint PAGE_EXECUTE_READ = 0x20;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint PAGE_EXECUTE_WRITECOPY = 0x80;
        private const uint PAGE_GUARD = 0x100;
        private const uint PAGE_NOACCESS = 0x01;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadProcessMemory(IntPtr handle, IntPtr baseAddress, byte[] buffer, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualQueryEx(IntPtr handle, IntPtr address, [Out] byte[] mbi, IntPtr length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr handle, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process(IntPtr handle, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);
    }
}
