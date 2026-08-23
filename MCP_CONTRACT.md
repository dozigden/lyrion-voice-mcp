# Implemented MCP Contracts

This documents the currently implemented public tools. It does not limit the server to these tools as player, queue, browse, and library support expands.

## Tool flow

1. `search` returns separately grouped artist, album, top-track, varied-track, and playlist candidates with capability-specific references.
2. `browse` returns local-library roots or descends through an opaque `browseRef` returned by search or an earlier browse call.
3. `get_player_status` discovers LMS players and their voice-relevant state.
4. The caller can pass one discovered LMS player ID or exact unique player name and an action to `control_player`.
5. The caller can pass one discovered LMS player ID or exact unique player name to `get_queue`.
6. The caller can clear that player's queue or pass `playRef` values to `manage_queue` for append or play-next placement.
7. The caller passes one discovered LMS player ID or exact unique player name and one or more `playRef` values to `play`.

The current public MCP surface contains `search`, `browse`, `get_player_status`, `control_player`, `get_queue`, `manage_queue`, and `play`.

Structured results conform to their advertised output schemas. Properties that are required but nullable are emitted explicitly as JSON `null` when no value is available rather than being omitted.

During MCP initialisation, the server supplies concise agent guidance connecting these tools: discover players rather than inventing IDs or names, use search for named media and browse for exploration, pass `browseRef` values recursively through the browse tree, pass `playRef` values to playback or queue tools, distinguish exact ratings from inclusive `at_least` ratings, avoid redundant queue additions before `play`, inspect partial batch results, and ask when player or media selection is genuinely ambiguous.

## Result references

Every ordinary candidate returned by `search` has one opaque result handle which the caller passes back unchanged. The response exposes that handle as `browseRef`, `playRef`, or both according to the candidate's capabilities. An album or playlist uses the same handle for both fields. A resolved `exactArtistMatch` instead exposes a dedicated correlated `discographyBrowseRef`.

That single reference combines:

- correlation with the particular returned candidate, so a later `play` identifies which result was selected;
- the underlying LMS media kind and identity required for playback.

There is no separate public `searchId`. The same LMS item returned by two searches receives two distinct references, one for each candidate occurrence.

The reference is a short server-issued handle whose in-memory registry entry retains the candidate correlation and LMS identity needed by `play`; it does not depend on a search-log lookup. Search and browse handles expire 24 hours after issue, share a 10,000-entry oldest-first bound, and are invalidated by application restart. Unknown, altered, expired, or evicted handles are rejected exactly rather than decoded or repaired. A handle has no format version or LMS server identity. A deployment targets one configured LMS server, and carrying references between deployments is unsupported.

Its exact encoding is a private implementation detail.

Every item returned by `browse` can contain a `browseRef`, a `playRef`, or both. A dual-purpose item uses the same opaque handle for both fields. Pure browse handles have no search correlation. When browse starts from a search result, descendants preserve that real candidate correlation so eventual playback or queue addition marks the originating search result selected; browsing alone does not. Artists and album artists are navigation only and never receive a `playRef`.

## `search`

`search` resolves voice-derived text into ordered media candidates.

The input has a required `name` field containing only meaningful artist, album, track, or playlist name text of at most 500 characters and 20 words. Optional `rating` and `ratingMatch` fields must be supplied together. `rating` is a decimal number from 0 to 5; `ratingMatch` is `exact` for exactly that rating or `at_least` for that rating and higher, so rating 4 with `at_least` means 4+. Ratings and rating syntax belong in those separate fields, never in `name`; recognisable misplaced numeric rating syntax returns a corrective tool error. Without a rating constraint, search uses the production catalogue resolver followed by isolated LMS playlist discovery. With one, search returns catalogue tracks only and does not query playlists.

The structured response contains concise recursive-browse guidance, a required nullable `exactArtistMatch`, and five required lists. When present, `exactArtistMatch` contains the resolved artist `name` and a `discographyBrowseRef`; `artists` is then empty. Otherwise `exactArtistMatch` is explicit JSON `null` and `artists` contains unresolved candidates with `name` and `browseRef`. `albums` contain title, nullable artist, `browseRef`, and `playRef`; `topTracks` and `tracks` contain title, nullable artist and album, numeric 0–5 `rating`, and `playRef`; `playlists` contain title, `browseRef`, and `playRef`. Empty lists represent no matches.

