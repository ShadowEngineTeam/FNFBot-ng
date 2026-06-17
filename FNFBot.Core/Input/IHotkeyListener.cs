using System;

namespace FNFBot.Core.Input
{
    public enum BotHotkey
    {
        Rewind,      // F1
        PlayPause,   // F2
        FastForward, // F3
        CloseChart   // F4
    }

    /// <summary>
    /// Watches global hotkeys (F1-F4) even when the game — not the bot — has focus.
    /// Per-OS implementation (Windows = GetAsyncKeyState polling, etc.).
    /// </summary>
    public interface IHotkeyListener : IDisposable
    {
        event Action<BotHotkey> Pressed;
        void Start();
        void Stop();
    }
}
