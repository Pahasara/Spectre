using System.Text.Json;

namespace Spectre.Core;

public class ConfigService
{
    private readonly Lock _lock = new();
    private AppConfig _current = Load();

    public event Action<AppConfig>? ConfigChanged;

    public AppConfig Current
    {
        get { lock (_lock) return _current; }
    }

    public void Save(AppConfig config)
    {
        lock (_lock) _current = config;

        var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
        File.WriteAllText(AppPaths.ConfigPath, json);

        ConfigChanged?.Invoke(config);
    }

    private static AppConfig Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigPath))
            {
                var json = File.ReadAllText(AppPaths.ConfigPath);
                var config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
                if (config is not null) return config;
            }
        }
        catch
        {
            // corrupt or unreadable config file — fall back to defaults rather than crash
        }

        return new AppConfig();
    }
}
