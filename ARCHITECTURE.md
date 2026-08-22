# Architecture

Lyrion Voice MCP is one ASP.NET Core application serving the MCP endpoint, administration REST API, and compiled Vue interface. It targets one configured Lyrion Music Server (LMS) and is intended only for a trusted local network.

This document maps the implemented system. Detailed coding and operational rules remain in [AGENTS.md](AGENTS.md) and [AGENTS](AGENTS/).

## System shape

```text
Voice assistant ── Streamable HTTP /mcp ─┐
Browser         ── REST /api + Vue SPA ──┼── LyrionVoiceMcp.Api
                                         │       │
                                         │       ├── LMS JSON-RPC
                                         │       ├── EF application database
                                         │       └── disposable search index
                                         └────────────────────────────────────
```

The process has two durable storage concerns:

- One EF Core SQLite application database is authoritative for the canonical catalogue, search observations, jobs, schedules, errors, and MCP tool-call history.
- One separate SQLite search index is a disposable derived artifact. It can be rebuilt from the canonical catalogue and is never application persistence.

MCP media handles are deliberately different: they live in a bounded in-memory registry, expire after 24 hours at most, and do not survive restart.

## Project map

```text
LyrionVoiceMcp.Api
├── Contracts
├── Abstractions
├── Services ────────────┬── Abstractions
│                        └── Ef.Abstractions
├── Lms ───────────────────> Abstractions
├── Search ────────────────> Abstractions
└── Ef ────────────────────> Ef.Abstractions

LyrionVoiceMcp.Evaluation ─> Abstractions + Lms + Search
LyrionVoiceMcp.Dev          local process supervisor only
```

| Project | Responsibility |
| --- | --- |
| `LyrionVoiceMcp.Api` | Composition root, MCP transport and tools, REST endpoints, exception and tool-call filters, production search diagnostics, and Vue hosting. |
| `LyrionVoiceMcp.Contracts` | Public HTTP DTOs and stable transport contracts. |
| `LyrionVoiceMcp.Abstractions` | Transport-neutral application and infrastructure interfaces plus domain-facing models. |
| `LyrionVoiceMcp.Services` | Application orchestration, validation, durable jobs, catalogue lifecycle, reference routing, search observations, and player/media workflows. |
| `LyrionVoiceMcp.Lms` | LMS JSON-RPC transport, response parsing, catalogue reader, browse, player, queue, playback, and playlist-search adapters. |
| `LyrionVoiceMcp.Ef.Abstractions` | EF-facing entities, context-scope contracts, and repository interfaces. |
| `LyrionVoiceMcp.Ef` | The application `DbContext`, migrations, configurations, scopes, and repository implementations. |
| `LyrionVoiceMcp.Search` | Production catalogue search, bounded index construction, ranking, artifact publication, and diagnostic evidence. |
| `LyrionVoiceMcp.Evaluation` | Non-deployed private-corpus runner and direct LMS baseline. |
| `LyrionVoiceMcp.Web` | Vue administration interface. |
| `LyrionVoiceMcp.Dev` | Local API and Vite process supervision. |

`Program.cs` binds and validates environment configuration, then composes the areas through their registration extensions. Implementation projects own their internal registrations; ASP.NET, MCP, and deployed diagnostic wiring remain in Api.

## MCP request flow

Every public tool follows the same outer path:

```text
/mcp
  → official MCP SDK
  → central tool-call history filter
  → thin tool handler in Api
  → application service
  → LMS, catalogue search, or EF repositories
  → structured tool result
```

Expected validation and business rejection return tool errors rather than exceptions. Unexpected failures pass through the MCP filter, which links the failed call to the durable error log before the SDK completes the request.

### Search

1. `SearchTools` delegates the query to `ISearchService`.
2. `SearchService` validates the query before starting retrieval.
3. Catalogue search and LMS playlist search run concurrently.
4. `ProductionCatalogueSearchService` queries the current published index for artists, albums, and tracks. `LmsSearchClient` supplies the separate playlist lane.
5. Services returns catalogue candidates first and playlists second, issuing a short opaque handle for each candidate occurrence.
6. `SearchObservationRecorder` records the query, resolver, retrieval evidence, timings, ordered candidates, and failure or completion state in the application database. Recording is best effort and cannot fail the search.

Successful `play` and queue-addition mutations use the correlation carried by a handle to mark only confirmed candidates selected. Browsing from a correlated search result preserves that correlation through descendants and continuations.

## Catalogue refresh and index publication

A catalogue refresh is background work, not a long-running HTTP request.

