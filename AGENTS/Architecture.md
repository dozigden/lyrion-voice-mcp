# Architecture Guidance

Read this before adding projects, dependencies, storage, or new integration boundaries.

## Implemented solution layers

- `LyrionVoiceMcp.Api`: ASP.NET composition root, HTTP endpoints, MCP transport, and built Vue hosting.
- `LyrionVoiceMcp.Contracts`: public HTTP DTOs and, later, stable MCP input/output DTOs.
- `LyrionVoiceMcp.Abstractions`: domain-facing interfaces and transport-neutral models.
- `LyrionVoiceMcp.Services`: application orchestration and policy.
- `LyrionVoiceMcp.Lms`: LMS JSON-RPC infrastructure behind abstractions.
- `LyrionVoiceMcp.Persistence`: separate SQLite-backed catalogue, search-observation, and operational jobs/errors/tool-call stores behind abstractions.
- `LyrionVoiceMcp.Search`: the production catalogue-backed resolver, bounded index construction, scoring, diagnostics and safe artifact publication. It depends only on storage-neutral abstractions.
- `LyrionVoiceMcp.Evaluation`: corpus validation, the LMS baseline, production-resolver benchmarking, and its transport-neutral diagnostic service. The deployed API composes that service so private evaluation can measure the target hardware.
- `LyrionVoiceMcp.Web`: Vue administration and review UI.
- `LyrionVoiceMcp.Dev`: local API/Vite process supervisor only.

## Dependency rules

- Contracts and Abstractions have no project references.
- Services, Lms, and Persistence may depend on Abstractions.
- Search depends only on Abstractions. Evaluation depends on Abstractions, Lms, and Search. Api composes Contracts, Abstractions, Services, Lms, Persistence, Search, and Evaluation.
- Api alone owns ASP.NET and MCP SDK transport wiring. Evaluation remains transport-neutral; do not add HTTP or MCP dependencies to it.
- Endpoint and MCP handlers stay thin and delegate behaviour to Services.
- Services must not depend on ASP.NET, MCP SDK types, Vue, or a concrete future search engine.
- MCP tools must not call raw LMS JSON-RPC directly.

## Runtime shape

- One ASP.NET process serves `/api`, including the search-evaluation diagnostics, `/mcp`, and the compiled Vue SPA.
- A deployment targets one configured LMS server. Do not build cross-server routing into the initial runtime.
- Health is process liveness and must not depend on LMS availability.
- LMS connectivity is reported separately by `/api/lms`; an unavailable LMS must not make `/api/health` fail.
- Operational search observations use SQLite through `ISearchObservationStore`. Do not let persistence types leak into Services or reuse this database as the catalogue or search index.
- The canonical catalogue uses its own SQLite database through `IMediaCatalogueStore`. `ICatalogueImportWriter` is the bounded ingestion boundary, and `ICatalogueSearchDocumentSource` is the bounded production-index read boundary.
- Durable jobs, schedules, application errors, and MCP tool-call history share the operational SQLite database through separate abstraction interfaces. Services owns job execution and scheduling policy; Api owns their REST/UI and MCP filter integration. Do not move background workflows back into bespoke in-memory queues.
- Jobs are the standard boundary for inspectable or scheduled background work. Handlers are typed, cancellation-aware application services; the runner alone owns lifecycle transitions and unexpected-exception logging.
- Production search-index builds are typed durable jobs. Services owns enqueue policy and catalogue validation through `ISearchIndexService`; Search implements bounded construction, validation, atomic publication, opening, and diagnostics. MCP and diagnostic HTTP search read only the published compatible generation.
- MCP media and browse references use a bounded singleton in-memory handle registry in Services. The registry deliberately remains process-local because the runtime is one application server and handle invalidation on restart is part of the contract; do not move these ephemeral hand-off values into operational persistence.

## Planned boundaries

Canonical ID reconciliation, provider/plugin catalogues, corrections, structured filtering, ratings, last-played capability work, and virtual-library-aware search remain planned. Keep them independent of operational history and the selected index adapter.
