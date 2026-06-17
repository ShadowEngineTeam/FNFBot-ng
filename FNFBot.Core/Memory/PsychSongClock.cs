using System;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// Psych Engine (and its forks, including Shadow Engine) songPosition reader.
    ///
    /// <para>Psych keeps <c>Conductor.songPosition</c> as a plain <c>static var ... :Float</c>
    /// in the main module, and <c>PlayState.startCountdown</c> sets it to
    /// <c>-Conductor.crochet * 5</c> before climbing toward 0 — exactly the deep negative
    /// countdown the shared scan and arming expect. Nothing engine-specific beyond the
    /// label and the process-name match, so it inherits all of
    /// <see cref="ModuleStaticSongClock"/>.</para>
    /// </summary>
    public sealed class PsychSongClock : ModuleStaticSongClock
    {
        public PsychSongClock(Action<string> log) : base(log) { }

        protected override string EngineName => "Psych/Shadow";

        /// <summary>Recognises Psych/Shadow by window/exe name. Most Psych mods carry a
        /// custom exe name, so this is a best-effort hint; unknown module-static engines
        /// fall through to the generic base clock.</summary>
        public static bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("psych", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
