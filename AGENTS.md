# Agent Notes

Read the relevant area guidance before changing that part of the system:

- [AGENTS/Architecture.md](AGENTS/Architecture.md) - project boundaries and dependency direction
- [AGENTS/Catalogue.md](AGENTS/Catalogue.md) - canonical media ingestion and refresh rules
- [AGENTS/CSharpCodingConventions.md](AGENTS/CSharpCodingConventions.md)
- [AGENTS/Development.md](AGENTS/Development.md) - local orchestration, ports, Docker, and build metadata
- [AGENTS/Evaluation.md](AGENTS/Evaluation.md) - private corpus boundary and benchmark rules
- [AGENTS/Frontend.md](AGENTS/Frontend.md)
- [AGENTS/Lyrion.md](AGENTS/Lyrion.md) - LMS transport and environment rules
- [AGENTS/Mcp.md](AGENTS/Mcp.md) - public MCP transport and tool rules
- [AGENTS/Operations.md](AGENTS/Operations.md) - durable jobs, schedules, errors, and MCP call history
- [AGENTS/Search.md](AGENTS/Search.md) - search evolution and observation rules
- [AGENTS/StoryBoardAndSourceControl.md](AGENTS/StoryBoardAndSourceControl.md)
- [AGENTS/Testing.md](AGENTS/Testing.md)

README files are for human-facing usage. Agent execution guidance belongs here or in `AGENTS/*.md`.

## Always-on rules

- Prefer British English in identifiers, comments, documentation, and generated prose unless an external contract dictates spelling.
- When behaviour covered by agent guidance changes, update the relevant guidance in the same work.
- Keep implemented behaviour clearly separated from future plans in documentation.
- This is unauthenticated trusted-LAN software. Do not imply that the service is safe to expose publicly.
- Treat the public MCP surface as evolvable user-facing product functionality. Add well-scoped media and player tools when justified, but do not expose diagnostics, raw LMS commands, or experimental internals as public tools.
- Use the repository validation scripts for normal checks. Avoid ad hoc test commands unless changing those scripts or diagnosing a script failure.
- For direct `dotnet` commands, prefer `-maxcpucount:1 -nodeReuse:false`. In a sandbox, set `NUGET_HTTP_CACHE_PATH` to a writable temporary path.
- Keep shared/global frontend CSS in `LyrionVoiceMcp.Web/src/style.css` or `src/shared/styles`; keep page and component CSS scoped in its Vue component.
- Do not create or switch branches, commit, push, or open a pull request unless the user asks for that source-control action.
- Use the Lyrion MCP BoardOil board (`boardId: 12`). Move a story to In Progress before implementation and only to Done after the user confirms completion.
