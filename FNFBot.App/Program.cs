using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace FNFBot.App
{
    internal static class Program
    {
        // 1ms timer resolution on Windows so Thread.Sleep in the play loop is fine-grained (default ~15.6ms).
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
