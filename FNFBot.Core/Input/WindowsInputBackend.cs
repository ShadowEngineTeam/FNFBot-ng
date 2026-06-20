using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FNFBot.Core.Input
{
    /// <summary>
    /// Windows key injection via SendInput. Uses key names from <see cref="KeyMap"/> to
    /// resolve scancodes, so the user can rebind via the settings UI.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsInputBackend : IInputBackend
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL, wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        private ushort[] _scan = { 0x4B, 0x50, 0x48, 0x4D };
        private bool[] _ext = { true, true, true, true };

        public void SetKeyCodes(int[] codes)
        {
            _scan = new ushort[codes.Length];
            _ext = new bool[codes.Length];
            for (int i = 0; i < codes.Length; i++)
            {
                _scan[i] = (ushort)(codes[i] & 0xFFFF);
                _ext[i] = (codes[i] & 0x10000) != 0;
            }
        }

        public void KeyDown(int direction) => SendScan(_scan[direction], _ext[direction], false);
        public void KeyUp(int direction) => SendScan(_scan[direction], _ext[direction], true);

        private static void SendScan(ushort scan, bool extended, bool keyUp)
        {
            uint flags = KEYEVENTF_SCANCODE;
            if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
            if (keyUp) flags |= KEYEVENTF_KEYUP;

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }
    }
}
