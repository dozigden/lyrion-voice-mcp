# Catalogue Guidance

Read this before changing catalogue models, LMS ingestion, provider metadata, catalogue persistence, or refresh orchestration.

The import contracts, LMS snapshot reader, durable atomic publication, refresh status, and manual refresh orchestration are implemented; the queryable canonical catalogue is not. The evidence and provisional data shape are recorded in [CATALOGUE_RECONNAISSANCE.md](../CATALOGUE_RECONNAISSANCE.md).

## Boundaries

- Catalogue application contracts belong in Abstractions, orchestration belongs in Services, LMS response parsing belongs in Lms, and concrete storage belongs behind a persistence adapter.
- Do not expose LMS JSON response types, a concrete database, or a search-engine document through catalogue contracts.
- The catalogue is canonical application data. Search indexes are disposable derived data and operational search observations remain separate application data.
- Preserve provider/source metadata needed for later Spotify, BBC Sounds, and other plugin adapters. Do not model every item as a local file merely because the first representative library currently contains local tracks.
- Keep typed entities and relationships for contributors, albums, tracks, genres, playlists, and virtual libraries. Avoid an untyped metadata bag as the primary model.
- Represent works, provider-specific statistics, and other optional capabilities through explicit extension points rather than forcing them into unrelated core fields.

## LMS ingestion

- `LmsCatalogueReader` currently reads contributors, albums, genres, tracks, native statistics, virtual libraries, and their memberships into one `CatalogueImportSnapshot`. Playlists remain a later part of the catalogue story.
- The reader uses sequential 500-item pages, checks `serverstatus` before and after the snapshot, refuses to read during a scan, rejects changing command counts and duplicate IDs, and returns aggregate referential warnings without media names or paths.
- Page every collection that supports paging and validate each command's actual response shape. The `libraries` command is the exception: it ignores paging arguments, returns all virtual libraries in one `folder_loop`, and may omit `count`; read it once, then page each library's track membership separately.
- Use role-specific contributor ID fields from title tag `S`; use a separate contributor listing for names. Do not split the corresponding comma-separated contributor-name fields because LMS itself notes that names containing commas are ambiguous.
- Use genre ID tag `P` and model track-to-genre as many-to-many. The ordinary `g` tag can return an arbitrary single genre for multi-genre tracks.
- Treat rating tag `R` as an optional raw 0–100 track rating and play-count tag `O` as the native LMS statistic. Do not represent a missing value as a known zero.
- Preserve remote/local state, URL, extension identity, release type, contributor roles, disc/track ordering, dates, and current LMS IDs even when a field is absent on the initial library.
- Treat a current numeric LMS media ID as a source locator needed for browse and playback, not as the application's only durable identity across destructive rescans.
- Core LMS stores a last-played value but the supported `titles`/`songinfo` tag surface does not return it. Keep last-played optional until a supported capability adapter is proven; do not reach into the LMS database or expose raw SQL.

## Refresh

- `IMediaCatalogueStore.PublishAsync` is the storage-neutral atomic-publication boundary. `SqliteMediaCatalogueStore` implements it in a catalogue database separate from search observations; do not leak this adapter choice into catalogue consumers or the future search index.
- The safe initial strategy is a complete paged snapshot reconciled into a staging generation and published atomically only after successful validation.
- Never delete or partially replace the current generation because a page, plugin, or LMS call failed. Retain run status, source freshness, counts, duration, and sanitised warnings.
- A refresh run is recorded before LMS reading begins. Successful publication, the active-generation switch, and refresh completion share one transaction; failed and cancelled reads leave the active generation unchanged. Startup marks a previously running refresh as interrupted.
- `GET /api/catalogue` exposes the current published generation and latest refresh status. `POST /api/catalogue/refresh` records and queues one background refresh, returns `202 Accepted`, and rejects concurrent attempts. The background operation is tied to application lifetime rather than request lifetime because a representative import takes about two minutes. Refresh remains deliberately manual; do not add startup or scheduled LMS reads without an explicit policy.
- `serverstatus lastscan` is a useful library-scan signal but not a complete change token: ratings, play counts, dynamic virtual-library membership, and plugin statistics can change without a media scan.
- Derive a catalogue revision only after publication. Downstream search indexes must be able to rebuild from a specific published revision.
- Do not choose SQLite, FTS5, Lucene.NET, or another backend as part of the ingestion contract. Storage and search-engine selection remain separate decisions.
