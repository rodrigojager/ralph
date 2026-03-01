using System.Text.Json.Serialization;

namespace Ralph.Persistence.State;

public sealed class RalphState
{
    [JsonPropertyName("iteration")]
    public int Iteration { get; set; }

    [JsonPropertyName("retries")]
    public int Retries { get; set; }

    [JsonPropertyName("last_task_index")]
    public int? LastTaskIndex { get; set; }

    [JsonPropertyName("last_task_text")]
    public string? LastTaskText { get; set; }

    [JsonPropertyName("last_updated")]
    public DateTimeOffset? LastUpdated { get; set; }
}
