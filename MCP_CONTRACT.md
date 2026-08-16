# Implemented MCP Contracts

This documents the currently implemented public tools. It does not limit the server to these tools as player, queue, browse, and library support expands.

## Tool flow

1. `search` returns ranked media candidates.
2. `browse` returns local-library roots or descends through an opaque reference returned by an earlier browse call.
3. `get_player_status` discovers LMS players and their voice-relevant state.
4. The caller can pass one discovered LMS player ID or exact unique player name and an action to `control_player`.
5. The caller can pass one discovered LMS player ID or exact unique player name to `get_queue`.
6. The caller can clear that player's queue or pass playable search or browse references to `manage_queue` for append or play-next placement.
7. The caller passes one discovered LMS player ID or exact unique player name and one or more playable search or browse references to `play`.

The current public MCP surface contains `search`, `browse`, `get_player_status`, `control_player`, `get_queue`, `manage_queue`, and `play`.

Structured results conform to their advertised output schemas. Properties that are required but nullable are emitted explicitly as JSON `null` when no value is available rather than being omitted.

During MCP initialisation, the server supplies concise agent guidance connecting these tools: discover players rather than inventing IDs or names, use a returned raw ID or exact unique name, choose search for named media and browse for exploration, keep references opaque, route search and browse references according to their actual capabilities, distinguish replace-and-start playback from queue addition and clearing, inspect partial batch results, and ask when player or media selection is genuinely ambiguous.

## Result references

Every candidate returned by `search` has one opaque result reference which the caller passes back unchanged.

That single reference combines:

- correlation with the particular returned candidate, so a later `play` identifies which result was selected;
- the underlying LMS media kind and identity required for playback.

There is no separate public `searchId`. The same LMS item returned by two searches receives two distinct references, one for each candidate occurrence.

The reference is a short server-issued handle whose in-memory registry entry retains the candidate correlation and LMS identity needed by `play`; it does not depend on a search-log lookup. Search and browse handles expire 24 hours after issue, share a 10,000-entry oldest-first bound, and are invalidated by application restart. Unknown, altered, expired, or evicted handles are rejected exactly rather than decoded or repaired. A handle has no format version or LMS server identity. A deployment targets one configured LMS server, and carrying references between deployments is unsupported.

Its exact encoding is a private implementation detail.

Every item returned by `browse` also has one opaque server-issued handle. Its registry entry can retain browse navigation, playback identity, or both. The caller passes the same handle to `browse` when `browsable` is true and to `play` or `manage_queue` when `playable` is true. Pure browse handles have no search correlation. When browse starts from a search result, descendants preserve that real candidate correlation so eventual playback or queue addition marks the originating search result selected; browsing alone does not.

## `search`

`search` resolves voice-derived text into ordered media candidates.

The implemented first-pass input consists only of a required query. It searches the whole configured LMS library and passes the query through to LMS.

Each ordered result carries its opaque candidate reference, media kind, and display information. An empty list represents no match. LMS artist, album, and track results retain their category order, followed by matching playlists.

The first-pass LMS pass-through does not invent a confidence rating which LMS cannot support. The server does not silently select or play a result.

Provider and collection scopes, kind filters, caller-selected result limits, match evidence, explicit rank, and public timing are not part of the first-pass contract. Observation timing and other diagnostic evidence remain internal concerns.

Confidence may be reconsidered later alongside indexed search and ranking. An invalid query returns an MCP tool execution error with `isError: true`, rather than a protocol error or validation exception. Precise property names beyond the implemented first pass have not yet been agreed.

## `browse`

`browse` takes one optional opaque search or browse reference. Omitting it returns these fixed local-library roots in order: album artists, artists, albums, genres, playlists, recently added, and years. Tracks are deliberately not exposed at the root.

Passing a browsable item reference descends through the local library:

- album artists and artists lead to their albums;
- albums lead to tracks in LMS track order;
- genres and years lead to albums, then tracks;
- playlists lead to their tracks in playlist order;
- recently added returns albums using LMS's native `sort:new` ordering, then those albums lead to tracks.

Artist, album, and playlist references returned by `search` can enter the same hierarchy directly. Artist search results lead to albums, album results lead to tracks, and playlist results lead to playlist tracks. Track search results are playable but not browsable. Search-derived descendants and continuations retain the originating candidate correlation until a playable result is used.

Pages use an internal 50-item size. The caller cannot select an offset, limit, filter, or sort order. When more results remain, the response contains an opaque `continuation` which is passed back as the next browse reference.

Each item contains only `reference`, `kind`, `title`, optional `artist`, optional `album`, `browsable`, and `playable`. The response contains `items` and nullable `continuation`. Album-artist, artist, album, playlist, and track items are playable; genres and years are navigation only. Tracks are playable but not browsable.

Playing or queueing an album-artist item retains LMS's album-artist selection constraint. It therefore selects the same album-artist catalogue represented by that browse item. This is a narrow LMS query scope, not a general contributor-role model; ordinary artist items remain unrestricted.

The implemented first pass excludes LMS plugins and providers, virtual-library selection, subscriptions, and player-dependent browsing. Invalid references and attempts to browse a playable-only item return MCP tool execution errors with `isError: true`.

## `get_player_status`

The implemented `get_player_status` takes no input and returns all players discovered from the configured LMS.

Each player contains the raw LMS player ID, friendly name, power state, playback mode, nullable volume, nullable mute state, and nullable now-playing details. Now-playing details contain title plus optional artist, album, duration, and elapsed time. Queue, connectivity, and grouping information are excluded.

