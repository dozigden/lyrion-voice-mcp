# Search Guidance

Read this before changing search contracts, ranking, observation capture, catalogue ingestion, or search storage.

- Production artist, album, and track search uses the catalogue-backed `catalogue-phuzzy-sqlite` resolver version 1. `ProductionCatalogueSearchService.Descriptor` is the authoritative production resolver identity; resolver and index-builder consumers must read the shared descriptor rather than repeat its name or version. Playlist discovery remains an isolated LMS `playlists` request.
- Keep resolver, candidate, execution, metric, and diagnostic contracts in Search production-neutral. The Evaluation executable consumes those contracts for benchmarking; deployed code must not depend on Evaluation types or the executable project.
- Return at most 20 ranked catalogue candidates followed by at most 20 playlists in LMS order. Do not interleave the two sources.
- Reject public search queries over 500 characters or 20 normalised letter-or-digit tokens before starting either retrieval source. Keep diagnostic and production limits aligned.
- The production resolver uses bounded normalised, compact, acronym, consonant-skeleton, Double Metaphone, token/prefix, and trigram retrieval lanes, then applies the application-owned scorer. Preserve the distinction between retrieval evidence, ranking score, and confidence; no confidence or speculative no-match threshold is implemented.
- Numeric tokens must contribute to phonetic evidence through spoken digit forms. Do not allow a phonetic encoder to silently discard a digit while treating the remaining words as a complete query span.
- Keep the public result contract independent of catalogue rows, SQLite documents, and LMS response shapes.
- Search returns one opaque result reference per candidate; it does not return a separate public search identifier. Each reference combines candidate correlation with the underlying LMS playback identity.
- Returning the same media item from two searches must produce distinct result references for the two candidate occurrences.
- Artist, album, and playlist search references can seed `browse`. Preserve correlation through derived browse descendants and continuations so successful playback or queue mutation marks the originating result selected.
- A production index is disposable derived data. Build only from storage-neutral `ICatalogueSearchDocumentSource` batches of 500 or fewer documents; never materialise the complete library in application memory.
- Build in a job-specific staging generation, validate it, move it into place, and atomically replace the small current-generation pointer. Continue serving the previous compatible generation throughout a rebuild.
- If no compatible published generation exists, return an explicit unavailable error. Do not build lazily and do not fall back to LMS artist, album, or track search.
- A successful catalogue refresh queues exactly one production index rebuild. Manual rebuild is a REST/UI administration action, not an MCP tool.
- The EF-backed operational observation store records original and trimmed query, resolver/version, retrieval sources, timings, ordered candidates, zero-result searches, failures, later successful selections, and human reviews. Services owns its scopes, model/entity mapping, and retention; EF repositories own its queries.
- `SearchObservationRecorder` owns observation lifecycle metadata, DTO construction, source evidence, and best-effort persistence. Keep `SearchService` focused on validation, concurrent retrieval, candidate ordering, correlation, references, and the public outcome.
- If playlist retrieval fails, retain catalogue candidates and per-source failure evidence while failing the public search call. Treat failed searches separately from completed searches with no results.
- Observation recording is best effort and must not fail search, playback, or queue operations.
- Only mark candidates selected after their LMS mutation succeeds. Never mark skipped, failed, or unattempted batch items selected.
- Evaluation exports contain only explicitly included cases and omit observation IDs, LMS media IDs, correlation references, timestamps, and private notes. The canonical real corpus lives only in the private sibling repository.
- Spotify may be used only in a later offline experiment over recorded misses. It is not a runtime fallback or dependency.
- `GET /api/evaluation` and `POST /api/evaluation/search` expose diagnostics for the actual production resolver under the external name `production`. Keep lane and scoring evidence available without accepting or persisting corpus cases.
- Historical comparator evidence and the SQLite selection rationale live in `SEARCH_RESEARCH.md`; retired comparator implementations do not belong in production or Evaluation.
