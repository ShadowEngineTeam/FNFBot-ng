using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace FNFBot.App
{
    internal static class Program
    {
        // Raise the system timer resolution to 1ms on Windows so the play loop's Thread.Sleep
        // is fine-grained (default is ~15.6ms). No-op / not needed on Linux & macOS.
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uMilliseconds);

        [STAThread]
        public static void Main(string[] args)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                timeBeginPeriod(1);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
