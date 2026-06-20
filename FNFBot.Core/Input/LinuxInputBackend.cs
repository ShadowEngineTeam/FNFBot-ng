using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace FNFBot.Core.Input
{
    /// <summary>
    /// Linux key injection via /dev/uinput. Creates a virtual keyboard at the kernel level,
    /// so injected keys work on both X11 and Wayland.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public sealed class LinuxInputBackend : IInputBackend, IDisposable
    {
        [DllImport("libc", SetLastError = true)]
        private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern nint write(int fd, byte[] buf, nuint count);

        [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
        private static extern int ioctl_int(int fd, nuint request, int arg);

        [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
        private static extern int ioctl_void(int fd, nuint request);

        private const int O_WRONLY = 1;
        private const int O_NONBLOCK = 0x800;

        // ioctl request codes (from linux/uinput.h, computed for x86_64).
        private const nuint UI_SET_EVBIT = 0x40045564;
        private const nuint UI_SET_KEYBIT = 0x40045565;
        private const nuint UI_DEV_CREATE = 0x5501;
        private const nuint UI_DEV_DESTROY = 0x5502;

        private const ushort EV_SYN = 0x00;
        private const ushort EV_KEY = 0x01;
        private const ushort SYN_REPORT = 0x00;

        private ushort[] _keys = { 105, 108, 103, 106 };
        private int _fd = -1;

        public LinuxInputBackend()
        {
            _fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
            if (_fd < 0)
                throw new InvalidOperationException(
                    "Cannot open /dev/uinput (permission denied?). Add your user to the 'input' group or add a udev rule.");

            ioctl_int(_fd, UI_SET_EVBIT, EV_KEY);
            RegisterAllKeys();

            // Legacy uinput_user_dev (1116 bytes on x86_64) + UI_DEV_CREATE.
            var dev = new byte[1116];
            byte[] name = Encoding.ASCII.GetBytes("FNFBot virtual keyboard");
            Array.Copy(name, dev, Math.Min(name.Length, 79));
            // input_id at offset 80: bustype(u16)=BUS_USB(3), vendor, product, version.
            BitConverter.GetBytes((ushort)0x03).CopyTo(dev, 80);
            BitConverter.GetBytes((ushort)0x1209).CopyTo(dev, 82);
            BitConverter.GetBytes((ushort)0xB07).CopyTo(dev, 84);
            BitConverter.GetBytes((ushort)1).CopyTo(dev, 86);

            if (write(_fd, dev, (nuint)dev.Length) < 0 || ioctl_void(_fd, UI_DEV_CREATE) < 0)
            {
                close(_fd);
                _fd = -1;
                throw new InvalidOperationException("Failed to create the uinput virtual keyboard.");
            }
        }

        public void SetKeyCodes(int[] codes)
        {
            _keys = new ushort[codes.Length];
            for (int i = 0; i < codes.Length; i++)
                _keys[i] = (ushort)(codes[i] & 0xFFFF);
        }

        public void KeyDown(int direction) => SendKey(_keys[direction], 1);
        public void KeyUp(int direction) => SendKey(_keys[direction], 0);

        // struct input_event: timeval (2 word-sized longs) + type(u16) + code(u16) + value(s32).
        // 24 bytes on 64-bit, 16 on 32-bit.
        private static readonly int Tv = IntPtr.Size * 2;
        private static readonly int EvSize = Tv + 8;

        private void SendKey(ushort code, int value)
        {
            if (_fd < 0) return;

            var buf = new byte[EvSize * 2];
            WriteEvent(buf, 0, EV_KEY, code, value);
            WriteEvent(buf, EvSize, EV_SYN, SYN_REPORT, 0);
            write(_fd, buf, (nuint)buf.Length);
        }

        private static void WriteEvent(byte[] buf, int off, ushort type, ushort code, int value)
        {
            BitConverter.GetBytes(type).CopyTo(buf, off + Tv);
            BitConverter.GetBytes(code).CopyTo(buf, off + Tv + 2);
            BitConverter.GetBytes(value).CopyTo(buf, off + Tv + 4);
        }

        private void RegisterAllKeys()
        {
            // Register all bindable keys up front.
            var all = new HashSet<ushort>(KeyMap.ToLinuxEv(KeyMap.DefaultNames(9)));
            foreach (ushort k in all)
                ioctl_int(_fd, UI_SET_KEYBIT, k);
        }

        public void Dispose()
        {
            if (_fd >= 0)
            {
                ioctl_void(_fd, UI_DEV_DESTROY);
                close(_fd);
                _fd = -1;
            }
        }
    }
}
