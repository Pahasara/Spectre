namespace Spectre.Core;

public static class AppPaths
{
    private static string DataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectre");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DbPath => Path.Combine(DataDirectory, "data.sqlite3");
    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");
}
