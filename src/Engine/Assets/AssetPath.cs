namespace Engine.Assets;

public static class AssetPath
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "assets")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    /// "assets/shaders/triangle.vert" → абсолютный путь в репозитории
    public static string Resolve(string relative) => Path.Combine(Root, relative);
}