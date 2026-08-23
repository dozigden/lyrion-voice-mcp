# LMS Catalogue Ingestion Reconnaissance

Research date: 2026-08-15

## Outcome

A reliable first catalogue slice can be built entirely through supported LMS JSON-RPC queries. It must still reconcile the complete LMS library because the available timestamps do not form a reliable incremental feed, but it should stream LMS pages into durable, idempotent storage batches rather than materialising or atomically replacing a complete snapshot.

This reconnaissance does not select SQLite or any search engine. It defines the information boundary that both must consume later.

## Implementation status

The first executable slice now provides:

- typed, storage-neutral import records in Abstractions for artists, albums, genres, tracks, artist and genre relationships, source-provenanced statistics, virtual libraries, and source freshness;
- `ICatalogueSourceReader` and `ICatalogueImportWriter` page/batch boundaries, with a separate SQLite adapter implementing durable upsert and seen-row reconciliation;
- a sequential 500-item LMS reader with command-specific parsing, initial/final scan-state checks, stable per-command counts, bounded memory, and persisted progress/referential warnings; and
- fictional automated coverage for field coercion, main and album artists, deliberate exclusion of other LMS person-role fields, multi-genre membership, native statistics, virtual-library membership, paging boundaries, convergence, schema rebuild, scan refusal, and rating validation.

Read-only integration runs validated both the earlier broad-role snapshot and the bounded writer implementation at household-library scale. They confirmed that a complete reconciliation is practical, that the broad LMS artist lookup must be filtered to referenced main and album artists, and that overlapping virtual-library memberships converge without requiring a full in-memory snapshot. Exact private-library measurements are deliberately not retained here.

Catalogue pages are now persisted durably through a manual background refresh. Unseen rows are removed only after all LMS reads and the final stability check succeed. Failed batches remain safe to retry and no catalogue generation is created. Canonical application ID reconciliation, playlists, catalogue queries, and search-index generation remain unimplemented; refresh is deliberately not automatic.

## Evidence gathered

Read-only probes confirmed the behaviours that shape the importer:

- the broad LMS artist lookup includes entries that are not referenced as main or album artists;
- ratings and native play counts are sparse optional values;
- virtual-library membership is many-to-many and overlapping;
- tracks can have multiple genres; and
- playlist entries and optional works/provider data require capability-aware handling.

Paged reads established that a complete reconciliation is practical at household-library scale. That is not a production page-size or refresh-frequency decision.

LMS exposes IDs for main artists and several other person roles. The product decision is to retain only main track artists and album artists; composer, conductor, band, and other role relationships are not catalogue requirements. LMS supports works and performances, and the model leaves room for them independently of person-role ingestion.

No private media names, paths, LMS address, virtual-library names, or identifiers belong in this document.

## Supported LMS surface

The importer can use these read-only command families:

| Data | Command shape | Important observations |
|---|---|---|
| Scan state and totals | `serverstatus 0 0` | Supplies `lastscan`, version, scan state, and aggregate counts. `lastscan` is not changed by every statistic or plugin update. |
| Artist-name lookup | paged `artists` | Returns IDs and names for LMS's configurable artist-role view. The catalogue retains only entries referenced by track `artist_ids` or album `artist_id`. Full sort names are used internally by LMS but are not returned by this command. |
| Albums | paged `albums` | Returns album IDs, titles, album artist, year, artwork locator, compilation/release metadata when requested. Full sort titles are not exposed. |
| Tracks | paged `titles` | Returns stable-in-the-current-catalogue IDs plus metadata tags, rating, native play count, main artist IDs, and all genre IDs. |
| Genres | paged `genres` | Resolves genre IDs to names. |
| Virtual libraries | `libraries`, then paged `titles library_id:{id}` | `libraries` returns `folder_loop` and may omit `count`. Membership is many-to-many and can be dynamic. `tags:II` is an efficient membership-only query when tracks are already known. |
| Playlists | paged `playlists`, then paged `playlists tracks playlist_id:{id}` | Playlist membership is ordered through `playlist index`; entries can include non-local provider URLs. |
| Works | paged `works` | Supported by LMS for composition/performance data but not universally populated. Treat as an optional later capability. |

