using System.Text;
using Ralph.Core.Processes;
using Ralph.Persistence.Workspace;

namespace Ralph.Core.Context;

public sealed class ContextPackBuilder
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".ralph", "bin", "obj", "node_modules", "dist", "build", ".next", ".nuxt", "coverage", ".idea", ".vs", ".vscode"
    };

    private static readonly string[] AnchorFiles =
    [
        "README.md",
        "package.json",
        "pnpm-lock.yaml",
        "yarn.lock",
        "package-lock.json",
        "go.mod",
        "Cargo.toml",
        "pyproject.toml",
        "requirements.txt",
        "Ralph.sln",
        "*.sln",
        "*.csproj"
    ];

    private readonly WorkspaceInitializer _workspace;

    public ContextPackBuilder(WorkspaceInitializer? workspace = null)
    {
        _workspace = workspace ?? new WorkspaceInitializer();
    }

    public string RefreshRepoMap(string workingDirectory)
    {
        var path = _workspace.GetRepoMapPath(workingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, BuildRepoMap(workingDirectory));
        return path;
    }

    public string BuildRepoMap(string workingDirectory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Repo Map");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"Root: {workingDirectory}");
        sb.AppendLine();

        var gitStatus = ReadGitStatus(workingDirectory);
        if (!string.IsNullOrWhiteSpace(gitStatus))
        {
            sb.AppendLine("## Git Status");
            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine(gitStatus.TrimEnd());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Files");
        sb.AppendLine();
        foreach (var file in EnumerateRelevantFiles(workingDirectory).Take(500))
            sb.AppendLine("- " + Path.GetRelativePath(workingDirectory, file).Replace('\\', '/'));
        sb.AppendLine();

        var anchors = FindAnchorFiles(workingDirectory).Take(25).ToList();
        if (anchors.Count > 0)
        {
            sb.AppendLine("## Anchor Files");
            sb.AppendLine();
            foreach (var file in anchors)
                sb.AppendLine("- " + Path.GetRelativePath(workingDirectory, file).Replace('\\', '/'));
        }

        return sb.ToString();
    }

    public string? ReadRepoMapIfAvailable(string workingDirectory, bool refreshIfMissing)
    {
        var path = _workspace.GetRepoMapPath(workingDirectory);
        if (!File.Exists(path) && refreshIfMissing)
            RefreshRepoMap(workingDirectory);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static IEnumerable<string> EnumerateRelevantFiles(string workingDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(workingDirectory);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(dir);
            }
            catch
            {
                continue;
            }

            foreach (var child in children.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(child))
                {
                    if (!IgnoredDirectories.Contains(Path.GetFileName(child)))
                        pending.Push(child);
                    continue;
                }

                yield return child;
            }
        }
    }

    private static IEnumerable<string> FindAnchorFiles(string workingDirectory)
    {
        foreach (var pattern in AnchorFiles)
        {
            IEnumerable<string> matches;
            try
            {
                matches = Directory.EnumerateFiles(workingDirectory, pattern, SearchOption.AllDirectories)
                    .Where(path => !HasIgnoredSegment(workingDirectory, path));
            }
            catch
            {
                continue;
            }

            foreach (var match in matches)
                yield return match;
        }
    }

    private static bool HasIgnoredSegment(string workingDirectory, string path)
    {
        var rel = Path.GetRelativePath(workingDirectory, path).Replace('\\', '/');
        return rel.Split('/').Any(segment => IgnoredDirectories.Contains(segment));
    }

    private static string? ReadGitStatus(string workingDirectory)
    {
        try
        {
            var result = ProcessRunner.Run("git", new[] { "status", "--short", "--branch" }, workingDirectory, TimeSpan.FromSeconds(5));
            return result.ExitCode == 0 ? result.Stdout : null;
        }
        catch
        {
            return null;
        }
    }
}
