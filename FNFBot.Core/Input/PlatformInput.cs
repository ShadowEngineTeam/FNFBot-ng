using System;
using System.Runtime.InteropServices;

namespace FNFBot.Core.Input
{
    public static class PlatformInput
    {
        /// <summary>
        /// Why the input backend failed to initialise (e.g. missing permissions), or null
        /// when key injection is working. Surfaced to the user so a silent no-op is explained.
        /// </summary>
        public static string LastBackendError { get; private set; }

        public static IInputBackend CreateBackend()
        {
            LastBackendError = null;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return new WindowsInputBackend();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return new LinuxInputBackend();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var mac = new MacInputBackend();
                    // Return the backend regardless so it starts working the moment the user
                    // grants permission, but warn now so a silent no-op is explained.
                    if (!MacInputBackend.IsTrusted())
                        LastBackendError = "macOS hasn't granted Accessibility to FNFBot, so key presses are ignored. Enable it under System Settings > Privacy & Security > Accessibility (and Input Monitoring for the F-key hotkeys), then restart the bot.";
                    return mac;
                }
                LastBackendError = "No key-injection backend for this OS.";
            }
            catch (Exception e)
            {
                LastBackendError = e.Message;
            }
            return new NullInputBackend();
        }

        public static IHotkeyListener CreateHotkeyListener()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return new WindowsHotkeyListener();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return new LinuxHotkeyListener();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return new MacHotkeyListener();
            }
            catch { }
            return new NullHotkeyListener();
        }
    }

    internal sealed class NullInputBackend : IInputBackend
    {
        public void KeyDown(int direction) { }
        public void KeyUp(int direction) { }
        public void SetKeyCodes(int[] codes) { }
    }

    internal sealed class NullHotkeyListener : IHotkeyListener
    {
        public event Action<BotHotkey> Pressed { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
