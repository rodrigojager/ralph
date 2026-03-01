using Ralph.Core.RunLoop;
using Ralph.Engines.Abstractions;

namespace Ralph.Tests.RunLoop;

public class RetryPolicyTests
{
    [Fact]
    public void ShouldNotRetry_WhenCommandNotFound()
    {
        var policy = new RetryPolicy { MaxRetries = 3 };
        var result = new EngineResult
        {
            ExitCode = -1,
            DetectedErrors = new[] { DetectedErrorKind.CommandNotFound }
        };

        Assert.False(policy.ShouldRetry(result, 0));
    }

    [Fact]
    public void ShouldRetry_WhenNetwork()
    {
        var policy = new RetryPolicy { MaxRetries = 3 };
        var result = new EngineResult
        {
            ExitCode = -1,
            DetectedErrors = new[] { DetectedErrorKind.Network }
        };

        Assert.True(policy.ShouldRetry(result, 0));
    }
}
