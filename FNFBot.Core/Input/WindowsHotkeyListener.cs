using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace FNFBot.Core.Input
{
    /// <summary>
    /// Detects F1-F4 by polling GetAsyncKeyState on a background thread (rather than a
    /// global keyboard hook, which would add input latency).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsHotkeyListener : IHotkeyListener
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // F1-F4 virtual key codes.
        private static readonly int[] Vks = { 0x70, 0x71, 0x72, 0x73 };

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
            var prev = new bool[Vks.Length];
            while (_running)
            {
                for (int i = 0; i < Vks.Length; i++)
                {
                    bool down = (GetAsyncKeyState(Vks[i]) & 0x8000) != 0;
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
