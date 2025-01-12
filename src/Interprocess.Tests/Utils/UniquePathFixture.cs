namespace Cloudtoid.Interprocess.Tests;

public class UniquePathFixture : IDisposable
{
    private static readonly string Root = System.IO.Path.GetTempPath();

    public UniquePathFixture()
    {
        while (true)
        {
            var folder = (DateTime.UtcNow.Ticks % 0xFFFFF).ToStringInvariant("X5");
            Path = System.IO.Path.Combine(Root, folder);
            if (!Directory.Exists(Path))
            {
                Directory.CreateDirectory(Path);
                break;
            }
        }
    }

    internal string Path { get; }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Path))
            PathUtil.TryDeleteFile(file);

        try
        {
            Directory.Delete(Path);
        }
        catch
        {
        }
    }
}