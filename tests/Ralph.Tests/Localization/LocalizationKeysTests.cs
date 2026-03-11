using System.Text.Json;

namespace Ralph.Tests.Localization;

public class LocalizationKeysTests
{
    [Fact]
    public void NewKeys_ExistInBothLanguageFiles()
    {
        var root = FindRepositoryRoot();
        var en = Load(Path.Combine(root, "lang", "en.json"));
        var pt = Load(Path.Combine(root, "lang", "ptBR.json"));

        var required = new[]
        {
            "parallel.dry_run",
            "parallel.retry_failed",
            "about.ralph_loop",
            "about.inspiration",
            "tasks.sync.usage",
            "tasks.sync.invalid_state",
            "tasks.sync.output_exists",
            "run.status_engine_attempt_exit",
            "run.no_change_fail_fast",
            "run.no_change_marked_for_review",
            "run.manual_review_remaining",
            "run.status_skipped_task_review",
            "config.invalid_no_change_policy",
            "ui.tui_runtime_error",
            "tui.tokens_template",
            "tui.shortcut.page_up",
            "tui.shortcut.page_down",
            "tui.shortcut.home",
            "tui.shortcut.end",
            "cursor.output.current_task",
            "cursor.output.success",
            "cursor.output.error"
        };

        foreach (var key in required)
        {
            Assert.True(en.ContainsKey(key), $"Missing key in en.json: {key}");
            Assert.True(pt.ContainsKey(key), $"Missing key in ptBR.json: {key}");
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var segments = new[] { dir }.Concat(Enumerable.Repeat("..", i)).ToArray();
            var candidate = Path.GetFullPath(Path.Combine(segments));
            if (File.Exists(Path.Combine(candidate, "Ralph.sln")))
                return candidate;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
