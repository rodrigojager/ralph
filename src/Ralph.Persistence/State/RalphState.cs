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

    [JsonPropertyName("current_run_id")]
    public string? CurrentRunId { get; set; }

    [JsonPropertyName("current_task_id")]
    public string? CurrentTaskId { get; set; }

    [JsonPropertyName("current_task_index")]
    public int? CurrentTaskIndex { get; set; }

    [JsonPropertyName("current_task_text")]
    public string? CurrentTaskText { get; set; }

    [JsonPropertyName("current_engine")]
    public string? CurrentEngine { get; set; }

    [JsonPropertyName("run_status")]
    public string? RunStatus { get; set; }

    [JsonPropertyName("current_run_started_at")]
    public DateTimeOffset? CurrentRunStartedAt { get; set; }

    [JsonPropertyName("current_task_started_at")]
    public DateTimeOffset? CurrentTaskStartedAt { get; set; }

    [JsonPropertyName("last_heartbeat")]
    public DateTimeOffset? LastHeartbeat { get; set; }

    [JsonPropertyName("last_exit_reason")]
    public string? LastExitReason { get; set; }

    [JsonPropertyName("last_exit_at")]
    public DateTimeOffset? LastExitAt { get; set; }

    [JsonPropertyName("unexpected_shutdown_detected")]
    public bool UnexpectedShutdownDetected { get; set; }

    [JsonPropertyName("last_updated")]
    public DateTimeOffset? LastUpdated { get; set; }
}
