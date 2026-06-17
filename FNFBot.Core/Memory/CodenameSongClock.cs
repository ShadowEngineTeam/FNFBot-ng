using System;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// Codename Engine songPosition reader.
    ///
    /// <para>Codename also keeps <c>Conductor.songPosition</c> as a module static, so it
    /// uses the shared <see cref="ModuleStaticSongClock"/> scan. Engine specifics worth
    /// recording:</para>
    /// <list type="bullet">
    ///   <item><description>It is declared <c>static var songPosition(get, default)</c>; the
    ///   getter returns <c>backingField - Options.songOffset</c>, but we scan/lock the raw
    ///   backing field, which equals the actual music time (the offset is the player's audio
    ///   calibration). Reading raw is correct for hitting charted note times; if a constant
    ///   skew ever appears, it equals the user's Codename song offset and the bot's own
    ///   Offset setting compensates.</description></item>
    ///   <item><description>Countdown depth is <c>-(crochet*introLength)+songOffset</c> with
    ///   <c>crochet = 15000*stepsPerBeat/bpm</c>; still a deep negative dip, caught by the
    ///   shared confirmed-countdown arming.</description></item>
    ///   <item><description>Codename drives the SAME global songPosition forward for freeplay
    ///   autoplay previews, handled by the bot's disarm-on-reentry, not here.</description></item>
    /// </list>
    /// </summary>
    public sealed class CodenameSongClock : ModuleStaticSongClock
    {
        public CodenameSongClock(Action<string> log) : base(log) { }

        protected override string EngineName => "Codename";

        public static bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("codename", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
