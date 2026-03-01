using System.Text.Json;

namespace Ralph.Persistence.State;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public RalphState Load(string statePath)
    {
        if (!File.Exists(statePath))
            return new RalphState();
        var json = File.ReadAllText(statePath);
        try
        {
            return JsonSerializer.Deserialize<RalphState>(json, Options) ?? new RalphState();
        }
        catch
        {
            return new RalphState();
        }
    }

    public void Save(string statePath, RalphState state)
    {
        state.LastUpdated = DateTimeOffset.UtcNow;
        var dir = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(state, Options);
        File.WriteAllText(statePath, json);
    }
}