The proposed title query should request typed scalar metadata plus:

- tag `S` exposes role-specific LMS ID fields; ingestion reads `artist_ids` and ignores composer, conductor, band, and other person-role fields;
- tag `P` for every genre ID;
- tags `R` and `O` for native rating and play count;
- tags for URL, remote state, external identity, album ID, disc/track order, duration, dates, release type, compilation, work/performance, and subtitle where supported.

Artist names should come from the broad artist lookup and be joined by ID, but only referenced main and album artists belong in the canonical catalogue. Albums and tracks are persisted first, allowing each later artist-lookup page to be filtered against relationships already in SQLite without retaining every track identity in memory. LMS serialises multiple names as comma-separated text and its own source notes the ambiguity when a name contains a comma; numeric artist IDs do not have that problem. Similarly, the ordinary single-genre track tag can select an arbitrary genre, so ingestion must use the multi-value genre ID tag.

Primary LMS references:

- [Database CLI commands](https://lyrion.org/reference/cli/database/)
- [LMS query implementation](https://github.com/LMS-Community/slimserver/blob/public/9.1/Slim/Control/Queries.pm)
- [Persistent track statistics schema](https://github.com/LMS-Community/slimserver/blob/public/9.1/Slim/Schema/TrackPersistent.pm)

## What KST contributes

KST provides useful proven mechanics:

- strict JSON-RPC envelope and loop validation;
- 500-item sequential paging;
- durable, idempotent write batches using fresh database scopes;
- destructive reconciliation only after current data has been persisted successfully;
- job progress and diagnostic logging;
- album-artist lookup before track import;
- `libraries`/`folder_loop` parsing and virtual-library membership paging;
- efficient `tags:II` membership queries when track IDs are already known;
- ordered playlist ingestion; and
- per-item and per-collection revision fingerprints with progress reporting.

Its domain model is not suitable to copy. KST is a device-copy catalogue, uses file URL as provider identity, deliberately drops remote/provider tracks, flattens media into generic items, and assumes SQLite elsewhere in its architecture. It also currently requests rating and play-count tags without parsing them and uses the single-genre field, which can lose real relationships.

Reuse the transport and paging lessons, not KST's catalogue schema or local-file filtering.

## Provisional canonical shape

This is a logical model, not a storage schema:

| Concept | Required information |
|---|---|
| Catalogue refresh state | Source scan marker, last successful completion, entity counts, source freshness, status, and sanitised progress/warnings |
| Artist | Internal catalogue ID, current LMS locator, display name, optional provider/external identity |
| Track artist | Track and main artist identity |
| Album | Internal catalogue ID, current LMS locator, display title, year, release type, compilation state, artwork locator, album artist |
| Track | Internal catalogue ID, current LMS playback locator, title/subtitle, album, disc and track numbers, duration, year, URL, local/remote state, provider/external identity, added/updated/source-modified dates |
| Genre and membership | Genre identity/name and many-to-many track membership |
| Virtual library and membership | Library identity/name and many-to-many track membership |
| Playlist and entry | Playlist identity/name/provider URL and ordered entries, including entries not backed by a local track |
| Track statistic | Typed statistic, value, source/capability, observation time, and optional source-updated time |
| Optional work/performance | Work identity and track performance relationship when the LMS exposes it |

Search-normalised text, n-grams, phonetic keys, backend scores, and embeddings do not belong in this model. Aliases and user-reviewed corrections are canonical application data but form a separate user-owned layer targeting catalogue IDs; they are not LMS metadata.

### Identity

Every entity needs an application-owned catalogue ID separate from its current LMS numeric locator. The locator is required to browse and play the item, but it must not be assumed to survive a destructive LMS database rebuild without reuse or reassignment.

For tracks, preserve URL, external/provider ID, and any later MusicBrainz identity as reconciliation evidence. Local URL is the practical first durable source key, while acknowledging that a file move changes it. Artist and album reconciliation across a destructive rescan remains an explicit design gap: names are not unique, and ambiguous matches should orphan a correction for review rather than silently retarget it.

The first implementation can preserve internal IDs across ordinary refreshes when the source locator and corroborating metadata still agree. It must make source identity and playback locator separate concepts so stronger reconciliation can be added later.

### Statistics and plugin capabilities

Rating is native LMS track data exposed as an optional 0–100 value; Ratings Light presents the same underlying field in stars rather than owning a separate rating store. Preserve the raw value and derive stars only at a presentation boundary.

Native play count is also exposed by the core track feed. Core LMS stores a `lastplayed` timestamp, but the supported `titles` and `songinfo` tags do not return it. The Alternative Play Count plugin owns additional play/skip statistics with different semantics and does not currently present an obvious bulk, read-only catalogue command.

Therefore:

- ingest native rating and play count in the first snapshot;
- keep `lastPlayedAt` unsupported/null until a supported adapter is demonstrated;
- identify every statistic by source so native and plugin values cannot overwrite each other; and
- do not couple the service to LMS's SQLite files or use internal SQL query escapes.

Plugin references:

- [Ratings Light](https://github.com/AF-1/lms-ratingslight)
- [Alternative Play Count](https://github.com/AF-1/lms-alternativeplaycount)

## Refresh strategy

LMS exposes per-track added, updated, and file-modification timestamps, but no supported changed-since track query. More importantly, rating, play count, dynamic virtual-library membership, and plugin statistics can change without a media rescan or track metadata update.

The reliable first strategy is therefore:

1. Open a persisted refresh run and read `serverstatus`; fail without cleanup while LMS is scanning.
2. Page albums, genres and tracks, writing each page immediately in a short transaction and marking rows with the refresh ID.
3. Page the broad artist lookup after relationships exist and retain only currently referenced album/main artists.
4. Write virtual-library identities, then stream each membership page with the same refresh marker.
5. Read `serverstatus` again and stop without cleanup if the library changed during ingestion.
6. Validate unique seen-row counts for every counted LMS collection, including the broad artist lookup and each virtual-library membership. Persist only the bounded artist-lookup identity evidence needed for that validation; do not retain whole-library identities in memory.
7. Delete unseen memberships and entities in dependency order, record aggregate sanitised warnings, and mark the refresh successful.
8. On cancellation or failure, retain every completed batch and all older unseen rows; the next successful refresh converges them.

This is a complete reconciliation, not a full-memory snapshot or atomic clear-and-reload. Later incremental work can use scan notifications, rating notifications, or provider-specific change feeds, but it must retain periodic reconciliation because none of those signals covers every capability.

## Implementation slices

1. **Implemented:** add transport-neutral catalogue ingestion records and bounded `ICatalogueImportWriter` contracts in Abstractions, including explicit source locators, main and album artists, many-to-many memberships, and statistics provenance.
2. **Partly implemented:** add an LMS catalogue reader in Lms with command-specific parsers, sequential paging, cancellation, sanitised progress, and storage-side filtering to referenced main and album artists. Albums, genres, tracks, native rating/play count, and virtual libraries are covered; configurable paging and playlists remain.
3. **Implemented:** persist each page durably in a separate SQLite catalogue, reconcile unseen rows after successful reading, rebuild incompatible derived schemas, retain failed-run batches for convergence, and orchestrate one manual background refresh with inspectable logs.
4. **Implemented:** validate the adapter read-only against a non-fixture LMS library without recording media names, paths, identifiers, or private measurements. The resulting qualitative conclusions are recorded above.
5. Add a storage-neutral read boundary so the converged catalogue can feed the separate search-backend benchmark story after a successful refresh.

## Decisions deliberately deferred

- Production page size and refresh schedule.
- Artist/album identity reconciliation across destructive LMS rescans.
- A supported source for last-played timestamps and Alternative Play Count statistics.
- Work/performance ingestion depth.
- Remote provider-specific metadata and refresh adapters, which need a representative Spotify/BBC test source.
- The MCP selection tool contract that will query rating, absolute play dates, artists, and virtual libraries.
