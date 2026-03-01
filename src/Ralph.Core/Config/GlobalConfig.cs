using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ralph.Core.Config;

public sealed class GlobalConfig
{
    public static readonly string[] ValidUiModes = { "none", "spectre", "gum", "spectre+gum", "tui" };

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    [JsonPropertyName("lang")]
    public string Lang { get; set; } = "en";

    [JsonPropertyName("ui")]
    public string Ui { get; set; } = "spectre";

    [JsonPropertyName("installDir")]
    public string? InstallDir { get; set; }

    [JsonPropertyName("releaseRepo")]
    public string? ReleaseRepo { get; set; }

    // ── persistence ──────────────────────────────────────────────────────────

    public static string ConfigPath()
    {
        var dir = AppContext.BaseDirectory;
        return Path.Combine(dir, "ralph-config.json");
    }

    public static GlobalConfig Load()
    {
        var path = ConfigPath();
        if (!File.Exists(path)) return Normalize(new GlobalConfig());
        try
        {
            var json = File.ReadAllText(path);
            return Normalize(JsonSerializer.Deserialize<GlobalConfig>(json) ?? new GlobalConfig());
        }
        catch { return Normalize(new GlobalConfig()); }
    }

    public void Save()
    {
        var path = ConfigPath();
        File.WriteAllText(path, JsonSerializer.Serialize(this, _json));
    }

    // ── env-var override (RALPH_UI=none for emergency fallback) ──────────────
    public string EffectiveUi()
    {
        var env = Environment.GetEnvironmentVariable("RALPH_UI");
        var candidate = string.IsNullOrWhiteSpace(env) ? Ui : env;
        return NormalizeUiMode(candidate);
    }

    public static string NormalizeUiMode(string? uiMode)
    {
        if (string.IsNullOrWhiteSpace(uiMode))
            return "spectre";

        var normalized = uiMode.Trim().ToLowerInvariant();
        if (normalized == "spectre_gum")
            normalized = "spectre+gum";
        if (normalized == "plain")
            normalized = "none";

        return ValidUiModes.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : "spectre";
    }

    private static GlobalConfig Normalize(GlobalConfig config)
    {
        config.Ui = NormalizeUiMode(config.Ui);
        if (string.IsNullOrWhiteSpace(config.Lang))
            config.Lang = "en";
        if (!string.IsNullOrWhiteSpace(config.ReleaseRepo))
            config.ReleaseRepo = config.ReleaseRepo.Trim();
        return config;
    }
}
