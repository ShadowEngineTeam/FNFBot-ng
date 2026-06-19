using System;

namespace FNFBot.Core.Memory
{
    public sealed class CDevSongClock : ModuleStaticSongClock
    {
        public CDevSongClock(Action<string> log) : base(log) { }

        protected override string EngineName => "CDev Engine";

        public static bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("cdev", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
