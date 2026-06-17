using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FNFBot.Core.Input
{
    /// <summary>
    /// Windows key injection via SendInput. HaxeFlixel engines run on Lime/SDL (SDL2 and
    /// SDL3), which identify keys by their hardware SCANCODE, not the virtual-key code — so
    /// we inject the real extended arrow-key scancodes. The arrow scancodes are identical
    /// across SDL2 and SDL3.
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

        // Extended-key scancodes for the arrows, indexed by direction (0=L,1=D,2=U,3=R).
        private static readonly ushort[] ArrowScan = { 0x4B, 0x50, 0x48, 0x4D };

        public void KeyDown(int direction) => SendScan(ArrowScan[direction], false);
        public void KeyUp(int direction) => SendScan(ArrowScan[direction], true);

        private static void SendScan(ushort scan, bool keyUp)
        {
            uint flags = KEYEVENTF_SCANCODE | KEYEVENTF_EXTENDEDKEY;
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
