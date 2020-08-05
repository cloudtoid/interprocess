namespace Cloudtoid.Interprocess
{
    internal readonly struct SharedAssetsIdentifier
    {
        public SharedAssetsIdentifier(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string Name { get; }
        public string Path { get; }
    }
}
