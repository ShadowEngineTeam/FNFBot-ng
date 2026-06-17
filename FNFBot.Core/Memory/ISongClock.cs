namespace FNFBot.Core.Memory
{
    /// <summary>
    /// A source of the game's current song time, found and followed in another process's
    /// memory. Implementations: <see cref="ModuleStaticSongClock"/> (and its subclasses
    /// <see cref="PsychSongClock"/> / <see cref="CodenameSongClock"/>) for engines that keep
    /// <c>Conductor.songPosition</c> as a module static, and <see cref="VSliceSongClock"/>
    /// for Funkin V-Slice, whose Conductor is a heap singleton reached through a static
    /// pointer.
    /// </summary>
    public interface ISongClock
    {
        /// <summary>True once an address (or pointer chain) has been pinned.</summary>
        bool Located { get; }

        /// <summary>Last raw songPosition read, in ms.</summary>
        double PositionMs { get; }

        /// <summary>Play clock in ms, smoothed between the engine's per-frame updates.</summary>
        double InterpolatedMs { get; }

        /// <summary>True while the value is changing (gameplay running, not paused).</summary>
        bool Advancing { get; }

        /// <summary>Name of the attached process, or null.</summary>
        string AttachedName { get; }

        bool HasProcess { get; }
        bool IsProcessAlive { get; }

        void Attach(ProcessMemory mem);
        void Detach();

        /// <summary>One step of work: scan-and-lock while unlocked, re-read once locked.</summary>
        void Tick();
    }
}
