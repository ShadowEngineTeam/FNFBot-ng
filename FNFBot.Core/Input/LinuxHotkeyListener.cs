using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace FNFBot.Core.Input
{
    [SupportedOSPlatform("linux")]
    public sealed class LinuxHotkeyListener : IHotkeyListener
    {
        [DllImport("libc", SetLastError = true)]
        private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern nint read(int fd, byte[] buf, nuint count);

        [DllImport("libc", SetLastError = true)]
        private static extern nint ioctl(int fd, nuint request, byte[] buf);

        private const nuint EVIOCGNAME = 0x82004506;

        private const int O_RDONLY = 0;
        private const int O_NONBLOCK = 0x800;

        private const ushort EV_KEY = 0x01;
        private const int KEY_F1 = 59;

        private readonly List<int> _fds = new List<int>();
        private Thread _thread;
        private volatile bool _running;

        public event Action<BotHotkey> Pressed;

        public void Start()
        {
            if (_running) return;

            try
            {
                if (Directory.Exists("/dev/input"))
                {
                    string[] all = Directory.GetFiles("/dev/input", "event*");
                    foreach (string path in all)
                    {
                        int fd = open(path, O_RDONLY | O_NONBLOCK);
                        if (fd < 0) continue;
                        string name = ReadDeviceName(fd);
                        if (name != "FNFBot virtual keyboard")
                            _fds.Add(fd);
                        else
                            close(fd);
                    }
                }

                string devicesFile = "/proc/bus/input/devices";
                if (File.Exists(devicesFile))
                    OpenKeyboardDevices(devicesFile, _fds);
            }
            catch { }

            if (_fds.Count == 0)
                return;

            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "FNFBot-hotkeys" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            foreach (int fd in _fds)
                close(fd);
            _fds.Clear();
        }

        private void Loop()
        {
            int tv = IntPtr.Size * 2;
            int evSize = tv + 8;

            var buf = new byte[evSize * 64];
            while (_running)
            {
                foreach (int fd in _fds)
                {
                    nint n = read(fd, buf, (nuint)buf.Length);
                    if (n <= 0) continue;

                    for (int off = 0; off + evSize <= (int)n; off += evSize)
                    {
                        ushort type = BitConverter.ToUInt16(buf, off + tv);
                        ushort code = BitConverter.ToUInt16(buf, off + tv + 2);
                        int value = BitConverter.ToInt32(buf, off + tv + 4);

                        if (type != EV_KEY || value != 1)
                            continue;

                        int idx = code - KEY_F1;
                        if (idx >= 0 && idx <= 6)
                            Pressed?.Invoke((BotHotkey)idx);
                    }
                }
                Thread.Sleep(8);
            }
        }

        private static string ReadDeviceName(int fd)
        {
            var buf = new byte[256];
            if (ioctl(fd, EVIOCGNAME, buf) >= 0)
            {
                int len = Array.IndexOf(buf, (byte)0);
                if (len > 0) return Encoding.ASCII.GetString(buf, 0, len);
            }
            return "?";
        }

        private static void OpenKeyboardDevices(string path, List<int> fds)
        {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return; }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("H:") || line.IndexOf(" kbd ", StringComparison.Ordinal) < 0)
                    continue;

                int evIdx = line.IndexOf("event", StringComparison.Ordinal);
                if (evIdx < 0) continue;

                int end = evIdx + 5;
                while (end < line.Length && char.IsDigit(line[end]))
                    end++;

                string evNum = line.Substring(evIdx + 5, end - evIdx - 5);
                string devPath = "/dev/input/event" + evNum;

                int fd = open(devPath, O_RDONLY | O_NONBLOCK);
                if (fd < 0) continue;

                string name = ReadDeviceName(fd);
                if (name == "FNFBot virtual keyboard")
                {
                    close(fd);
                    continue;
                }

                fds.Add(fd);
            }
        }

        public void Dispose() => Stop();
    }
}
