# Initial MCP Contract

This is the agreed working boundary for the first three public tools. `search` and `get_player_status` are implemented; `play` remains planned.

## Tool flow

1. `search` returns ranked media candidates.
2. `get_player_status` discovers LMS players and their basic state.
3. The caller passes one discovered LMS player ID and one or more selected search-result references to `play`.

The public MCP surface contains exactly `search`, `get_player_status`, and `play`.

## Search-result references

Every candidate returned by `search` has one opaque result reference which the caller passes back unchanged.

That single reference combines:

- correlation with the particular returned candidate, so a later `play` identifies which result was selected;
- the underlying LMS media kind and identity required for playback.

There is no separate public `searchId`. The same LMS item returned by two searches receives two distinct references, one for each candidate occurrence.

The reference contains enough LMS identity for `play` without requiring a transient search-log lookup. It is a short-lived hand-off value with no format version or LMS server identity. A deployment targets one configured LMS server, and carrying references between deployments is unsupported.

Its exact encoding is private implementation detail and remains undecided.

## `search`

`search` resolves voice-derived text into ordered media candidates.

The implemented first-pass input consists only of a required query. It searches the whole configured LMS library and passes the query through to LMS.

Each ordered result carries its opaque candidate reference, media kind, and display information. An empty list represents no match. LMS artist, album, and track results retain their category order, followed by matching playlists.

The first-pass LMS pass-through does not invent a confidence rating which LMS cannot support. The server does not silently select or play a result.

Provider and collection scopes, kind filters, caller-selected result limits, match evidence, explicit rank, and public timing are not part of the first-pass contract. Observation timing and other diagnostic evidence remain internal concerns.

Confidence may be reconsidered later alongside indexed search and ranking. Precise property names and validation errors have not yet been agreed.

## `get_player_status`

The implemented `get_player_status` takes no input and returns all players discovered from the configured LMS.

First-pass status contains only the raw LMS player ID, friendly name, power state, and playback state.

The raw LMS player ID is passed directly to `play`; it is not wrapped in an application reference.

## `play`

`play` accepts an explicit raw LMS player ID, a non-empty ordered list of opaque search-result references, and either `replace` or `append` placement. `replace` is the default.

The player and every reference are resolved before mutation. After successful preflight, the server powers on the target when necessary. A power-on failure must not mutate the queue.

`replace` replaces the queue and starts the first playable item. `append` adds items without interrupting active playback, or starts playback when the player is off or idle. Playable collections expand in provider order, while multiple input references preserve caller order.

The result is the selected player's updated first-pass status.

The precise error shape remains undecided.

## Deferred surface

Arbitrary queue editing, browse, grouping, mixes, ratings and likes, standalone power or player settings, subscriptions, ingestion, reindexing, and search diagnostics are not initial public tools.
