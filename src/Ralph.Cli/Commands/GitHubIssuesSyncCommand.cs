using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class GitHubIssuesSyncCommand
{
    public async Task<int> ExecuteAsync(
        string workingDirectory,
        string? repo,
        string? label,
        string? state,
        string? outputPath,
        IStringCatalog s,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            Console.Error.WriteLine(s.Get("tasks.sync.usage"));
            return 1;
        }

        var effectiveState = string.IsNullOrWhiteSpace(state) ? "open" : state!.ToLowerInvariant();
        if (effectiveState != "open" && effectiveState != "closed" && effectiveState != "all")
        {
            Console.Error.WriteLine(s.Get("tasks.sync.invalid_state"));
            return 1;
        }

        var output = ResolveOutputPath(workingDirectory, outputPath);
        if (File.Exists(output) && !force)
        {
            Console.Error.WriteLine(s.Format("tasks.sync.output_exists", output));
            return 1;
        }

        var issues = await FetchIssuesAsync(repo!, label, effectiveState, cancellationToken);
        var content = BuildPrdContent(repo!, label, effectiveState, issues);
        File.WriteAllText(output, content, new UTF8Encoding(false));
        Console.WriteLine(s.Format("tasks.sync_ok", issues.Count, output));
        return 0;
    }

    internal static string BuildPrdContent(string repo, string? label, string state, IReadOnlyList<GitHubIssueItem> issues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("task: Synced from GitHub issues");
        sb.AppendLine("engine: codex");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# PRD");
        sb.AppendLine();
        sb.AppendLine($"Source: github issues ({repo}), state={state}" + (string.IsNullOrWhiteSpace(label) ? string.Empty : $", label={label}"));
        sb.AppendLine();
        foreach (var issue in issues.OrderBy(i => i.Number))
            sb.AppendLine($"- [ ] #{issue.Number} {issue.Title} ({issue.HtmlUrl})");
        if (issues.Count == 0)
            sb.AppendLine("- [ ] No matching issues found");
        return sb.ToString();
    }

    private static string ResolveOutputPath(string workingDirectory, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return Path.Combine(workingDirectory, "PRD.md");
        return Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(workingDirectory, outputPath);
    }

    private static async Task<List<GitHubIssueItem>> FetchIssuesAsync(string repo, string? label, string state, CancellationToken cancellationToken)
    {
        var parts = repo.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            throw new InvalidOperationException("Invalid repo format. Expected owner/repo.");
        var owner = Uri.EscapeDataString(parts[0]);
        var name = Uri.EscapeDataString(parts[1]);
        var query = $"https://api.github.com/repos/{owner}/{name}/issues?per_page=100&state={Uri.EscapeDataString(state)}";
        if (!string.IsNullOrWhiteSpace(label))
            query += $"&labels={Uri.EscapeDataString(label!)}";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ralph-cli", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(query, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub API request failed: {(int)response.StatusCode} {response.ReasonPhrase}");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var result = new List<GitHubIssueItem>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("pull_request", out _))
                continue;
            var number = item.GetProperty("number").GetInt32();
            var title = item.GetProperty("title").GetString() ?? "(untitled)";
            var htmlUrl = item.GetProperty("html_url").GetString() ?? "";
            result.Add(new GitHubIssueItem(number, title, htmlUrl));
        }
        return result;
    }
}

public sealed record GitHubIssueItem(int Number, string Title, string HtmlUrl);
