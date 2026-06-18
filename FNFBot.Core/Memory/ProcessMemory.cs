using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// A read-only window into another process's memory, used to find and follow the FNF
    /// engine's <c>Conductor.songPosition</c>. This is the platform-independent contract;
    /// <see cref="OpenByPid"/> returns the right backend for the OS
    /// (<see cref="WindowsProcessMemory"/>, <see cref="LinuxProcessMemory"/>,
    /// <see cref="MacProcessMemory"/>). Reading another process needs elevated rights on
    /// every OS: admin on Windows, and <c>sudo</c> (or a relaxed ptrace scope) on Linux/macOS.
    /// </summary>
    public abstract class ProcessMemory : IDisposable
    {
        public static bool IsSupported =>
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

        public int Pid { get; }
        public string Name { get; }

        /// <summary>Base address of the target's main module (the executable image), or 0.</summary>
        public ulong ModuleBase { get; protected set; }

        /// <summary>One-past-the-end address of the main module image, or 0.</summary>
        public ulong ModuleEnd { get; protected set; }

        public bool HasModule => ModuleEnd > ModuleBase;

        /// <summary>Pointer width of the target: 8 (64-bit) or 4 (32-bit).</summary>
        public int PointerSize { get; protected set; } = 8;

        protected ProcessMemory(int pid, string name)
        {
            Pid = pid;
            Name = name;
        }

        // ----- platform contract -------------------------------------------------

        public abstract bool IsAlive { get; }

        /// <summary>Reads <paramref name="count"/> bytes at <paramref name="address"/> from the target.</summary>
        public abstract bool Read(ulong address, byte[] buffer, int count);

        /// <summary>Committed writable regions; restricted to the main module when asked and known.</summary>
        public abstract IEnumerable<(ulong addr, ulong size)> WritableRegions(bool moduleOnly);

        /// <summary>Committed readable regions across the whole address space.</summary>
        public abstract IEnumerable<(ulong addr, ulong size)> ReadableRegions();

        public virtual void Dispose() { }

        // ----- shared typed reads ------------------------------------------------

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

        // ----- discovery / factory ----------------------------------------------

        public static List<ProcessPick> ListProcesses()
        {
            if (OperatingSystem.IsWindows())
                return WindowsProcessMemory.List();
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                return ListByProcessName();
            return new List<ProcessPick>();
        }

        public static ProcessMemory OpenByPid(int pid, Action<string> log = null)
        {
            if (OperatingSystem.IsWindows())
                return WindowsProcessMemory.Open(pid, log);
            if (OperatingSystem.IsLinux())
                return LinuxProcessMemory.Open(pid, log);
            if (OperatingSystem.IsMacOS())
                return MacProcessMemory.Open(pid, log);
            log?.Invoke("Memory attach isn't supported on this OS.");
            return null;
        }

        /// <summary>
        /// Linux/macOS process list. There is no portable window-title API there, so we list
        /// by process name and let the user pick (e.g. "ShadowEngine", "Funkin", "Corruption").
        /// </summary>
        protected static List<ProcessPick> ListByProcessName()
        {
            var list = new List<ProcessPick>();
            int self = Environment.ProcessId;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self)
                        continue;
                    string name = p.ProcessName;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    list.Add(new ProcessPick(p.Id, name, ""));
                }
                catch
                {
                    // Vanished or not permitted; skip.
                }
                finally
                {
                    p.Dispose();
                }
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }
    }
}
