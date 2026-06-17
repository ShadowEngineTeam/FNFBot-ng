using System;

namespace FNFBot.Core.Memory
{
    /// <summary>
    /// FNF Troll Engine songPosition reader. Troll is a Psych-derived engine; it keeps
    /// <c>Conductor.songPosition</c> as a module <c>static var ... :Float</c>, with a
    /// countdown of <c>startOnTime - Conductor.crochet * 5</c> that climbs continuously
    /// (<c>songPosition += elapsedMS</c>). It also has a <c>songSyncMode</c> / multiple
    /// audio tracks and a separate <c>visualPosition</c>, but the logical <c>songPosition</c>
    /// we lock advances at ~1 ms/ms like every other engine. Inherits all of
    /// <see cref="ModuleStaticSongClock"/>.
    /// </summary>
    public sealed class TrollSongClock : ModuleStaticSongClock
    {
        public TrollSongClock(Action<string> log) : base(log) { }

        protected override string EngineName => "Troll Engine";

        public static bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("troll", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
