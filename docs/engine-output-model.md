# Engine Stream-JSON Output Model

This document defines how Ralph adapts stream-json outputs for UI display while preserving raw stdout.

## Design goals
1. Keep raw engine output untouched for logs/audit.
2. Convert only user-relevant content to display lines.
3. Keep engine-specific parsing isolated by adapter class.
4. Avoid throwing on unknown/malformed events.

## Pipeline
1. Engine writes raw stdout.
2. `EngineOutputDisplayAdapterRegistry` selects adapter by engine name.
3. Adapter converts raw json lines to display lines.
4. UI shows adapted lines.
5. Token panel is fed by `TokenUsageParser` from raw stdout/stderr.

## Gemini model (observed in local captures)
Observed event examples:
- `{"type":"init", ...}`
- `{"type":"message","role":"user|assistant","content":"...","delta":true}`
- `{"type":"tool_use","tool_name":"...","parameters":{...}}`
- `{"type":"tool_result","status":"success|error"}`
- `{"type":"result","status":"success","stats":{"total_tokens":...,"input_tokens":...,"output_tokens":...}}`

Display mapping:
1. `message/content` from `assistant` -> appended and coalesced.
2. `tool_use` -> `[tool] <tool_name>` line.
3. `tool_result` -> `[tool:<status>]` line.
4. Thinking/reasoning markers are ignored.
5. `result.stats.*tokens` are NOT rendered in output panel; they belong to token panel.

## Cursor model (event-oriented + fallback)
Supported event structure (normalized):
- `type`: `message|content|tool_use|tool_result|result|...`
- `role`: expected `assistant` for display content
- text fields: `content|text|message|delta` (including nested content arrays)
- tool metadata: `tool_name|status`

Display mapping:
1. `message/content` from assistant -> coalesced display text.
2. `tool_use` -> `[tool] <tool_name>`.
3. `tool_result` -> `[tool:<status>]`.
4. Unknown event types fall back to extracted text if present.

## Compatibility rules
1. Adapters must be tolerant to missing keys.
2. Unknown event types must not fail the run loop.
3. Any adapter failure falls back to raw stdout display.
4. i18n applies to deterministic product messages; raw/adapted model text is engine content and not translated.

## Files
- `src/Ralph.Core/RunLoop/OutputAdapters/EngineOutputDisplayAdapterRegistry.cs`
- `src/Ralph.Core/RunLoop/OutputAdapters/GeminiStreamJsonOutputAdapter.cs`
- `src/Ralph.Core/RunLoop/OutputAdapters/CursorStreamJsonOutputAdapter.cs`
- `src/Ralph.Core/RunLoop/RunLoopService.cs`
