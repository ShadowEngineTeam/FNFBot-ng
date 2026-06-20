using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace FNFBot.Core.Input
{
    /// <summary>
    /// Detects F1-F4 globally by polling CGEventSourceKeyState (like Windows' GetAsyncKeyState).
    /// Needs Input Monitoring / Accessibility permission on recent macOS.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public sealed class MacHotkeyListener : IHotkeyListener
    {
        private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

        [DllImport(CG)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGEventSourceKeyState(int stateID, ushort key);

        private const int kCGEventSourceStateHIDSystemState = 1;

        // Carbon virtual key codes (non-sequential; named constants avoid confusion).
        private const ushort kVK_F1 = 0x7A; // 122
        private const ushort kVK_F2 = 0x78; // 120
        private const ushort kVK_F3 = 0x63; // 99
        private const ushort kVK_F4 = 0x76; // 118

        private static readonly (ushort Key, BotHotkey Action)[] FKeys = {
            (kVK_F1, BotHotkey.Rewind),
            (kVK_F2, BotHotkey.PlayPause),
            (kVK_F3, BotHotkey.FastForward),
            (kVK_F4, BotHotkey.CloseChart)
        };

        private Thread _thread;
        private volatile bool _running;

        public event Action<BotHotkey> Pressed;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "FNFBot-hotkeys" };
            _thread.Start();
        }

        public void Stop() => _running = false;

        private void Loop()
        {
            var prev = new bool[FKeys.Length];
            while (_running)
            {
                for (int i = 0; i < FKeys.Length; i++)
                {
                    bool down = CGEventSourceKeyState(kCGEventSourceStateHIDSystemState, FKeys[i].Key);
                    if (down && !prev[i])
                        Pressed?.Invoke(FKeys[i].Action);
                    prev[i] = down;
                }
                Thread.Sleep(20);
            }
        }

        public void Dispose() => Stop();
    }
}
