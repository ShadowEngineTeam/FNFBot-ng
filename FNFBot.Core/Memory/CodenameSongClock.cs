using System;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// Codename Engine songPosition reader. Codename keeps <c>Conductor.songPosition</c> as a
    /// module static, so it reuses the shared <see cref="ModuleStaticSongClock"/> scan. Notes:
    /// the field is <c>static var songPosition(get, default)</c> whose getter subtracts the
    /// player's songOffset, but we scan the raw backing field (the true music time, correct for
    /// charted note timing); its countdown is a deep <c>-(crochet*introLength)+songOffset</c> dip;
    /// and freeplay autoplay drives the same global songPosition forward, which the bot's
    /// disarm-on-reentry handles.
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
