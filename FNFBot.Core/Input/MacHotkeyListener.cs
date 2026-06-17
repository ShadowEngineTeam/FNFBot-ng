using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace FNFBot.Core.Input
{
    /// <summary>
    /// Detects F1-F7 globally by polling CGEventSourceKeyState (like Windows' GetAsyncKeyState).
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

        // macOS virtual key codes for F1..F7, mapped onto BotHotkey 0..6.
        private static readonly ushort[] FKeys = { 122, 120, 99, 118, 96, 97, 98 };

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
                    bool down = CGEventSourceKeyState(kCGEventSourceStateHIDSystemState, FKeys[i]);
                    if (down && !prev[i])
                        Pressed?.Invoke((BotHotkey)i);
                    prev[i] = down;
                }
                Thread.Sleep(20);
            }
        }

        public void Dispose() => Stop();
    }
}
