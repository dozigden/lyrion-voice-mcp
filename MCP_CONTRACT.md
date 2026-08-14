# Implemented MCP Contracts

This documents the currently implemented public tools. It does not limit the server to these tools as player, queue, browse, and library support expands.

## Tool flow

1. `search` returns ranked media candidates.
2. `browse` returns local-library roots or descends through an opaque reference returned by an earlier browse call.
3. `get_player_status` discovers LMS players and their voice-relevant state.
4. The caller can pass one discovered LMS player ID and an action to `control_player`.
5. The caller can pass one discovered LMS player ID to `get_queue`.
6. The caller can clear that player's queue or pass playable search or browse references to `manage_queue` for append or play-next placement.
7. The caller passes one discovered LMS player ID and one or more playable search or browse references to `play`.

The current public MCP surface contains `search`, `browse`, `get_player_status`, `control_player`, `get_queue`, `manage_queue`, and `play`.

During MCP initialisation, the server supplies concise agent guidance connecting these tools: discover player IDs rather than inventing them, choose search for named media and browse for exploration, keep references opaque, route search and browse references according to their actual capabilities, distinguish replace-and-start playback from queue addition and clearing, and ask when player or media selection is genuinely ambiguous.

## Result references

Every candidate returned by `search` has one opaque result reference which the caller passes back unchanged.

That single reference combines:

- correlation with the particular returned candidate, so a later `play` identifies which result was selected;
- the underlying LMS media kind and identity required for playback.

There is no separate public `searchId`. The same LMS item returned by two searches receives two distinct references, one for each candidate occurrence.

The reference contains enough LMS identity for `play` without requiring a transient search-log lookup. It is a short-lived hand-off value with no format version or LMS server identity. A deployment targets one configured LMS server, and carrying references between deployments is unsupported.

Its exact encoding is a private implementation detail.

Every item returned by `browse` also has one opaque reference. That reference can carry browse navigation, playback identity, or both. The caller passes the same reference to `browse` when `browsable` is true and to `play` or `manage_queue` when `playable` is true. Pure browse references have no search correlation. When browse starts from a search result, descendants preserve that real candidate correlation so eventual playback or queue addition marks the originating search result selected; browsing alone does not.

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

Playing or queueing an album-artist item retains LMS's album-artist role constraint. It therefore selects the same album-artist catalogue represented by that browse item rather than every track on which the contributor has any role. Ordinary artist items remain unrestricted.

The implemented first pass excludes LMS plugins and providers, virtual-library selection, subscriptions, and player-dependent browsing. Invalid references and attempts to browse a playable-only item return MCP tool execution errors with `isError: true`.

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

`play` accepts an explicit raw LMS player ID and a non-empty ordered list of opaque playable references returned by search or browse. It always replaces the current queue and starts playback.

The player and every reference are resolved before mutation. Lightweight filtered LMS queries verify that each referenced item still resolves to playable media without materialising whole collections in this server. After successful preflight, the server powers on the target when necessary. A power-on failure must not mutate the queue.

Tracks, artists, albums, and playlists are passed directly to LMS `playlistcontrol` by ID. LMS owns collection expansion and its internal track order. The first reference replaces the queue and starts playback; later references are added in caller order. Appending and play-next placement belong to `manage_queue` rather than `play`.

The result is the selected player's updated status.

Invalid requests, missing players, and stale or unplayable references return MCP tool execution errors with `isError: true` and a concise corrective message. They are not reported as protocol errors and do not use validation exceptions as application control flow.

## `manage_queue`

`manage_queue` accepts an explicit raw LMS player ID, one action, and optional opaque playable references returned by search or browse. Its actions are `clear`, `append`, and `insert_next`.

`clear` accepts no items and empties the selected player's queue. `append` and `insert_next` require a non-empty ordered item list. They accept the same track, artist, album, and playlist references as `play`; LMS expands collections and preserves their internal ordering. Multiple references preserve caller order, including when they are inserted together as the next media to play.

The player and every supplied reference are resolved before mutation. Addition requests also resolve collection sizes and the current queue length before mutation, and reject the whole request if it would exceed the supported 300-item queue limit. Successful additions mark search-result correlations as selected when the references originated from search; browse references do not create search correlations.

Queue append and insert-next do not power on a player or change its playback state. Clear uses LMS's native queue clear behaviour, which empties the queue and stops playback. Queue management returns only the selected player ID and resulting queue length; callers can use `get_queue` when they need the updated contents. Remove, move, and arbitrary positions are not part of this contract.

Invalid actions or item combinations, missing players, stale references, and requests over the queue limit return concise MCP tool errors with `isError: true`.

## Further surface

Further queue editing, provider and plugin browsing, grouping, mixes, ratings and likes, volume or other player settings, and subscriptions are candidates for additional user-facing tools. Ingestion, reindexing, and search diagnostics remain operational concerns rather than public MCP tools.
