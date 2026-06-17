using System;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// NightmareVision songPosition reader. Module <c>static var songPosition:Float</c>;
    /// its countdown is <c>songPosition = 0; songPosition -= Conductor.crotchet * 5</c>
    /// (note the "crotchet" spelling) and climbs continuously via
    /// <c>songPosition += FlxG.elapsed * 1000 * playbackRate</c> — a deep negative ramp the
    /// shared arming catches. Inherits all of <see cref="ModuleStaticSongClock"/>.
    /// </summary>
    public sealed class NightmareVisionSongClock : ModuleStaticSongClock
    {
        public NightmareVisionSongClock(Action<string> log) : base(log) { }

        protected override string EngineName => "NightmareVision";

        public static bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("nightmare", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("nmv", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
