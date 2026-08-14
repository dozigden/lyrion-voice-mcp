# Implemented MCP Contracts

This documents the three currently implemented public tools. It does not limit the server to these tools as player, queue, browse, and library support expands.

## Tool flow

1. `search` returns ranked media candidates.
2. `get_player_status` discovers LMS players and their basic state.
3. The caller passes one discovered LMS player ID and one or more selected search-result references to `play`.

The current public MCP surface contains `search`, `get_player_status`, and `play`.

## Search-result references

Every candidate returned by `search` has one opaque result reference which the caller passes back unchanged.

That single reference combines:

- correlation with the particular returned candidate, so a later `play` identifies which result was selected;
- the underlying LMS media kind and identity required for playback.

There is no separate public `searchId`. The same LMS item returned by two searches receives two distinct references, one for each candidate occurrence.

The reference contains enough LMS identity for `play` without requiring a transient search-log lookup. It is a short-lived hand-off value with no format version or LMS server identity. A deployment targets one configured LMS server, and carrying references between deployments is unsupported.

Its exact encoding is a private implementation detail.

## `search`

`search` resolves voice-derived text into ordered media candidates.

The implemented first-pass input consists only of a required query. It searches the whole configured LMS library and passes the query through to LMS.

Each ordered result carries its opaque candidate reference, media kind, and display information. An empty list represents no match. LMS artist, album, and track results retain their category order, followed by matching playlists.

The first-pass LMS pass-through does not invent a confidence rating which LMS cannot support. The server does not silently select or play a result.

Provider and collection scopes, kind filters, caller-selected result limits, match evidence, explicit rank, and public timing are not part of the first-pass contract. Observation timing and other diagnostic evidence remain internal concerns.

Confidence may be reconsidered later alongside indexed search and ranking. An invalid query returns an MCP tool execution error with `isError: true`, rather than a protocol error or validation exception. Precise property names beyond the implemented first pass have not yet been agreed.

## `get_player_status`

The implemented `get_player_status` takes no input and returns all players discovered from the configured LMS.

Each player contains the raw LMS player ID, friendly name, power state, playback mode, nullable volume, nullable mute state, and nullable now-playing details. Now-playing details contain title plus optional artist, album, duration, and elapsed time. Queue, connectivity, and grouping information are excluded.

The raw LMS player ID is passed directly to `play`; it is not wrapped in an application reference.

## `play`

`play` accepts an explicit raw LMS player ID, a non-empty ordered list of opaque search-result references, and either `replace` or `append` placement. `replace` is the default.

The player and every reference are resolved before mutation. Lightweight filtered LMS queries verify that each referenced item still resolves to playable media without materialising whole collections in this server. After successful preflight, the server powers on the target when necessary. A power-on failure must not mutate the queue.

Tracks, artists, albums, and playlists are passed directly to LMS `playlistcontrol` by ID. LMS owns collection expansion and its internal track order. `replace` loads the first reference and adds later references. `append` adds every reference without interrupting active playback, or starts at the first appended item when the player is off or idle. Multiple input references preserve caller order.

The result is the selected player's updated status.

Invalid requests, missing players, and stale or unplayable references return MCP tool execution errors with `isError: true` and a concise corrective message. They are not reported as protocol errors and do not use validation exceptions as application control flow.

## Further surface

Queue editing, browse, grouping, mixes, ratings and likes, standalone power or player settings, and subscriptions are candidates for additional user-facing tools. Ingestion, reindexing, and search diagnostics remain operational concerns rather than public MCP tools.
