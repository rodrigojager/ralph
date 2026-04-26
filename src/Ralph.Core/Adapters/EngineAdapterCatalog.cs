using System.Text.Json;
using Ralph.Engines.Agent;
using Ralph.Engines.Registry;
using Ralph.Persistence.Workspace;

namespace Ralph.Core.Adapters;

public sealed class EngineAdapterCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly WorkspaceInitializer _workspace;

    public EngineAdapterCatalog(WorkspaceInitializer? workspace = null)
    {
        _workspace = workspace ?? new WorkspaceInitializer();
    }

    public IReadOnlyList<EngineAdapterManifest> LoadAll(string workingDirectory)
    {
        var manifests = new List<EngineAdapterManifest>();
        foreach (var dir in GetManifestDirectories(workingDirectory))
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                var manifest = TryLoad(file);
                if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Name))
                    manifests.Add(manifest);
            }
        }

        return manifests
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public EngineAdapterManifest? Resolve(string workingDirectory, string engineName, string? adapterName = null)
    {
        var target = string.IsNullOrWhiteSpace(adapterName) ? engineName : adapterName!;
        return LoadAll(workingDirectory).FirstOrDefault(m =>
            m.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
    }

    public void RegisterAdapters(string workingDirectory, EngineRegistry registry)
    {
        foreach (var manifest in LoadAll(workingDirectory))
        {
            if (registry.Get(manifest.Name) != null)
                continue;

            registry.Register(new AgentEngine(
                manifest.Name,
                string.IsNullOrWhiteSpace(manifest.Command) ? manifest.Name : manifest.Command!));
        }
    }

    public string CreateTemplate(string workingDirectory, string name, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Adapter name is required.", nameof(name));

        var dir = _workspace.GetAdaptersDir(workingDirectory);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, SanitizeFileName(name) + ".json");
        if (File.Exists(path) && !force)
            return path;

        var manifest = new EngineAdapterManifest
        {
            Name = name,
            Command = name,
            PromptTransport = "argument",
            OutputMode = "plain",
            PromptFlag = null,
            ModelFlag = "--model",
            DefaultArgs = Array.Empty<string>(),
            SafeArgs = Array.Empty<string>(),
            AutoArgs = Array.Empty<string>(),
            DangerousArgs = Array.Empty<string>()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return path;
    }

    private IEnumerable<string> GetManifestDirectories(string workingDirectory)
    {
        yield return _workspace.GetAdaptersDir(workingDirectory);
        yield return _workspace.GetPluginsDir(workingDirectory);
    }

    private static EngineAdapterManifest? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<EngineAdapterManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return new string(chars).Trim();
    }
}
