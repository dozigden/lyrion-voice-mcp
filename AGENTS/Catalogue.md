# Catalogue Guidance

Read this before changing catalogue models, LMS ingestion, provider metadata, catalogue persistence, or refresh orchestration.

The import contracts, bounded LMS reader/writer pipeline, durable reconciliation, refresh status/logs, and manual refresh orchestration are implemented; the queryable canonical catalogue is not. The evidence and provisional data shape are recorded in [CATALOGUE_RECONNAISSANCE.md](../CATALOGUE_RECONNAISSANCE.md).

## Boundaries

- Catalogue application contracts belong in Abstractions, orchestration belongs in Services, LMS response parsing belongs in Lms, and concrete storage belongs behind a persistence adapter.
- Do not expose LMS JSON response types, a concrete database, or a search-engine document through catalogue contracts.
- The catalogue is canonical application data. Search indexes are disposable derived data and operational search observations remain separate application data.
- Preserve provider/source metadata needed for later Spotify, BBC Sounds, and other plugin adapters. Do not model every item as a local file merely because the first representative library currently contains local tracks.
- Keep typed entities and relationships for artists, albums, tracks, genres, playlists, and virtual libraries. Avoid an untyped metadata bag as the primary model.
- Artists mean main track artists and album artists. Do not generalise them into contributors or ingest composer, conductor, band, or other contributor-role relationships.
- Represent works, provider-specific statistics, and other optional capabilities through explicit extension points rather than forcing them into unrelated core fields.

## LMS ingestion

- `LmsCatalogueReader` sends albums, genres, tracks, the broad LMS artist-name lookup, virtual libraries, and membership pages directly to `ICatalogueImportWriter`; it never materialises the complete library. Each counted collection is read in sequential 500-item pages. The unpaged `libraries` response is accepted as one bounded response because LMS installations are expected to have far fewer than 500 virtual libraries.
- Albums and tracks are written before artist lookup pages. The writer retains an artist lookup row only when the current refresh has already referenced its ID through track `artist_ids` or album `artist_id`; unreferenced person roles never enter the canonical artist table.
- The reader checks `serverstatus` before and after ingestion, refuses to read during a scan, rejects changing command counts and invalid response shapes, and reports sanitised phase progress. SQLite records the broad artist-lookup identities and validates each virtual-library membership count so repeated source rows cannot make an incomplete import appear successful. Referential warnings are calculated in storage after successful reading without materialising whole-library identity sets in memory.
- Page every collection that supports paging and validate each command's actual response shape. The `libraries` command is the exception: it ignores paging arguments, returns all virtual libraries in one `folder_loop`, and may omit `count`; read it once, then page each library's track membership separately.
- Title tag `S` exposes role-specific LMS ID fields. Ingest only `artist_ids` and resolve names through the separate broad `artists` lookup; album `artist_id` supplies album artists. Ignore composer, conductor, band, and other role fields, and discard lookup entries not referenced through one of those two artist fields. Do not split comma-separated artist-name fields because LMS itself notes that names containing commas are ambiguous.
- Use genre ID tag `P` and model track-to-genre as many-to-many. The ordinary `g` tag can return an arbitrary single genre for multi-genre tracks.
- Treat rating tag `R` as an optional raw 0–100 track rating and play-count tag `O` as the native LMS statistic. Do not represent a missing value as a known zero.
- Preserve remote/local state, URL, extension identity, release type, main track artists, album artists, disc/track ordering, dates, and current LMS IDs even when a field is absent on the initial library.
- Treat a current numeric LMS media ID as a source locator needed for browse and playback, not as the application's only durable identity across destructive rescans.
- Core LMS stores a last-played value but the supported `titles`/`songinfo` tag surface does not return it. Keep last-played optional until a supported capability adapter is proven; do not reach into the LMS database or expose raw SQL.

## Refresh

- `ICatalogueImportWriter` is the storage-neutral page/batch boundary. `SqliteMediaCatalogueStore` implements it in a catalogue database separate from search observations; do not leak this adapter choice into catalogue consumers or the future search index.
- Every LMS page is upserted in its own short transaction and is immediately durable. The store marks each canonical row with the current refresh ID. Track relationships are replaced for the bounded track page being written; virtual-library memberships carry their own seen marker. A bounded reconciliation table records every broad artist-lookup ID even though only referenced main and album artists enter the canonical artist table.
- A failed run may leave successfully written new or updated rows alongside older rows. Do not remove unseen rows unless every LMS phase and the final `serverstatus` stability check succeeded. A retry must converge without duplicating identities.
- After a successful read, validate the unique seen counts for the broad artist lookup, albums, genres, tracks, virtual libraries, and each virtual-library membership, then delete unseen memberships and entities in dependency order. Cleanup is deliberately not an all-catalogue transaction; a cleanup failure is recorded and the next refresh converges.
- The catalogue summary describes the last successful refresh. It is not an atomic revision of every row and must not be presented as one. Downstream search indexing should begin only after a refresh succeeds.
- A refresh run and its inspectable progress/warning/error logs are recorded before reading begins. Startup marks a previously running refresh as interrupted. Failure to persist a terminal refresh state is logged but must not terminate the MCP service; startup recovery remains the fallback for the abandoned running row.
- Existing catalogue schemas are disposable derived data. When this bounded-ingestion schema replaces an older schema, rebuild the catalogue database rather than maintaining data migrations. Never delete or rebuild the separate operational search-observation database.
- `GET /api/catalogue` exposes the last successful summary plus the latest refresh and its logs. `POST /api/catalogue/refresh` records and queues one background refresh, returns `202 Accepted`, and rejects concurrent attempts. The operations page exposes this as a manual **Rebuild catalogue** action, shows its state plus the last successful track count and time, and polls the latest run while it is active. The background operation is tied to application lifetime rather than request lifetime because a representative import takes about two minutes. Refresh remains deliberately manual; do not add startup or scheduled LMS reads without an explicit policy.
- `serverstatus lastscan` is a useful library-scan signal but not a complete change token: ratings, play counts, dynamic virtual-library membership, and plugin statistics can change without a media scan.
- Start downstream search-index work only after a refresh succeeds. The future index must tolerate rebuilding from the current converged catalogue rather than assuming an atomic catalogue generation.
- Offline evaluation owns a separate local catalogue snapshot under `.data/evaluation` and may compose the existing LMS reader and SQLite store to refresh it from the live evaluation LMS. It must not reuse or overwrite the normal development catalogue. Reuse the same successful snapshot when comparing search candidates.
- Do not choose SQLite, FTS5, Lucene.NET, or another backend as part of the ingestion contract. Storage and search-engine selection remain separate decisions.
