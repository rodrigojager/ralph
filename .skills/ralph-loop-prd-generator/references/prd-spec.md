# Ralph Loop PRD Spec (Code-Verified)

This document captures what the current codebase actually parses and executes.

## 1) Accepted PRD Sources

- Markdown: `.md` (default example: `PRD.md`)
- YAML: `.yaml` / `.yml`
- JSON tasks array: `.json`

References:
- `src/Ralph.Tasks/Prd/PrdParser.cs`
- `src/Ralph.Cli/Program.cs` (`PRD.md` and `PRD.yaml` detection)

## 2) Markdown Frontmatter (Supported Keys)

Use YAML frontmatter at file start:

```yaml
---
task: <string>
engine: <string>
model: <string>
lint_command: <string>
test_command: <string>
browser_command: <string>
---
```

Only these keys are currently read by parser:
- `task`
- `engine`
- `model`
- `lint_command`
- `test_command`
- `browser_command`

Unknown keys are ignored by parser.

### When to use each key

- `task`: high-level objective label for the run.
- `engine`: default engine for this PRD when CLI `--engine` is not provided.
- `model`: default model for selected engine when CLI `--model` is not provided.
- `lint_command`: command executed for lint validation (unless lint skipped by flags).
- `test_command`: command executed for tests (unless tests skipped by flags).
- `browser_command`: browser/E2E command when browser validation is enabled.

## 3) Engine Values (Practical Set)

The parser accepts any string for `engine`, but runtime support in this repo is oriented to:
- `cursor`
- `claude`
- `codex`
- `opencode`
- `qwen`
- `droid`
- `copilot`
- `gemini`

Use these values unless user explicitly needs a custom mapped command.

## 4) Task Line Syntax

A task is recognized only when line matches markdown checkbox format:

- `- [ ] <text>` pending
- `- [x] <text>` completed

Parser behavior details:
- leading indentation is allowed
- tasks inside fenced code blocks are ignored
- nested sublist tasks are still parsed as tasks

## 5) Parallel Metadata in Task Text

When using `ralph parallel`, inline annotations are parsed from task text:

- Group:
  - `[parallel_group:<name>]`
  - alias: `[group:<name>]`
- Dependencies:
  - `[depends_on:<groupA,groupB>]`
  - alias: `[depends:<groupA,groupB>]`

Constraints from regex:
- group names: letters, numbers, `_`, `-`
- depends list: comma-separated group names

Semantics:
- Tasks whose dependency groups are satisfied can run in current batch.
- If no group/dependency annotations are present, execution is normal parallel batch.

## 6) Ordering Rules for Generator

Always order tasks top-to-bottom so prerequisites come first.

Sequential mode:
- do not add group/dependency tags
- encode order directly by placement

Parallel mode:
- still keep logical order by phases
- add `[parallel_group:...]` and `[depends_on:...]` only when it is truly safe

## 7) Atomicity and Context Rules for Tasks

Every task must be:
- atomic (single objective)
- executable in isolation
- explicit about required artifact/context in task text when needed

Do not assume executor read neighboring tasks.

Keep shared knowledge in non-task sections (headers, requirements, acceptance criteria), since those sections are considered globally visible.

## 8) Recommended PRD Skeleton

Follow the template in `../assets/PRD.template.md`.

Minimal required shape for markdown:
1. frontmatter block with supported keys
2. title and context sections
3. phase-based checklist using `- [ ]` lines

## 9) JSON PRD Source (Alternative)

When `.json` is used, parser expects array of objects, typically:

```json
[
  { "text": "Task A", "completed": false },
  { "task": "Task B", "completed": true }
]
```

Notes:
- `text` preferred; `task` accepted fallback.
- Frontmatter is not used in JSON mode.