Search returns up to 5 artists, 5 albums, 5 top tracks, 30 tracks, and 5 playlists; playlists preserve LMS order. `topTracks` contains relevant tracks rated 4 or above, intersected with any explicit rating constraint. Within an equal-relevance band, higher ratings improve top-track selection odds, but rating is not a strict ordering and eligible four-star tracks retain a chance of appearing. The ordinary `tracks` population still includes highly rated tracks, but the particular selected `topTracks` are removed from `tracks` in the final response. Track ordering preserves relevance score bands, varies candidates within equal-score bands, and spreads albums within a band. These are central internal caps rather than request parameters. Search has no continuation or caller-selected limit.

When the whole query uniquely and exactly identifies an artist, with no equally exact album or track, search draws from that artist's canonical complete track relationship instead of the bounded text-retrieval lanes and populates `exactArtistMatch`. Its `discographyBrowseRef` opens every album where that identity is the album artist, excluding compilations and guest appearances where it occurs only as a track artist. This expansion is streamed through bounded rotating pools. Rating-constrained searches preserve the same exact-artist representation. Duplicate exact artists, fuzzy artist matches, and exact album or track conflicts—including self-titled albums—retain ordinary search behaviour.

Native LMS zero, including a missing rating normalised during import, is public rating `0`; there is no separate unrated value. The native LMS 0–100 value is not public.

Exact matching scales the public value by 20 and requires that exact native integer, so exact `4` means native 80 and a value with no exact native representation matches nothing. At-least matching uses the smallest native integer at or above the scaled public value. Exact `0` selects native zero; at-least `0` selects every track. Rating constraints are applied inside retrieval before lane limits.

The production catalogue resolver does not expose its ranking score as confidence. The server does not silently select or play a result.

Provider and collection scopes, caller-selected result limits, match evidence, explicit rank, and public timing are not part of the current contract. Observation timing and returned category counts remain internal concerns.

Confidence may be reconsidered later alongside ranking calibration. `name` must contain meaningful letter-or-digit media-name text; `*` and other wildcard-only input return a corrective MCP tool error directing rating-only exploration to Browse → Ratings.

## `browse`

`browse` takes one optional opaque `browseRef`. Omitting it returns these fixed local-library roots in order: album artists, artists, albums, genres, playlists, ratings, recently added, and years. Tracks are deliberately not exposed at the root.

Passing a browsable item reference descends through the local library:

- album artists and artists lead to their albums;
- albums lead to tracks in LMS track order;
- genres and years lead to albums, then tracks;
- playlists lead to their tracks in playlist order;
- ratings lead to buckets `0`, `1`, `2`, `3`, `4`, and `5`, then to matching tracks;
- recently added returns albums using LMS's native `sort:new` ordering, then those albums lead to tracks.

Rating buckets floor the public decimal: native 0–19 appears under `0`, 20–39 under `1`, 40–59 under `2`, 60–79 under `3`, 80–99 under `4`, and 100 under `5`. Rating-only 4+ exploration therefore combines buckets 4 and 5. Tracks within a bucket are ordered by native rating descending, then title, artist, album, and stable identity.

Artist, album, and playlist `browseRef` values returned by `search` can enter the same hierarchy directly. Unresolved artist search results lead to their albums; an exact artist's `discographyBrowseRef` leads specifically to every album credited to that album artist. Album results lead to tracks, and playlist results lead to playlist tracks. Track results contain only a `playRef`. Search-derived descendants and continuations retain the originating candidate correlation until a playable result is used.

Pages use an internal 50-item size. The caller cannot select an offset, limit, filter, or sort order. When more results remain, the response contains an opaque `nextBrowseRef` which is passed back to `browse`.

Each item contains `kind`, `title`, optional `artist`, optional `album`, and whichever capability references apply. Categories, album artists, artists, genres, rating buckets, and years contain only `browseRef`; albums and playlists contain both `browseRef` and `playRef`; tracks contain only `playRef`. Tracks returned from a rating bucket also contain their numeric 0–5 `rating`; other browse items omit it. The response also contains generic recursive-browse guidance and nullable `nextBrowseRef`.

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

