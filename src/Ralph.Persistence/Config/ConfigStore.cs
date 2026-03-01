using System.Text.Json;

namespace Ralph.Persistence.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public RalphConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
            return RalphConfig.Default;
        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<RalphConfig>(json, Options);
            return config ?? RalphConfig.Default;
        }
        catch
        {
            return RalphConfig.Default;
        }
    }

    public void Save(string configPath, RalphConfig config)
    {
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(configPath, json);
    }
}
