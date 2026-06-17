using System;

namespace FNFBot.Core.Input
{
    public enum BotHotkey
    {
        TogglePlay, // F1
        OffsetUp,   // F2
        OffsetDown, // F3
        PressUp,    // F4
        PressDown,  // F5
        HoldUp,     // F6
        HoldDown    // F7
    }

    /// <summary>
    /// Watches global hotkeys (F1-F7) even when the game — not the bot — has focus.
    /// Per-OS implementation (Windows = GetAsyncKeyState polling, etc.).
    /// </summary>
    public interface IHotkeyListener : IDisposable
    {
        event Action<BotHotkey> Pressed;
        void Start();
        void Stop();
    }
}