`play` accepts an explicit raw LMS player ID or exact unique player name and a non-empty ordered list of opaque `playRef` values returned by search or browse. It always replaces the current queue and starts playback. Callers should invoke it directly rather than first appending the same media through `manage_queue`.

The player and every reference are inspected before mutation. Lightweight filtered LMS queries verify that each valid reference still resolves to playable media without materialising whole collections in this server. Invalid or unavailable references are skipped. If none remains, the tool returns an error without changing LMS. After successful preflight, the server powers on the target when necessary. A power-on failure must not mutate the queue.

Tracks, albums, and playlists are passed directly to LMS `playlistcontrol` by ID. Artists and album artists are rejected before any LMS mutation. LMS owns legitimate album and playlist expansion and their internal track order. The first usable reference replaces the queue and starts playback; later usable references are added in relative caller order. Appending and play-next placement belong to `manage_queue` rather than `play`.

The result contains nullable refreshed player status, requested and completed reference counts, indexed skipped items, and a nullable state-refresh error. A normal successful refresh emits a player and an explicit `null` refresh error. Stable skipped-item reasons are `invalid_reference`, `media_unavailable`, `lms_error`, and `not_attempted`; queue-only `queue_capacity` is also part of the shared reason vocabulary.

After mutation begins, the server stops on the first LMS failure rather than risking further changes against an unavailable player or server. If at least one item completed, the tool returns structured partial success and identifies the failed item and every unattempted remainder without repeating successful references. If none is confirmed complete, it returns `isError: true` with the same structured batch shape, zero completed items, and refreshed player state where available; this also covers a failed power-on confirmation because the power command may already have changed the player. Only completed references mark real originating search-result correlations selected; pure root-browse references carry no synthetic correlation.

Invalid requests, missing players, and stale or unplayable references return MCP tool execution errors with `isError: true` and a concise corrective message. They are not reported as protocol errors and do not use validation exceptions as application control flow.

## `manage_queue`

`manage_queue` accepts an explicit raw LMS player ID or exact unique player name, one action, and optional opaque `playRef` values returned by search or browse. Its actions are `clear`, `append`, and `insert_next`.

`clear` accepts no items and empties the selected player's queue. `append` and `insert_next` require a non-empty ordered item list. They accept the same track, album, and playlist references as `play`; artist identities are rejected. LMS expands legitimate collections and preserves their internal ordering. Multiple references preserve caller order, including when they are inserted together as the next media to play.

The player and every supplied reference are inspected before mutation. Addition requests also resolve collection sizes and the current queue length. Invalid and unavailable items are skipped. Capacity is assigned greedily in input order up to the supported 300-item limit: an item that does not fit is reported as `queue_capacity`, while a later smaller item may still fit. If nothing can be added, the tool returns an error without mutation.

Append submits retained items in input order. Insert-next submits them to LMS in reverse so their resulting queue order still matches the caller's relative order. Both stop on the first LMS mutation failure. Once one item completes, a failure returns structured partial success with requested and completed reference counts plus indexed `lms_error` and `not_attempted` entries. With no confirmed completion, the same structure and refreshed queue length are returned with `isError: true`, because an upstream failure does not prove that LMS left the queue unchanged. Only completed references mark originating search-result correlations selected.

Queue append and insert-next do not power on a player or change its playback state. Clear uses LMS's native queue clear behaviour, which empties the queue and stops playback. Queue management returns the canonical player ID, nullable refreshed queue length, requested and completed reference counts, skipped items, and nullable state-refresh error. Clear reports zero requested and completed media items with an empty skipped list. Callers can use `get_queue` when they need updated contents after a failed refresh. Remove, move, and arbitrary positions are not part of this contract.

Invalid actions or item combinations, missing players, and batches with no usable additions return concise plain MCP tool errors. A mutation attempt with no confirmed completion returns the structured error described above. Both use `isError: true`.

## Further surface

Further queue editing, provider and plugin browsing, grouping, mixes, broader compound filtering and likes, volume or other player settings, and subscriptions are candidates for additional user-facing tools. Ingestion, reindexing, and search diagnostics remain operational concerns rather than public MCP tools.
