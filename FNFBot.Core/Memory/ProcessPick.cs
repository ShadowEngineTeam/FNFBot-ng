namespace FNFBot.Core.Memory
{
    /// <summary>
    /// A user-selectable running process, shown in the "Attach Game" picker.
    /// </summary>
    public sealed class ProcessPick
    {
        public int Pid { get; }
        public string Name { get; }
        public string Title { get; }

        public ProcessPick(int pid, string name, string title)
        {
            Pid = pid;
            Name = name;
            Title = title;
        }

        public string Display =>
            string.IsNullOrEmpty(Title)
                ? $"{Name}  (pid {Pid})"
                : $"{Name}  (pid {Pid})  —  {Title}";

        public override string ToString() => Display;
    }
}
