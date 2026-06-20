using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FNFBot.Core.Memory
{
    /// <summary>Read-only access to another process's memory. Platform-agnostic; dispatching is in <see cref="OpenByPid"/>.</summary>
    public abstract class ProcessMemory : IDisposable
    {
        public static bool IsSupported =>
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

        public int Pid { get; }
        public string Name { get; }

        public ulong ModuleBase { get; protected set; }

        public ulong ModuleEnd { get; protected set; }

        public bool HasModule => ModuleEnd > ModuleBase;

        public int PointerSize { get; protected set; } = 8;

        protected ProcessMemory(int pid, string name)
        {
            Pid = pid;
            Name = name;
        }

        // ----- platform contract -------------------------------------------------

        public abstract bool IsAlive { get; }

        public abstract bool Read(ulong address, byte[] buffer, int count);

        public abstract IEnumerable<(ulong addr, ulong size)> WritableRegions(bool moduleOnly);

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

        /// <summary>Linux/macOS process list. On Linux, excludes processes without DISPLAY or WAYLAND_DISPLAY.</summary>
        protected static List<ProcessPick> ListByProcessName()
        {
            var list = new List<ProcessPick>();
            int self = Environment.ProcessId;
            bool isLinux = OperatingSystem.IsLinux();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self)
                        continue;
                    string name = p.ProcessName;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (isLinux && !HasDisplayConnection(p.Id))
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

        /// <summary>Checks /proc/&lt;pid&gt;/environ for DISPLAY or WAYLAND_DISPLAY.</summary>
        private static bool HasDisplayConnection(int pid)
        {
            try
            {
                string environ = File.ReadAllText($"/proc/{pid}/environ");
                int i = 0;
                while (i < environ.Length)
                {
                    int end = environ.IndexOf('\0', i);
                    if (end < 0) end = environ.Length;
                    if (end - i > 0)
                    {
                        string entry = environ.Substring(i, end - i);
                        if (entry.StartsWith("DISPLAY=", StringComparison.Ordinal) ||
                            entry.StartsWith("WAYLAND_DISPLAY=", StringComparison.Ordinal))
                            return true;
                    }
                    i = end + 1;
                }
            }
            catch
            {
                // Can't read environment (permission, vanished); treat as non-GUI.
            }
            return false;
        }
    }
}
