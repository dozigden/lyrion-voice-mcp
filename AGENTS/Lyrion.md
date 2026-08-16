# Lyrion Music Server Guidance

Read this before changing LMS configuration, JSON-RPC transport, response parsing, player discovery, or playback.

## Boundary

- LMS calls belong in `LyrionVoiceMcp.Lms` and are exposed through interfaces in Abstractions.
- Use LMS JSON-RPC `slim.request` at the configured origin's `/jsonrpc.js` endpoint.
- Accept only an absolute HTTP/HTTPS origin with no path, query, fragment, or credentials as the base URL.
- Authentication is not supported in the initial system.
- Do not bake lab hostnames, addresses, player identifiers, or library paths into product code.
- KST's LMS client is useful reference code, not a runtime or project dependency.

## Implemented configuration, probe, search, browse, player discovery, and playback

- Runtime configuration keys are `LyrionVoiceMcpLms:ServerId`, `LyrionVoiceMcpLms:BaseUrl`, and optional `LyrionVoiceMcpLms:RequestTimeoutSeconds` (default 5, range 1–30).
- Environment variables use .NET's double-underscore form, for example `LyrionVoiceMcpLms__BaseUrl`.
- Development launchers load the ignored `.data/dev/appsettings.local.json`; do not require interactive developers to export LMS variables for normal `dev.sh` use.
- The separate evaluation executable reads only `LVM_EVALUATION_LMS_BASE_URL` and uses a fixed `live-evaluation` identity for LMS pass-through and its local evaluation-catalogue refresh. It must not inherit normal application LMS configuration or fall back to the development LMS.
- `ServerId` currently labels the configured environment in operational diagnostics. It is not part of public media references.
- Both identity and base URL may be absent for an unconfigured development runtime. If either is supplied, both are required and invalid configuration must fail at startup.
- `ILmsConnectionProbe` sends `serverstatus 0 0`; `/api/lms` exposes its state for the operational UI. This is not an MCP tool.
- `LmsJsonRpcClient` owns JSON-RPC request creation, transport failure mapping, and response-envelope validation for LMS adapters.
- `LmsCatalogueReader` sends sequential 500-item `albums`, `genres`, `titles`, `artists`, and virtual-library membership pages directly to `ICatalogueImportWriter`; it does not retain the complete catalogue. The unpaged `libraries` command is read once. LMS's configurable `artists` result is used only as a name lookup, and persistence retains entries referenced by current track `artist_ids` or album `artist_id`. Composer, conductor, band, other contributor-role fields, and unreferenced lookup entries are deliberately ignored. The reader preserves all genre IDs, provider/file metadata, and native rating/play-count values. It checks server status before and after the read and refuses to import during a scan. Manual refresh through `POST /api/catalogue/refresh` persists durable batches and reconciles unseen rows only after a successful read; it is not invoked automatically.
- First-pass local search issues the LMS `search` command for artists, albums, and tracks plus a `playlists search:` query. It requests at most 20 results per category and preserves category and LMS result order.
- Local-library browse uses paged `artists`, `albums`, `genres`, `playlists`, `years`, `titles`, and `playlists tracks` queries. Album artists use `role_id:ALBUMARTIST`; recently added uses `albums sort:new`; album tracks use `sort:tracknum`. Keep the fixed public hierarchy and opaque paging policy in Services rather than leaking LMS commands into MCP tools.
- Player discovery issues `players 0`, then parallel one-item `status - 1` and `mixer muting ?` queries for every player. This returns power, playback mode, volume, optional mute state, and current-media metadata/progress without materialising the queue. Mute is nullable because LMS/player combinations may not expose it.
- Player-targeting application services resolve a supplied raw ID before trying an exact case-insensitive current display-name match. Duplicate names are ambiguous and must not select a player; successful LMS mutations always use the canonical discovered ID.
- Player control uses explicit `play`, `pause 1`, `stop`, `playlist index +1`, and `playlist index -1` commands. Power control sets the requested state and confirms it with `power ?`; power-on uses the `noplay` flag.
- Queue reading uses one `status 0 300 tags:aAld` request after player validation. Preserve each LMS `playlist index`, use top-level `current_title` for the current remote item, and reject responses that exceed 300 items or omit queued entries rather than silently truncating them.
- Playback and queue-management preflight use one-item filtered `titles` or `playlists tracks` queries. Their result counts verify that each referenced LMS item remains playable and let queue additions enforce the application-level 300-item limit without materialising collection contents.
- Submit tracks, artists, albums, and playlists directly to `playlistcontrol` using their LMS IDs. Album-artist selections also pass `role_id:ALBUMARTIST` during preflight and submission so playback matches the browsed role. LMS owns collection expansion and internal ordering.
- Queue management uses `playlist clear`, `playlistcontrol cmd:add`, and `playlistcontrol cmd:insert`. Submit separate play-next references in reverse so LMS's repeated next-position inserts preserve caller order. Do not power on or start playback for queue management.
- Power on with LMS's `noplay` flag, then confirm the state with `power ?` before changing the queue.
- Batched playback loads the first reference, replacing the queue and starting playback, then adds later references in caller order. Append and play-next placement are queue-management operations.
- Keep `/api/health` independent of LMS reachability.

## Environments

- Development LMS contains artificial media and is suitable for deterministic protocol and mutation tests. Any discovered development player may be mutated during integration work, but leave it stopped afterwards.
- Live LMS contains the representative library and may be queried for read-only evaluation.
- Playback against a live player requires explicit approval for the resolved player in the current work; never invent a player identifier.
- The development lab may expose a software player. Treat it as a protocol smoke test; verify surprising state or transport behaviour on an explicitly approved hardware player before generalising it.
- Each MCP deployment targets one LMS server. Result references contain no server identity and are not supported across deployments.

## Parsing and tests

- Centralise JSON-RPC request creation, HTTP error mapping, value coercion, and response-level validation.
- Page commands rather than assuming a library fits in one response.
- Use fictional music metadata in committed fixtures and diagnostics.
- Unit and service tests use fake HTTP responses. Tests requiring a real LMS must be explicitly labelled integration tests.
