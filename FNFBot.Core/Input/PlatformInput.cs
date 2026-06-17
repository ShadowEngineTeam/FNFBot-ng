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
                    return new MacInputBackend();
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
    }

    internal sealed class NullHotkeyListener : IHotkeyListener
    {
        public event Action<BotHotkey> Pressed { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
