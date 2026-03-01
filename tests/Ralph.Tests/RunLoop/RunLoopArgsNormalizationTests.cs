using System.Reflection;
using Ralph.Core.RunLoop;

namespace Ralph.Tests.RunLoop;

public class RunLoopArgsNormalizationTests
{
    [Fact]
    public void BuildExtraArgs_ReturnsTokenizedFlags()
    {
        var method = typeof(RunLoopService).GetMethod(
            "BuildExtraArgs",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var args = (IReadOnlyList<string>?)method!.Invoke(
            null,
            new object?[] { "gemini", 2048, 0.2, null });

        Assert.NotNull(args);
        Assert.Equal(new[] { "--max-tokens", "2048", "--temperature", "0.2" }, args);
    }
}