```text
POST /api/catalogue/refresh
  → enqueue catalogue.refresh job
  → JobSchedulerService / JobRunner
  → CatalogueRefreshJobHandler
  → LmsCatalogueReader
  → bounded ICatalogueImportWriter batches
  → EF catalogue repositories
  → successful reconciliation
  → enqueue search-index.rebuild job
  → SearchIndexRebuildJobHandler
  → bounded catalogue document stream
  → staged index → validation → published pointer
```

The refresh lifecycle is:

1. The handler marks the singleton catalogue state `running` with a `job-{id}` refresh identity.
2. The LMS reader checks that LMS is not scanning and reads albums, genres, tracks, relevant artists, virtual libraries, and memberships in bounded pages.
3. Each page is saved immediately in a fresh EF scope. Rows and relationships carry the active refresh identity.
4. The reader verifies LMS counts and scan stability. Only a fully successful read permits unseen rows to be reconciled away in bounded batches.
5. The catalogue state records the terminal result. A failed or cancelled refresh leaves already durable pages in place; the next successful refresh converges them.
6. A successful catalogue job queues one correlated production index job.
7. The index handler confirms that catalogue readiness still matches its requested refresh, then streams search documents in batches of at most 500.
8. Search builds in a job-specific staging directory, validates the artifact, moves it into place, and atomically replaces the small current-generation pointer.

Searches continue using the previous compatible generation during a rebuild. With no compatible published index, search returns an explicit unavailable error; it never builds lazily or falls back to LMS library search.

## Durable jobs and schedules

Jobs, job logs, and scheduler state are EF entities. `IJobService` validates and enqueues work; the single hosted `JobSchedulerService` polls scheduled definitions and asks `IJobRunner` to run the next due job. The runner alone owns lifecycle transitions, cancellation registration, handler dispatch, and finalisation.

Catalogue refresh, search-index rebuild, and retention work are typed handlers. Correlation IDs make repeated scheduler polls idempotent. Startup marks abandoned running jobs failed so interrupted work remains visible rather than silently disappearing.

This deliberately assumes one application process. There is no distributed job claim protocol.

## Errors, calls, and observations

- API middleware captures unexpected `/api` failures and writes a durable error reference before returning a generic response.
- The MCP SDK filter records every call's arguments and complete result, including returned tool errors. Unexpected exceptions link the call to an error record.
- The job runner and scheduler record unexpected background failures with job context.
- Search observations are application records distinct from generic MCP history. They retain search-specific evidence, candidates, later selections, and human reviews.

These diagnostic writes are best effort and use independent EF scopes where needed so failure recording does not replace the original outcome. The Vue application exposes paged summaries and full detail views for jobs, schedules, errors, tool calls, and search observations.

## Persistence boundary

Application services own EF scopes and unit-of-work saves. Repositories contain entity queries and persistence operations but do not create scopes or save independently. Contexts remain short-lived and are never held across LMS calls or index construction.

Startup applies generated EF migrations, enables SQLite WAL mode, recovers interrupted catalogue state, marks abandoned tool calls interrupted, and starts the hosted job scheduler. Catalogue data can be rebuilt from LMS; operational history and human reviews are instance data.

## Implemented versus planned

Implemented now:

- Catalogue-backed phuzzy artist, album, and track search plus isolated LMS playlist search.
- Voice-facing track search ratings on a 0–5 string scale, with unrated tracks labelled explicitly.
- Local-library browse and opaque reference routing into playback and queue operations.
- Canonical catalogue ingestion with ratings, play counts, genres, virtual libraries, and memberships.
- One EF application database, disposable search generations, durable jobs, operational history, and Vue inspection.

Planned, not implemented:

- User-managed local search corrections and aliases.
- Structured compound search using ratings, virtual-library scope, relationships, and available date/statistic fields.
- Provider and plugin capability adapters for Spotify/Spotty, BBC Sounds, and other non-local media shapes.
- Last-played filtering unless LMS or a plugin exposes a supported capability.
- Provider browsing, player grouping, mixes/radio, and further player controls.

## Further reading

- [README.md](README.md) — setup and current user-facing behaviour
- [MCP_CONTRACT.md](MCP_CONTRACT.md) — public tool contracts
- [CATALOGUE_RECONNAISSANCE.md](CATALOGUE_RECONNAISSANCE.md) — LMS ingestion evidence
- [SEARCH_RESEARCH.md](SEARCH_RESEARCH.md) — matching experiments and backend decision
- [SCRATCHPAD.md](SCRATCHPAD.md) — product decisions and future requirements
- [AGENTS.md](AGENTS.md) — implementation guidance index
