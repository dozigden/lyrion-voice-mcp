# Lyrion Music Server Guidance

Read this before changing LMS configuration, JSON-RPC transport, response parsing, player discovery, or playback.

## Boundary

- LMS calls belong in `LyrionVoiceMcp.Lms` and are exposed through interfaces in Abstractions.
- Use LMS JSON-RPC `slim.request` at the configured origin's `/jsonrpc.js` endpoint.
- Accept only an absolute HTTP/HTTPS origin with no path, query, fragment, or credentials as the base URL.
- Authentication is not supported in the initial system.
- Do not bake lab hostnames, addresses, player identifiers, or library paths into product code.
- KST's LMS client is useful reference code, not a runtime or project dependency.

## Implemented configuration, probe, search, and player discovery

- Runtime configuration keys are `LyrionVoiceMcpLms:ServerId`, `LyrionVoiceMcpLms:BaseUrl`, and optional `LyrionVoiceMcpLms:RequestTimeoutSeconds` (default 5, range 1–30).
- Environment variables use .NET's double-underscore form, for example `LyrionVoiceMcpLms__BaseUrl`.
- Development launchers load the ignored `.data/dev/appsettings.local.json`; do not require interactive developers to export LMS variables for normal `dev.sh` use.
- `ServerId` currently labels the configured environment in operational diagnostics. It is not part of public media references.
- Both identity and base URL may be absent for an unconfigured development runtime. If either is supplied, both are required and invalid configuration must fail at startup.
- `ILmsConnectionProbe` sends `serverstatus 0 0`; `/api/lms` exposes its state for the operational UI. This is not an MCP tool.
- `LmsJsonRpcClient` owns JSON-RPC request creation, transport failure mapping, and response-envelope validation for both the probe and search adapter.
- First-pass local search issues the LMS `search` command for artists, albums, and tracks plus a `playlists search:` query. It requests at most 20 results per category and preserves category and LMS result order.
- Player discovery issues `players 0`, then parallel read-only `mode ?` queries so paused and stopped players remain distinguishable. Public player state remains limited to raw LMS ID, name, power, and playback mode.
- Keep `/api/health` independent of LMS reachability.

## Environments

- Development LMS contains artificial media and is suitable for deterministic protocol and mutation tests.
- Live LMS contains the representative library and may be queried for read-only evaluation.
- Playback against discovered live players is allowed only in explicit integration work; never invent a player identifier.
- Each MCP deployment targets one LMS server. Result references contain no server identity and are not supported across deployments.

## Parsing and tests

- Centralise JSON-RPC request creation, HTTP error mapping, value coercion, and response-level validation.
- Page commands rather than assuming a library fits in one response.
- Use fictional music metadata in committed fixtures and diagnostics.
- Unit and service tests use fake HTTP responses. Tests requiring a real LMS must be explicitly labelled integration tests.
