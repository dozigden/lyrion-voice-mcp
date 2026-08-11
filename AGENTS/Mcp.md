# MCP Guidance

Read this before changing MCP registration, tool schemas, results, or error handling.

## Implemented transport

- Use the official C# SDK package `ModelContextProtocol.AspNetCore` 2.1.x.
- Serve stateless Streamable HTTP at `/mcp`.
- Do not enable legacy SSE, sessions, OAuth, or application authentication without a new architectural decision.
- MCP transport registration belongs in Api; public input/output records belong in Contracts.

## Public tool boundary

The intended initial surface is exactly:

1. `search`
2. `get_player_status`
3. `play`

The skeleton intentionally exposes no tools. Add the three tools only through their implementation stories. Do not expose health, diagnostics, raw LMS commands, experimental search, or provider administration as MCP tools.

## Tool behaviour

- Keep tool handlers thin and use application services.
- Treat [MCP_CONTRACT.md](../MCP_CONTRACT.md) as the working public contract until implemented schemas replace it.
- Return structured, agent-friendly results and opaque per-candidate result references.
- Propagate cancellation and map expected validation/upstream failures to useful tool errors without leaking stack traces.
- A result reference carries both the candidate correlation and underlying LMS playback identity. These remain separate internal concepts but require no separate public `searchId`.
- Result references are short-lived hand-off values. Do not add a format version or LMS server identity.
- `get_player_status` and `play` use the raw LMS player ID; do not wrap it in an application reference.
- Tool contracts must survive replacing LMS pass-through search with an indexed resolver.
