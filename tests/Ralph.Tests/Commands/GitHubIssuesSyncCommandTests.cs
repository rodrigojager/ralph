using Ralph.Cli.Commands;
using Ralph.Core.Localization;

namespace Ralph.Tests.Commands;

public class GitHubIssuesSyncCommandTests
{
    [Fact]
    public async Task ExecuteAsync_InvalidState_UsesLocalizedMessage()
    {
        var cmd = new GitHubIssuesSyncCommand();
        var stderr = new StringWriter();
        var previous = Console.Error;
        Console.SetError(stderr);
        try
        {
            var exit = await cmd.ExecuteAsync(
                Directory.GetCurrentDirectory(),
                "owner/repo",
                null,
                "invalid",
                null,
                StringCatalog.Default());

            Assert.Equal(1, exit);
            Assert.Contains(StringCatalog.Default().Get("tasks.sync.invalid_state"), stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(previous);
        }
    }
}
