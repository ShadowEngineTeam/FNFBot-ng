using System;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// Kade Engine songPosition reader. Kade is one of the oldest engines, built straight
    /// on the original FNF HaxeFlixel base, so <c>Conductor.songPosition</c> is a plain
    /// module <c>static var ... :Float</c>. Its countdown sets it to a fixed <c>-5000</c>
    /// (PlayState) and climbs continuously via <c>songPosition += FlxG.elapsed * 1000</c> —
    /// a deep negative ramp the shared confirmed-countdown arming catches. No engine-specific
    /// behaviour beyond the label, so it inherits all of <see cref="ModuleStaticSongClock"/>.
    /// </summary>
    public sealed class KadeSongClock : ModuleStaticSongClock
    {
        public KadeSongClock(Action<string> log) : base(log) { }

        protected override string EngineName => "Kade Engine";

        public static bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("kade", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
