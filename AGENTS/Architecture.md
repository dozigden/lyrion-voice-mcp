# Architecture Guidance

Read this before adding projects, dependencies, storage, or new integration boundaries.

## Implemented solution layers

- `LyrionVoiceMcp.Api`: ASP.NET composition root, HTTP endpoints, MCP transport, and built Vue hosting.
- `LyrionVoiceMcp.Contracts`: public HTTP DTOs and, later, stable MCP input/output DTOs.
- `LyrionVoiceMcp.Abstractions`: domain-facing interfaces and transport-neutral models.
- `LyrionVoiceMcp.Services`: application orchestration and policy.
- `LyrionVoiceMcp.Lms`: LMS JSON-RPC infrastructure behind abstractions.
- `LyrionVoiceMcp.Persistence`: separate SQLite-backed catalogue, search-observation, and operational jobs/errors/tool-call stores behind abstractions.
- `LyrionVoiceMcp.Evaluation`: corpus validation, sequential replaceable-resolver benchmarking, experimental indexed comparators, and their transport-neutral diagnostic service. The deployed API composes that service so private evaluation can measure the target hardware. Evaluation-only candidates may inspect the concrete catalogue adapter as an explicitly ring-fenced experiment.
- `LyrionVoiceMcp.Web`: Vue administration and review UI.
- `LyrionVoiceMcp.Dev`: local API/Vite process supervisor only.

## Dependency rules

- Contracts and Abstractions have no project references.
- Services, Lms, and Persistence may depend on Abstractions.
- Evaluation depends on Abstractions, Lms, and Persistence so it can build a separate local catalogue and run implemented adapters. Its concrete persistence dependency is for evaluation experiments only and does not establish a general production search dependency direction.
- Api composes Contracts, Abstractions, Services, Lms, Persistence, and Evaluation.
- Api alone owns ASP.NET and MCP SDK transport wiring. Evaluation remains transport-neutral; do not add HTTP or MCP dependencies to it.
- Endpoint and MCP handlers stay thin and delegate behaviour to Services.
- Services must not depend on ASP.NET, MCP SDK types, Vue, or a concrete future search engine.
- MCP tools must not call raw LMS JSON-RPC directly.

## Runtime shape

- One ASP.NET process serves `/api`, including the search-evaluation diagnostics, `/mcp`, and the compiled Vue SPA.
- A deployment targets one configured LMS server. Do not build cross-server routing into the initial runtime.
- Health is process liveness and must not depend on LMS availability.
- LMS connectivity is reported separately by `/api/lms`; an unavailable LMS must not make `/api/health` fail.
- Operational search observations use SQLite through `ISearchObservationStore`. Do not let persistence types leak into Services or reuse this database as a future catalogue or search index.
- The canonical catalogue uses its own SQLite database through `IMediaCatalogueStore`. `ICatalogueImportWriter` is the bounded ingestion boundary implemented by that store. SQLite is an adapter choice for durable application data, not a decision about the replaceable search index.
- Durable jobs, schedules, application errors, and MCP tool-call history share the operational SQLite database through separate abstraction interfaces. Services owns job execution and scheduling policy; Api owns their REST/UI and MCP filter integration. Do not move background workflows back into bespoke in-memory queues.
- Jobs are the standard boundary for inspectable or scheduled background work. Handlers are typed, cancellation-aware application services; the runner alone owns lifecycle transitions and unexpected-exception logging.
- Deployed diagnostic search-index builds are typed durable jobs. Services owns enqueue policy and job/catalogue validation through `ISearchIndexService`; Evaluation implements the replaceable artifact builder and opener. HTTP search reads only published artifacts, while the offline evaluator remains synchronous.

## Planned boundaries

Storage-neutral catalogue import records and bounded writer contracts are implemented in Abstractions, the paged LMS reader is implemented in Lms, refresh orchestration is a durable job in Services, and batch upsert/reconciliation is implemented in Persistence. Durable build/open orchestration exists for the deployed evaluation comparators, but selecting and integrating the production search index remains planned. Canonical ID reconciliation, catalogue queries, and playlists also remain planned. Keep them independent of operational history and selected index technology.