A raw LMS player ID or exact unique player name returned by `get_player_status` can be passed directly to `control_player`, `get_queue`, `manage_queue`, or `play`; it is not wrapped in an application reference. IDs take precedence over names. Names are trimmed and compared exactly without regard to case. Unknown or duplicate names return an actionable tool error rather than guessing. Successful responses identify the selected player by its canonical raw LMS ID.

## `control_player`

`control_player` accepts an explicit raw LMS player ID or exact unique player name and one action: `resume`, `pause`, `stop`, `next`, `previous`, `power_on`, or `power_off`.

The player is resolved before mutation. Playback actions map to LMS's explicit play, pause, stop, and adjacent queue-index commands. Power changes are confirmed before success is returned, and power-on does not automatically resume playback. The implementation refreshes the selected player's status after the command.

The result is the selected player's refreshed full status. Volume, mute, seek, grouping, and queue management are not control actions. Invalid actions and missing players return MCP tool execution errors with `isError: true` rather than protocol errors or validation exceptions.

## `get_queue`

`get_queue` accepts one explicit raw LMS player ID or exact unique player name and returns that player's complete current queue, up to LMS's 300-item queue limit.

The response contains the player ID, nullable current LMS queue index, and ordered items. Each item contains its LMS queue index, title, and optional artist, album, and duration. Array order and explicit indices both reflect LMS's queue order; the index identifies the current item and remains useful when LMS omits no entries.

The response does not contain pagination, a duplicated count, queue revisions, search-result references, or internal LMS media IDs. An empty queue has a null current index and an empty item list. A missing player, oversized queue, or incomplete upstream queue response returns a concise MCP tool error rather than partial data.

## `play`

`play` accepts an explicit raw LMS player ID or exact unique player name and a non-empty ordered list of opaque playable references returned by search or browse. It always replaces the current queue and starts playback.

The player and every reference are inspected before mutation. Lightweight filtered LMS queries verify that each valid reference still resolves to playable media without materialising whole collections in this server. Invalid or unavailable references are skipped. If none remains, the tool returns an error without changing LMS. After successful preflight, the server powers on the target when necessary. A power-on failure must not mutate the queue.

Tracks, artists, albums, and playlists are passed directly to LMS `playlistcontrol` by ID. LMS owns collection expansion and its internal track order. The first usable reference replaces the queue and starts playback; later usable references are added in relative caller order. Appending and play-next placement belong to `manage_queue` rather than `play`.

The result contains nullable refreshed player status, requested and completed reference counts, indexed skipped items, and a nullable state-refresh error. A normal successful refresh emits a player and an explicit `null` refresh error. Stable skipped-item reasons are `invalid_reference`, `media_unavailable`, `lms_error`, and `not_attempted`; queue-only `queue_capacity` is also part of the shared reason vocabulary.

After mutation begins, the server stops on the first LMS failure rather than risking further changes against an unavailable player or server. If at least one item completed, the tool returns structured partial success and identifies the failed item and every unattempted remainder without repeating successful references. If none is confirmed complete, it returns `isError: true` with the same structured batch shape, zero completed items, and refreshed player state where available; this also covers a failed power-on confirmation because the power command may already have changed the player. Only completed references mark real originating search-result correlations selected; pure root-browse references carry no synthetic correlation.

Invalid requests, missing players, and stale or unplayable references return MCP tool execution errors with `isError: true` and a concise corrective message. They are not reported as protocol errors and do not use validation exceptions as application control flow.

## `manage_queue`

`manage_queue` accepts an explicit raw LMS player ID or exact unique player name, one action, and optional opaque playable references returned by search or browse. Its actions are `clear`, `append`, and `insert_next`.

`clear` accepts no items and empties the selected player's queue. `append` and `insert_next` require a non-empty ordered item list. They accept the same track, artist, album, and playlist references as `play`; LMS expands collections and preserves their internal ordering. Multiple references preserve caller order, including when they are inserted together as the next media to play.

The player and every supplied reference are inspected before mutation. Addition requests also resolve collection sizes and the current queue length. Invalid and unavailable items are skipped. Capacity is assigned greedily in input order up to the supported 300-item limit: an item that does not fit is reported as `queue_capacity`, while a later smaller item may still fit. If nothing can be added, the tool returns an error without mutation.

Append submits retained items in input order. Insert-next submits them to LMS in reverse so their resulting queue order still matches the caller's relative order. Both stop on the first LMS mutation failure. Once one item completes, a failure returns structured partial success with requested and completed reference counts plus indexed `lms_error` and `not_attempted` entries. With no confirmed completion, the same structure and refreshed queue length are returned with `isError: true`, because an upstream failure does not prove that LMS left the queue unchanged. Only completed references mark originating search-result correlations selected.

Queue append and insert-next do not power on a player or change its playback state. Clear uses LMS's native queue clear behaviour, which empties the queue and stops playback. Queue management returns the canonical player ID, nullable refreshed queue length, requested and completed reference counts, skipped items, and nullable state-refresh error. Clear reports zero requested and completed media items with an empty skipped list. Callers can use `get_queue` when they need updated contents after a failed refresh. Remove, move, and arbitrary positions are not part of this contract.

Invalid actions or item combinations, missing players, and batches with no usable additions return concise plain MCP tool errors. A mutation attempt with no confirmed completion returns the structured error described above. Both use `isError: true`.

## Further surface

Further queue editing, provider and plugin browsing, grouping, mixes, ratings and likes, volume or other player settings, and subscriptions are candidates for additional user-facing tools. Ingestion, reindexing, and search diagnostics remain operational concerns rather than public MCP tools.
