using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FNFBot20
{
   
    public class KeyBot
    {
        private const string SettingsFile = "bot.settings";

        public int offset = 25;
        public int PressMs = 40;
        public int HoldReleaseMs = 20;

        public KeyBot()
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    SaveSettings();
                    return;
                }

                foreach (string raw in File.ReadAllLines(SettingsFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0)
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq < 0)
                    {
                        // legacy format: the whole file was just the offset number.
                        if (int.TryParse(line, out int legacy))
                            offset = legacy;
                        continue;
                    }

                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    if (!int.TryParse(line.Substring(eq + 1).Trim(), out int num))
                        continue;

                    switch (key)
                    {
                        case "offset": offset = num; break;
                        case "press": PressMs = Math.Max(1, num); break;
                        case "hold": HoldReleaseMs = num; break;
                    }
                }
            }
            catch
            {
                Form1.WriteToConsole("Failed to load config....");
            }
        }

        public void SaveSettings()
        {
            try
            {
                File.WriteAllText(SettingsFile,
                    $"offset={offset}\npress={PressMs}\nhold={HoldReleaseMs}\n");
            }
            catch
            {
                // non-fatal
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // Virtual-key codes F1..F7.
        private static readonly int[] FunctionKeys = { 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76 };

        private Thread _pollThread;
        private volatile bool _polling;

        public void InitHooks()
        {
            if (_polling)
                return;
            _polling = true;
            _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "FNFBot-hotkeys" };
            _pollThread.Start();
        }

        public void StopHooks()
        {
            _polling = false;
        }

        private void PollLoop()
        {
            var prev = new bool[FunctionKeys.Length];
            while (_polling)
            {
                for (int i = 0; i < FunctionKeys.Length; i++)
                {
                    bool down = (GetAsyncKeyState(FunctionKeys[i]) & 0x8000) != 0;
                    if (down && !prev[i])
                        OnHotkey(i);
                    prev[i] = down;
                }
                Thread.Sleep(20);
            }
        }

        private void OnHotkey(int index)
        {
            switch (index)
            {
                case 0: // F1
                    Bot.Playing = !Bot.Playing;
                    Form1.WriteToConsole("Playing: " + Bot.Playing);
                    if (Bot.ended)
                        Form1.instance.Play();
                    break;
                case 1: // F2
                    offset++;
                    Form1.WriteToConsole("Offset: " + offset);
                    Form1.offset.Text = "Offset: " + offset;
                    SaveSettings();
                    break;
                case 2: // F3
                    offset--;
                    Form1.WriteToConsole("Offset: " + offset);
                    Form1.offset.Text = "Offset: " + offset;
                    SaveSettings();
                    break;
                case 3: // F4
                    PressMs += 5;
                    Form1.WriteToConsole("Press hold: " + PressMs + "ms");
                    SaveSettings();
                    break;
                case 4: // F5
                    PressMs = Math.Max(1, PressMs - 5);
                    Form1.WriteToConsole("Press hold: " + PressMs + "ms");
                    SaveSettings();
                    break;
                case 5: // F6
                    HoldReleaseMs += 5;
                    Form1.WriteToConsole("Sustain overhold: " + HoldReleaseMs + "ms");
                    SaveSettings();
                    break;
                case 6: // F7
                    HoldReleaseMs -= 5;
                    Form1.WriteToConsole("Sustain overhold: " + HoldReleaseMs + "ms");
                    SaveSettings();
                    break;
            }
        }

        // --- Key injection via SendInput ---------------------------------------------
        // Lime uses SDL, which what also Shadow Engine uses. we identify keys by their hardware
        // SCANCODE, not the virtual-key code, and the arrow-key scancodes are identical
        // across both versions. So we inject the real extended arrow-key scancodes; sending
        // only a VK (or a zero scancode) is silently ignored by SDL-based games.

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

        private static readonly ushort[] ArrowScan = { 0x4B, 0x50, 0x48, 0x4D };
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

        public void KeyDown(int dir) => SendScan(ArrowScan[dir], false);

        public void KeyUp(int dir) => SendScan(ArrowScan[dir], true);
    }
}