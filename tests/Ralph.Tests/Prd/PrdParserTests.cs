using Ralph.Tasks.Prd;
using Xunit;

namespace Ralph.Tests.Prd;

public class PrdParserTests
{
    [Fact]
    public void Parses_simple_task_list()
    {
        var content = @"# PRD
- [ ] First task
- [x] Done task
- [ ] Second task";
        var doc = PrdParser.ParseContent(content);
        Assert.Equal(3, doc.TaskEntries.Count);
        Assert.False(doc.TaskEntries[0].IsCompleted);
        Assert.True(doc.TaskEntries[1].IsCompleted);
        Assert.False(doc.TaskEntries[2].IsCompleted);
        Assert.Equal("First task", doc.TaskEntries[0].DisplayText);
        Assert.Equal(0, doc.GetNextPendingTaskIndex());
    }

    [Fact]
    public void GetNextPendingTaskIndex_returns_first_incomplete()
    {
        var content = @"- [x] A
- [ ] B
- [ ] C";
        var doc = PrdParser.ParseContent(content);
        Assert.Equal(1, doc.GetNextPendingTaskIndex());
    }

    [Fact]
    public void GetNextPendingTaskIndex_returns_null_when_all_done()
    {
        var content = @"- [x] A
- [x] B";
        var doc = PrdParser.ParseContent(content);
        Assert.Null(doc.GetNextPendingTaskIndex());
    }

    [Fact]
    public void Skipped_for_review_tasks_are_not_pending()
    {
        var content = @"- [~] Needs manual review
- [ ] Next task";
        var doc = PrdParser.ParseContent(content);
        Assert.Equal(2, doc.TaskEntries.Count);
        Assert.True(doc.TaskEntries[0].IsSkippedForReview);
        Assert.False(doc.TaskEntries[0].IsCompleted);
        Assert.Equal(1, doc.GetNextPendingTaskIndex());
    }

    [Fact]
    public void Parses_frontmatter()
    {
        var content = @"---
task: My task
test_command: dotnet test
engine: cursor
model: gpt-4
browser_command: npx playwright test
---
- [ ] Do something";
        var doc = PrdParser.ParseContent(content);
        Assert.NotNull(doc.Frontmatter);
        Assert.Equal("My task", doc.Frontmatter.Task);
        Assert.Equal("dotnet test", doc.Frontmatter.TestCommand);
        Assert.Equal("cursor", doc.Frontmatter.Engine);
        Assert.Equal("gpt-4", doc.Frontmatter.Model);
        Assert.Equal("npx playwright test", doc.Frontmatter.BrowserCommand);
        Assert.Single(doc.TaskEntries);
    }

    [Fact]
    public void Preserves_indentation()
    {
        var content = @"  - [ ] Indented task";
        var doc = PrdParser.ParseContent(content);
        Assert.Single(doc.TaskEntries);
        Assert.Contains("  - ", doc.TaskEntries[0].RawLine);
    }

    [Fact]
    public void Handles_multiple_lists()
    {
        var content = @"
## Section 1
- [ ] A
- [x] B
## Section 2
- [ ] C";
        var doc = PrdParser.ParseContent(content);
        Assert.Equal(3, doc.TaskEntries.Count);
        Assert.Equal(0, doc.GetNextPendingTaskIndex());
    }

    [Fact]
    public void Ignores_code_blocks_content()
    {
        var content = @"
- [ ] Real task
```markdown
- [ ] Not a task
```
- [x] Done";
        var doc = PrdParser.ParseContent(content);
        Assert.Equal(2, doc.TaskEntries.Count);
        Assert.Equal("Real task", doc.TaskEntries[0].DisplayText);
        Assert.Equal("Done", doc.TaskEntries[1].DisplayText);
    }

    [Fact]
    public void Handles_nested_sublists()
    {
        var content = @"- [ ] Parent task
  - [ ] Child task A
  - [x] Child task B
- [x] Done parent";
        var doc = PrdParser.ParseContent(content);
        Assert.Equal(4, doc.TaskEntries.Count);
        Assert.False(doc.TaskEntries[0].IsCompleted);  // Parent
        Assert.False(doc.TaskEntries[1].IsCompleted);  // Child A
        Assert.True(doc.TaskEntries[2].IsCompleted);   // Child B done
        Assert.True(doc.TaskEntries[3].IsCompleted);   // Done parent
        Assert.Equal(0, doc.GetNextPendingTaskIndex()); // First pending is parent
    }

    [Fact]
    public void Parses_yaml_prd_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "PRD.yaml");
            var content = @"task: Ship feature
engine: codex
model: gpt-5
test_command: dotnet test
tasks:
  - [ ] First
  - [x] Done";
            File.WriteAllText(path, content);
            var doc = PrdParser.Parse(path);
            Assert.NotNull(doc.Frontmatter);
            Assert.Equal("Ship feature", doc.Frontmatter!.Task);
            Assert.Equal("codex", doc.Frontmatter.Engine);
            Assert.Equal("gpt-5", doc.Frontmatter.Model);
            Assert.Equal("dotnet test", doc.Frontmatter.TestCommand);
            Assert.Equal(2, doc.TaskEntries.Count);
            Assert.Equal("First", doc.TaskEntries[0].DisplayText);
            Assert.True(doc.TaskEntries[1].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Parses_json_task_source()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "tasks.json");
            File.WriteAllText(path, """
[
  { "text": "First", "completed": false },
  { "task": "Second", "completed": true }
]
""");
            var doc = PrdParser.Parse(path);
            Assert.Equal(2, doc.TaskEntries.Count);
            Assert.Equal("First", doc.TaskEntries[0].DisplayText);
            Assert.False(doc.TaskEntries[0].IsCompleted);
            Assert.Equal("Second", doc.TaskEntries[1].DisplayText);
            Assert.True(doc.TaskEntries[1].IsCompleted);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Parses_json_skipped_for_review_status()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RalphTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "tasks.json");
            File.WriteAllText(path, """
[
  { "text": "First", "status": "skipped_for_review" },
  { "task": "Second", "completed": false }
]
""");
            var doc = PrdParser.Parse(path);
            Assert.True(doc.TaskEntries[0].IsSkippedForReview);
            Assert.Equal(1, doc.GetNextPendingTaskIndex());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
