namespace FNFBot20
{
    public class ChartTag
    {
        public string Path { get; set; }
        public string Difficulty { get; set; }

        public ChartTag(string path, string difficulty = null)
        {
            Path = path;
            Difficulty = difficulty;
        }
    }
}
