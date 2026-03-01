namespace Ralph.Cli.Commands;

internal static class ReleaseChannel
{
    public const string RepoEnvVar = "RALPH_REPO";
    public const string DefaultRepo = "rodrigojager/ralph";
    public const string LanguageAssetName = "ralph-lang.zip";

    public static string ResolveRepo(string? configuredRepo = null)
    {
        var envRepo = Environment.GetEnvironmentVariable(RepoEnvVar);
        var repo = !string.IsNullOrWhiteSpace(envRepo) ? envRepo : configuredRepo;
        return string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo.Trim();
    }

    public static string LatestReleaseApi(string repo) =>
        $"https://api.github.com/repos/{repo}/releases/latest";

    public static bool IsConfigured(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo))
            return false;

        var normalized = repo.Trim();
        return normalized.Contains('/');
    }
}
