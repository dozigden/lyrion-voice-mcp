# Implemented MCP Contracts

This documents the currently implemented public tools. It does not limit the server to these tools as player, queue, browse, and library support expands.

## Tool flow

1. `search` returns ranked media candidates.
2. `get_player_status` discovers LMS players and their voice-relevant state.
3. The caller can pass one discovered LMS player ID and an action to `control_player`.
4. The caller can pass one discovered LMS player ID to `get_queue`.
5. The caller can clear that player's queue or pass selected search-result references to `manage_queue` for append or play-next placement.
6. The caller passes one discovered LMS player ID and one or more selected search-result references to `play`.

The current public MCP surface contains `search`, `get_player_status`, `control_player`, `get_queue`, `manage_queue`, and `play`.

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

The raw LMS player ID is passed directly to `control_player`, `get_queue`, `manage_queue`, or `play`; it is not wrapped in an application reference.

## `control_player`

`control_player` accepts an explicit raw LMS player ID and one action: `resume`, `pause`, `stop`, `next`, `previous`, `power_on`, or `power_off`.

The player is resolved before mutation. Playback actions map to LMS's explicit play, pause, stop, and adjacent queue-index commands. Power changes are confirmed before success is returned, and power-on does not automatically resume playback. The implementation refreshes the selected player's status after the command.

The result is the selected player's refreshed full status. Volume, mute, seek, grouping, and queue management are not control actions. Invalid actions and missing players return MCP tool execution errors with `isError: true` rather than protocol errors or validation exceptions.

## `get_queue`

`get_queue` accepts one explicit raw LMS player ID and returns that player's complete current queue, up to LMS's 300-item queue limit.

The response contains the player ID, nullable current LMS queue index, and ordered items. Each item contains its LMS queue index, title, and optional artist, album, and duration. Array order and explicit indices both reflect LMS's queue order; the index identifies the current item and remains useful when LMS omits no entries.

The response does not contain pagination, a duplicated count, queue revisions, search-result references, or internal LMS media IDs. An empty queue has a null current index and an empty item list. A missing player, oversized queue, or incomplete upstream queue response returns a concise MCP tool error rather than partial data.

## `play`

`play` accepts an explicit raw LMS player ID, a non-empty ordered list of opaque search-result references, and either `replace` or `append` placement. `replace` is the default.

The player and every reference are resolved before mutation. Lightweight filtered LMS queries verify that each referenced item still resolves to playable media without materialising whole collections in this server. After successful preflight, the server powers on the target when necessary. A power-on failure must not mutate the queue.

Tracks, artists, albums, and playlists are passed directly to LMS `playlistcontrol` by ID. LMS owns collection expansion and its internal track order. `replace` loads the first reference and adds later references. `append` adds every reference without interrupting active playback, or starts at the first appended item when the player is off or idle. Multiple input references preserve caller order.

The result is the selected player's updated status.

Invalid requests, missing players, and stale or unplayable references return MCP tool execution errors with `isError: true` and a concise corrective message. They are not reported as protocol errors and do not use validation exceptions as application control flow.

## `manage_queue`

`manage_queue` accepts an explicit raw LMS player ID, one action, and optional opaque search-result references. Its actions are `clear`, `append`, and `insert_next`.

`clear` accepts no items and empties the selected player's queue. `append` and `insert_next` require a non-empty ordered item list. They accept the same track, artist, album, and playlist references as `play`; LMS expands collections and preserves their internal ordering. Multiple references preserve caller order, including when they are inserted together as the next media to play.

The player and every supplied reference are resolved before mutation. Addition requests also resolve collection sizes and the current queue length before mutation, and reject the whole request if it would exceed the supported 300-item queue limit. Successful additions mark their search-result correlations as selected.

Queue management does not power on a player, start or pause playback, or otherwise change playback state. It returns only the selected player ID and resulting queue length; callers can use `get_queue` when they need the updated contents. Remove, move, and arbitrary positions are not part of this contract.

Invalid actions or item combinations, missing players, stale references, and requests over the queue limit return concise MCP tool errors with `isError: true`.

## Further surface

Further queue editing, browse, grouping, mixes, ratings and likes, volume or other player settings, and subscriptions are candidates for additional user-facing tools. Ingestion, reindexing, and search diagnostics remain operational concerns rather than public MCP tools.
