using System.Text.Json;
using System.Text;

namespace Ralph.Core.Localization;

public sealed class StringCatalog : IStringCatalog
{
    private readonly Dictionary<string, string> _strings;

    private StringCatalog(Dictionary<string, string> strings)
    {
        _strings = strings;
    }

    /// <summary>
    /// Load catalog from a lang JSON file next to the binary.
    /// Falls back to embedded English if the file is missing or invalid.
    /// </summary>
    public static StringCatalog Load(string langCode)
    {
        var exeDir = AppContext.BaseDirectory;
        var langFile = Path.Combine(exeDir, "lang", $"{langCode}.json");

        if (File.Exists(langFile))
        {
            try
            {
                var json = ReadLangFileText(langFile);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed != null)
                {
                    // Merge with English fallback so missing keys always resolve
                    var merged = new Dictionary<string, string>(EmbeddedStrings.English, StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in parsed)
                        merged[kv.Key] = kv.Value;
                    return new StringCatalog(merged);
                }
            }
            catch
            {
                // Fall through to embedded English
            }
        }

        return new StringCatalog(new Dictionary<string, string>(EmbeddedStrings.English, StringComparer.OrdinalIgnoreCase));
    }

    private static string ReadLangFileText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            // Prefer UTF-8 and fail fast on invalid bytes so we can fallback.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Compatibility with legacy Windows-1252 encoded lang files.
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    public static StringCatalog Default() => Load("en");

    public string Get(string key) =>
        _strings.TryGetValue(key, out var val) ? val : $"[{key}]";

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    /// <summary>Returns all available lang codes by scanning the lang/ dir next to the binary.</summary>
    public static IReadOnlyList<string> Available()
    {
        var exeDir = AppContext.BaseDirectory;
        var langDir = Path.Combine(exeDir, "lang");
        if (!Directory.Exists(langDir)) return new[] { "en" };
        return Directory.GetFiles(langDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(x => x)
            .ToList();
    }
}
