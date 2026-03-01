---
name: ralph-loop-prd-generator
description: This skill should be used when creating or rewriting PRD files for Ralph Loop CLI from a scenario, ensuring parser-compatible syntax, atomic tasks, correct dependency ordering, and optional parallel grouping decisions.
---

# Ralph Loop PRD Generator

Generate a new `PRD.md` for Ralph Loop using the exact syntax expected by this repository.

## Trigger

Use this skill when asked to:
- create a new PRD for a project/scenario
- transform requirements into a Ralph Loop execution plan
- rewrite an existing PRD with better task granularity, dependency order, or parallel grouping

## Required Inputs

Collect or infer the following before writing the PRD:
- project goal/outcome
- functional and non-functional requirements
- stack and runtime constraints
- test/lint/browser validation strategy
- preferred engine/model (if not specified and there is already some source code on the current directory, try following the same stack/engine/model or ask/suggest a better one)

When opportunities for safe parallelization exist, ask explicitly whether to:
- keep everything sequential, or
- enable parallel groups/dependencies.

If user does not answer, default to sequential execution.

## Authoring Workflow

1. Read [`references/prd-spec.md`](./references/prd-spec.md) to enforce exact supported keys and annotations.
2. Use [`assets/PRD.template.md`](./assets/PRD.template.md) as base structure.
3. Build execution phases ordered top-to-bottom by real dependency.
4. Write tasks as atomic units.
5. Make each task self-contained:
- include enough local context so the task can be executed in isolation
- avoid relying on prior/future task text
- when task needs a prior artifact, state explicit prerequisite in the same line (or via `depends_on` when using parallel mode)
6. Keep non-task shared guidance in headers/sections (requirements, acceptance criteria, assumptions); do not duplicate this shared context inside every task.
7. Validate syntax and compatibility against parser rules before finalizing.

## Task Writing Rules

Apply all rules:
- Use markdown checkbox task lines: `- [ ] ...`
- Keep one objective per task
- Split complex work into smaller sequential tasks
- Place prerequisite tasks earlier in file
- Use concise wording but include enough execution context
- Avoid ambiguous references like "as above" or "from previous task"
- Preserve parser-friendly inline annotations only when needed: `[parallel_group:...]` and `[depends_on:...]`

## Output Contract

When generating PRD output:
- produce full file content, ready to save as `PRD.md`
- preserve frontmatter + sections + task checklist structure
- ensure tasks are executable by Ralph Loop in top-to-bottom order
- ensure values are limited to supported keys and annotation formats documented in `references/prd-spec.md`

## Final Validation Checklist

Before returning PRD content, verify:
- frontmatter keys are supported
- all task lines match `- [ ]`/`- [x]` syntax
- dependency order is coherent
- parallel annotations (if present) use valid format
- no task requires hidden context unavailable in the task line + shared headers