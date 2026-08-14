# MCP Guidance

Read this before changing MCP registration, tool schemas, results, or error handling.

## Implemented transport

- Use the official C# SDK package `ModelContextProtocol.AspNetCore` 2.1.x.
- Serve stateless Streamable HTTP at `/mcp`.
- Do not enable legacy SSE, sessions, OAuth, or application authentication without a new architectural decision.
- MCP transport registration belongs in Api; public input/output records belong in Contracts.

## Current public tools

The implemented surface currently contains:

1. `search`
2. `get_player_status`
3. `control_player`
4. `get_queue`
5. `manage_queue`
6. `play`

The first three-tool delivery slice was not a permanent limit. Add cohesive user-facing tools when their contracts and application boundaries are understood. Do not expose health, diagnostics, raw LMS commands, experimental search, or provider administration as MCP tools.

## Tool behaviour

- Keep tool handlers thin and use application services.
- Treat [MCP_CONTRACT.md](../MCP_CONTRACT.md) as the working public contract until implemented schemas replace it.
- Return structured, agent-friendly results and opaque per-candidate result references.
- Propagate cancellation and map expected validation/upstream failures to useful tool errors without leaking stack traces.
- A result reference carries both the candidate correlation and underlying LMS playback identity. These remain separate internal concepts but require no separate public `searchId`.
- Result references are short-lived hand-off values. Do not add a format version or LMS server identity.
- `get_player_status` returns every discovered player with full voice-relevant power, mode, volume, optional mute, and now-playing state. It deliberately excludes queue, connectivity, and grouping information.
- `get_player_status`, `control_player`, `get_queue`, `manage_queue`, and `play` use the raw LMS player ID; do not wrap it in an application reference.
- `control_player` accepts exactly one lowercase action: `resume`, `pause`, `stop`, `next`, `previous`, `power_on`, or `power_off`. It excludes volume, mute, seek, grouping, and queue operations and returns the selected player's refreshed full status.
- Register `PlayerControlTools` through `PlayerControlToolRegistration` so malformed enum values become corrective tool errors while the generated schema retains the agreed lowercase action enum.
- `get_queue` takes one player and returns its complete queue up to the LMS 300-item limit. Return only the player ID, nullable current LMS index, and ordered items with LMS index plus title, optional artist, album, and duration. Do not expose pagination, queue revisions, search-result references, or LMS media IDs.
- `manage_queue` takes one player, `clear`, `append`, or `insert_next`, and optional search-result references. `clear` accepts no items; additions require at least one item, preserve caller order, and must preflight every reference and the 300-item application limit before mutation. It returns only the player ID and resulting queue length and must not change power or playback state.
- Register `QueueManagementTools` through `QueueManagementToolRegistration` so malformed enum values become corrective tool errors while the generated schema retains the agreed lowercase action enum.
- `play` accepts a non-empty ordered reference list, replaces the queue, powers on when required, starts playback, and returns the selected player's updated full status. Append and play-next behaviour belong to `manage_queue`; do not add a placement mode to `play`.
- Register `PlaybackTools` normally through the SDK; it no longer requires an enum-binding workaround.
- Model expected search, player-control, queue-management, and playback validation or business rejection as application outcomes, not exceptions. Tools map rejections to `CallToolResult` with `IsError = true`; keep `OutputSchemaType` set to their successful response contracts.
- Tool contracts must survive replacing LMS pass-through search with an indexed resolver.
