using Ralph.Tasks.Prd;
using Xunit;

namespace Ralph.Tests.Prd;

public class PrdWriterTests
{
    [Fact]
    public void MarkTaskCompleted_changes_only_one_line()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var prdPath = Path.Combine(dir, "PRD.md");
            var content = @"# PRD
- [ ] First
- [ ] Second
- [ ] Third";
            File.WriteAllText(prdPath, content);
            var doc = PrdParser.Parse(prdPath);
            PrdWriter.MarkTaskCompleted(prdPath, doc, 1);
            var lines = File.ReadAllLines(prdPath);
            Assert.Contains("[ ]", lines[1]);
            Assert.Contains("[x]", lines[2]);
            Assert.Contains("[ ]", lines[3]);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void MarkTaskCompleted_preserves_rest_of_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var prdPath = Path.Combine(dir, "PRD.md");
            var content = @"Title
- [ ] Task one
- [ ] Task two";
            File.WriteAllText(prdPath, content);
            var doc = PrdParser.Parse(prdPath);
            PrdWriter.MarkTaskCompleted(prdPath, doc, 0);
            var read = File.ReadAllText(prdPath);
            Assert.Contains("[x]", read);
            Assert.Contains("Task one", read);
            Assert.Contains("Task two", read);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void MarkTaskCompleted_updates_json_task_source()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var prdPath = Path.Combine(dir, "tasks.json");
            File.WriteAllText(prdPath, """
[
  { "text": "First", "completed": false },
  { "text": "Second", "completed": false }
]
""");

            var doc = PrdParser.Parse(prdPath);
            PrdWriter.MarkTaskCompleted(prdPath, doc, 1);

            var updated = PrdParser.Parse(prdPath);
            Assert.False(updated.TaskEntries[0].IsCompleted);
            Assert.True(updated.TaskEntries[1].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void MarkTaskCompleted_converts_skipped_review_to_completed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, "- [~] Review manually");

            var doc = PrdParser.Parse(prdPath);
            PrdWriter.MarkTaskCompleted(prdPath, doc, 0);

            var updated = PrdParser.Parse(prdPath);
            Assert.True(updated.TaskEntries[0].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void MarkTaskSkippedForReview_updates_markdown_task_source()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var prdPath = Path.Combine(dir, "PRD.md");
            File.WriteAllText(prdPath, "- [ ] Review later");

            var doc = PrdParser.Parse(prdPath);
            PrdWriter.MarkTaskSkippedForReview(prdPath, doc, 0);

            var updated = PrdParser.Parse(prdPath);
            Assert.True(updated.TaskEntries[0].IsSkippedForReview);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
