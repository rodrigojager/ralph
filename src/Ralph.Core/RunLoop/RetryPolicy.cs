using Ralph.Engines.Abstractions;

namespace Ralph.Core.RunLoop;

public sealed class RetryPolicy
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public bool ShouldRetry(EngineResult result, int attempt)
    {
        if (attempt >= MaxRetries) return false;
        if (result.DetectedErrors.Count == 0) return false;
        return result.DetectedErrors.Any(e => e is DetectedErrorKind.Auth or DetectedErrorKind.RateLimit or DetectedErrorKind.Network);
    }

    public async Task WaitBeforeRetryAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(RetryDelay, cancellationToken);
    }
}
