# Catalogue Guidance

Read this before changing catalogue models, LMS ingestion, provider metadata, catalogue persistence, or refresh orchestration.

The import contracts, bounded LMS reader/writer pipeline, durable reconciliation, refresh job, queryable canonical catalogue, and bounded production-search document source are implemented. The evidence and provisional data shape are recorded in [CATALOGUE_RECONNAISSANCE.md](../CATALOGUE_RECONNAISSANCE.md).

## Boundaries

- Catalogue application contracts belong in Abstractions, orchestration belongs in Services, LMS response parsing belongs in Lms, and concrete storage belongs behind a persistence adapter.
- Do not expose LMS JSON response types, a concrete database, or a search-engine document through catalogue contracts.
- The catalogue is canonical application data. Search indexes are disposable derived data and operational search observations remain separate application data.
- Stream production search documents in stable keyset-ordered batches of at most 500. Validate the successful refresh before and after streaming; never return a partially converged catalogue or materialise the complete library.
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

- `ICatalogueImportWriter` is the storage-neutral write boundary and `ICatalogueSearchDocumentSource` is the storage-neutral production-index read boundary. `SqliteMediaCatalogueStore` implements both without leaking its database to Search.
- Every LMS page is upserted in its own short transaction and is immediately durable. The store marks each canonical row with the current refresh ID. Track relationships are replaced for the bounded track page being written; virtual-library memberships carry their own seen marker. A bounded reconciliation table records every broad artist-lookup ID even though only referenced main and album artists enter the canonical artist table.
- A failed run may leave successfully written new or updated rows alongside older rows. Do not remove unseen rows unless every LMS phase and the final `serverstatus` stability check succeeded. A retry must converge without duplicating identities.
- After a successful read, validate the unique seen counts for the broad artist lookup, albums, genres, tracks, virtual libraries, and each virtual-library membership, then delete unseen memberships and entities in dependency order. Cleanup is deliberately not an all-catalogue transaction; a cleanup failure is recorded and the next refresh converges.
- `catalogue_state` is a singleton readiness record, not refresh history. Starting a refresh replaces it with the new refresh ID and `running` state and clears the previous summary; completion records `succeeded`, `failed`, or `cancelled`. Catalogue initialisation changes an abandoned `running` state to `interrupted`. A summary exists only for the current successful refresh and is not an atomic revision of every row.
- Catalogue refresh is the `catalogue.refresh` durable job. Its payload, result, progress, warnings, cancellation and historical terminal state live in the operational job store. Startup marks abandoned running jobs failed; unexpected handler failures are linked to the durable error log. The matching `job-{id}` refresh ID joins the job to catalogue readiness and derived search artifacts without adding a second generation concept.
- Existing catalogue schemas are disposable derived data. When this bounded-ingestion schema replaces an older schema, rebuild the catalogue database rather than maintaining data migrations. Never delete or rebuild the separate operational search-observation database.
- `GET /api/catalogue` exposes the current successful summary, when there is one, plus the latest catalogue job. `POST /api/catalogue/refresh` queues one durable job, returns `202 Accepted`, and rejects concurrent attempts. Full progress and result inspection belongs to `/api/jobs/{id}` and the Jobs UI. The catalogue schedule definition is present but disabled by default; run-now is allowed, while automatic LMS reads require explicit configuration.
- `serverstatus lastscan` is a useful library-scan signal but not a complete change token: ratings, play counts, dynamic virtual-library membership, and plugin statistics can change without a media scan.
- A successful deployed refresh queues one `search-index.rebuild` durable job. The job records the catalogue refresh ID and refuses to build after readiness moves to another refresh; the published artifact records the same ID.
- Production and diagnostic searches share the last compatible published artifact. Building occurs only in durable jobs; a missing artifact makes search explicitly unavailable.
- Do not expose SQLite or FTS5 through the catalogue boundary. Storage and search-engine selection remain separate decisions even while both adapters use SQLite.
