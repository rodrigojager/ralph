using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Ralph.Core.Git;

public sealed class GitService
{
    public bool IsGitRepo(string workingDirectory) =>
        Directory.Exists(Path.Combine(workingDirectory, ".git"));

    public string? GetCurrentBranch(string workingDirectory)
    {
        var output = RunGit("rev-parse --abbrev-ref HEAD", workingDirectory);
        var branch = output?.Trim();
        return string.IsNullOrEmpty(branch) ? null : branch;
    }

    public bool HasUncommittedChanges(string workingDirectory)
    {
        var output = RunGit("status --porcelain", workingDirectory);
        return !string.IsNullOrWhiteSpace(output);
    }

    public IReadOnlyList<string> GetUntrackedFiles(string workingDirectory)
    {
        var output = RunGit("ls-files --others --exclude-standard", workingDirectory);
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<string>();
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public bool RestoreTrackedFiles(string workingDirectory)
    {
        var output = RunGit("restore --worktree --staged .", workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool CheckoutBranch(string workingDirectory, string branchName)
    {
        var output = RunGit($"checkout {branchName}", workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool CreateAndCheckoutBranch(string workingDirectory, string branchName, string baseBranch)
    {
        // Ensure we're on the base branch first
        RunGit($"checkout {baseBranch}", workingDirectory, captureStderr: true);
        var output = RunGit($"checkout -b {branchName}", workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool CommitAll(string workingDirectory, string message)
    {
        RunGit("add -A", workingDirectory, captureStderr: true);
        var escaped = message.Replace("\"", "\\\"");
        var output = RunGit($"commit -m \"{escaped}\"", workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool PushBranch(string workingDirectory, string branchName)
    {
        var output = RunGit($"push -u origin {branchName}", workingDirectory, captureStderr: true);
        return output != null;
    }

    public bool CreatePullRequest(string workingDirectory, string title, string body, bool draft)
    {
        var escapedTitle = title.Replace("\"", "\\\"");
        var escapedBody = body.Replace("\"", "\\\"");
        var draftFlag = draft ? " --draft" : "";
        var output = RunGh($"pr create --title \"{escapedTitle}\" --body \"{escapedBody}\"{draftFlag}", workingDirectory);
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

    private static string? RunGit(string arguments, string workingDirectory, bool captureStderr = false)
    {
        return RunCommand("git", arguments, workingDirectory, captureStderr);
    }

    private static string? RunGh(string arguments, string workingDirectory)
    {
        return RunCommand("gh", arguments, workingDirectory, captureStderr: true);
    }

    private static string? RunCommand(string fileName, string arguments, string workingDirectory, bool captureStderr)
    {
        try
        {
            var stdout = new StringBuilder();
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = captureStderr,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit(30_000);
            return process.ExitCode == 0 ? stdout.ToString() : null;
        }
        catch
        {
            return null;
        }
    }
}
