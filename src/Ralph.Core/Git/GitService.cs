using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Ralph.Core.Processes;

namespace Ralph.Core.Git;

public sealed class GitService
{
    public bool IsGitRepo(string workingDirectory) =>
        Directory.Exists(Path.Combine(workingDirectory, ".git"));

    public string? GetCurrentBranch(string workingDirectory)
    {
        var output = RunGit(new[] { "rev-parse", "--abbrev-ref", "HEAD" }, workingDirectory);
        var branch = output?.Trim();
        return string.IsNullOrEmpty(branch) ? null : branch;
    }

    public bool HasUncommittedChanges(string workingDirectory)
    {
        var output = RunGit(new[] { "status", "--porcelain" }, workingDirectory);
        return !string.IsNullOrWhiteSpace(output);
    }

    public IReadOnlyList<string> GetUntrackedFiles(string workingDirectory)
    {
        var output = RunGit(new[] { "ls-files", "--others", "--exclude-standard" }, workingDirectory);
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<string>();
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public bool RestoreTrackedFiles(string workingDirectory)
    {
        var output = RunGit(new[] { "restore", "--worktree", "--staged", "." }, workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool CheckoutBranch(string workingDirectory, string branchName)
    {
        var output = RunGit(new[] { "checkout", branchName }, workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool CreateAndCheckoutBranch(string workingDirectory, string branchName, string baseBranch)
    {
        // Ensure we're on the base branch first
        RunGit(new[] { "checkout", baseBranch }, workingDirectory, captureStderr: true);
        var output = RunGit(new[] { "checkout", "-b", branchName }, workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool CommitAll(string workingDirectory, string message)
    {
        RunGit(new[] { "add", "-A" }, workingDirectory, captureStderr: true);
        var output = RunGit(new[] { "commit", "-m", message }, workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool PushBranch(string workingDirectory, string branchName)
    {
        var output = RunGit(new[] { "push", "-u", "origin", branchName }, workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool CreatePullRequest(string workingDirectory, string title, string body, bool draft)
    {
        var args = new List<string> { "pr", "create", "--title", title, "--body", body };
        if (draft)
            args.Add("--draft");
        var output = RunGh(args, workingDirectory);
        return output != null;
    }

    public static string SlugifyBranch(string taskText)
    {
        var slug = taskText.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-{2,}", "-");
        slug = slug.Trim('-');
        if (slug.Length > 50)
            slug = slug[..50].TrimEnd('-');
        return $"ralph/{slug}";
    }

    private static string? RunGit(IReadOnlyList<string> arguments, string workingDirectory, bool captureStderr = false)
    {
        return RunCommand("git", arguments, workingDirectory, captureStderr);
    }

    private static string? RunGh(IReadOnlyList<string> arguments, string workingDirectory)
    {
        return RunCommand("gh", arguments, workingDirectory, captureStderr: true);
    }

    private static string? RunCommand(string fileName, IReadOnlyList<string> arguments, string workingDirectory, bool captureStderr)
    {
        try
        {
            var result = ProcessRunner.Run(fileName, arguments, workingDirectory, TimeSpan.FromSeconds(30));
            if (result.ExitCode != 0)
                return null;
            return result.Stdout;
        }
        catch
        {
            return null;
        }
    }
}
