using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// macOS backend (experimental): reads via Mach VM (<c>task_for_pid</c> +
    /// <c>mach_vm_read_overwrite</c> + <c>mach_vm_region</c>). <c>task_for_pid</c> needs root,
    /// so run the bot with <c>sudo</c>. No module range is resolved, so the song clocks scan
    /// all writable memory. Mach calls are guarded, falling back to manual F2 on any failure.
    /// </summary>
    public sealed class MacProcessMemory : ProcessMemory
    {
        private uint _task;
        private bool _disposed;

        private MacProcessMemory(int pid, string name, uint task) : base(pid, name)
        {
            _task = task;
            PointerSize = 8; // modern macOS is 64-bit only
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

            uint self;
            try
            {
                self = GetSelfTask();
            }
            catch (Exception e)
            {
                log?.Invoke($"macOS: could not get the self task port ({e.GetType().Name}). Memory attach is unavailable; use manual F2.");
                return null;
            }
            if (self == 0)
            {
                log?.Invoke("macOS: self task port is 0; memory attach unavailable.");
                return null;
            }

            int kr;
            uint task;
            try
            {
                kr = task_for_pid(self, pid, out task);
            }
            catch (Exception e)
            {
                log?.Invoke($"macOS: task_for_pid unavailable ({e.GetType().Name}); memory attach unavailable.");
                return null;
            }

            if (kr != 0 || task == 0)
            {
                log?.Invoke($"macOS: task_for_pid failed (kr={kr}) for {name}. Run the bot with sudo; task_for_pid needs root to read another app.");
                return null;
            }

            log?.Invoke($"Attached to {name} (pid {pid}) [experimental macOS support, full-memory scan].");
            return new MacProcessMemory(pid, name, task);
        }

        private static uint GetSelfTask()
        {
            try { return mach_task_self(); }
            catch (EntryPointNotFoundException) { return task_self_trap(); }
        }

        public override bool IsAlive
        {
            get
            {
                if (_disposed || _task == 0)
                    return false;
                try { return kill(Pid, 0) == 0; }
                catch { return true; }
            }
        }

        public override unsafe bool Read(ulong address, byte[] buffer, int count)
        {
            if (_disposed || _task == 0 || buffer == null || count <= 0 || count > buffer.Length)
                return false;
            try
            {
                fixed (byte* p = buffer)
                {
                    int kr = mach_vm_read_overwrite(_task, address, (ulong)count, (ulong)p, out ulong outsize);
                    return kr == 0 && outsize == (ulong)count;
                }
            }
            catch
            {
                return false;
            }
        }

        public override IEnumerable<(ulong addr, ulong size)> WritableRegions(bool moduleOnly)
        {
            // No module range on macOS, so moduleOnly is ignored (full writable sweep).
            foreach (var (addr, size, prot) in Regions())
                if ((prot & VM_PROT_WRITE) != 0)
                    yield return (addr, size);
        }

        public override IEnumerable<(ulong addr, ulong size)> ReadableRegions()
        {
            foreach (var (addr, size, prot) in Regions())
                if ((prot & VM_PROT_READ) != 0)
                    yield return (addr, size);
        }

        private IEnumerable<(ulong addr, ulong size, int prot)> Regions()
        {
            ulong addr = 1;
            int guard = 0;
            while (guard++ < 2_000_000)
            {
                ulong size = 0;
                // vm_region_basic_info_data_64_t is 40 bytes (the uint64 `offset` forces
                // 8-byte alignment), i.e. VM_REGION_BASIC_INFO_COUNT_64 == 10 natural_t's.
                // Pass the correct count with a roomy buffer; protection is the first int.
                var info = new int[16];
                uint cnt = VM_REGION_BASIC_INFO_COUNT_64;
                int kr;
                try
                {
                    kr = mach_vm_region(_task, ref addr, ref size, VM_REGION_BASIC_INFO_64, info, ref cnt, out _);
                }
                catch
                {
                    yield break;
                }
                if (kr != 0 || size == 0)
                    yield break;

                yield return (addr, size, info[0]); // info[0] = protection

                ulong next = addr + size;
                if (next <= addr)
                    yield break;
                addr = next;
            }
        }

        public override void Dispose()
        {
            _disposed = true;
            _task = 0;
        }

        // ----- P/Invoke ----------------------------------------------------------

        private const int VM_REGION_BASIC_INFO_64 = 9;     // flavor
        private const uint VM_REGION_BASIC_INFO_COUNT_64 = 10; // sizeof(info_64)/sizeof(int)
        private const int VM_PROT_READ = 0x1;
        private const int VM_PROT_WRITE = 0x2;

        [DllImport("libc", SetLastError = true)]
        private static extern uint mach_task_self();

        [DllImport("libc", SetLastError = true)]
        private static extern uint task_self_trap();

        [DllImport("libc", SetLastError = true)]
        private static extern int task_for_pid(uint targetTask, int pid, out uint task);

        [DllImport("libc", SetLastError = true)]
        private static extern int mach_vm_read_overwrite(uint task, ulong address, ulong size, ulong data, out ulong outSize);

        [DllImport("libc", SetLastError = true)]
        private static extern int mach_vm_region(uint task, ref ulong address, ref ulong size, int flavor, int[] info, ref uint infoCnt, out uint objectName);

        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int sig);
    }
}
