# Architecture Guidance

Read this before adding projects, dependencies, storage, or new integration boundaries.

## Implemented solution layers

- `LyrionVoiceMcp.Api`: ASP.NET composition root, HTTP endpoints, MCP transport, and built Vue hosting.
- `LyrionVoiceMcp.Contracts`: public HTTP DTOs and, later, stable MCP input/output DTOs.
- `LyrionVoiceMcp.Abstractions`: domain-facing interfaces and transport-neutral models.
- `LyrionVoiceMcp.Services`: application orchestration and policy.
- `LyrionVoiceMcp.Lms`: LMS JSON-RPC infrastructure behind abstractions.
- `LyrionVoiceMcp.Ef.Abstractions`: EF-facing scope, entity, and repository contracts, kept separate from transport-neutral application abstractions.
- `LyrionVoiceMcp.Ef`: the EF Core application database, context/scoping infrastructure, repository base, entity configurations, and generated migrations.
- `LyrionVoiceMcp.Persistence`: dormant handwritten SQLite catalogue and operational stores plus the read-only legacy search-observation importer, retained until cleanup. Runtime application persistence no longer uses the handwritten stores.
- `LyrionVoiceMcp.Search`: the production catalogue-backed resolver, production-neutral resolver and diagnostic contracts, bounded index construction, scoring, diagnostics and safe artifact publication. It depends only on storage-neutral abstractions.
- `LyrionVoiceMcp.Evaluation`: the executable private-corpus validator, LMS baseline, and resolver-neutral benchmark runner. It consumes production-neutral Search contracts and is never a deployed runtime dependency.
- `LyrionVoiceMcp.Web`: Vue administration and review UI.
- `LyrionVoiceMcp.Dev`: local API/Vite process supervisor only.

## Dependency rules

- Contracts and general Abstractions have no project references. `LyrionVoiceMcp.Ef.Abstractions` references EF Core because its scope and repository contracts are explicitly persistence-facing.
- Services, Lms, and Persistence may depend on Abstractions. Services may additionally depend on `LyrionVoiceMcp.Ef.Abstractions` for repository contracts and persistence entities, but not on the EF implementation.
- `LyrionVoiceMcp.Ef` depends only on `LyrionVoiceMcp.Ef.Abstractions`. Application services may depend on `LyrionVoiceMcp.Ef.Abstractions` as entities are migrated, but must not reference the EF implementation or a concrete DbContext.
- Search depends only on Abstractions. Evaluation depends on Abstractions, Lms, and Search. Api composes Contracts, Abstractions, Services, Lms, Persistence, EF, and Search; it must not reference the executable Evaluation project.
- Api alone owns ASP.NET, MCP SDK transport wiring, and the deployed production-search diagnostic service. Evaluation remains transport-neutral; do not add HTTP or MCP dependencies to it or move deployed runtime services into it.
- Endpoint and MCP handlers stay thin and delegate behaviour to Services.
- Services must not depend on ASP.NET, MCP SDK types, Vue, or a concrete future search engine.
- MCP tools must not call raw LMS JSON-RPC directly.
- Application services own EF scopes, repository coordination, and the unit-of-work save. EF repositories own only entity queries and persistence operations.

## Runtime shape

- One ASP.NET process serves `/api`, including the search-evaluation diagnostics, `/mcp`, and the compiled Vue SPA.
- A deployment targets one configured LMS server. Do not build cross-server routing into the initial runtime.
- Health is process liveness and must not depend on LMS availability.
- LMS connectivity is reported separately by `/api/lms`; an unavailable LMS must not make `/api/health` fail.
- Operational search observations use the EF application database through `ISearchObservationStore`. Services owns the unit of work and maps transport-neutral observation models to EF entities; repository queries remain behind `ISearchObservationRepository`. Do not expose EF types through the application or HTTP contracts.
- The canonical catalogue uses focused EF repositories in the application database. `ICatalogueLifecycleService` owns readiness and reconciliation policy, `ICatalogueImportWriter` is the bounded ingestion boundary, and `ICatalogueSearchDocumentSource` is the bounded production-index read boundary. The search index remains a separate derived artifact.
- Durable jobs, schedules, application errors, and MCP tool-call history use focused EF repositories in the application database. Services owns scopes, lifecycle coordination, job execution, scheduling policy, retention, and entity-to-domain mapping; Api owns their REST/UI and MCP filter integration. Do not move background workflows back into bespoke in-memory queues.
- Jobs are the standard boundary for inspectable or scheduled background work. Handlers are typed, cancellation-aware application services; the runner alone owns lifecycle transitions and unexpected-exception logging.
- Production search-index builds are typed durable jobs. Services owns enqueue policy and catalogue validation through `ISearchIndexService`; Search implements bounded construction, validation, atomic publication, opening, and diagnostics. MCP and diagnostic HTTP search read only the published compatible generation.
- MCP media and browse references use a bounded singleton in-memory handle registry in Services. The registry deliberately remains process-local because the runtime is one application server and handle invalidation on restart is part of the contract; do not move these ephemeral hand-off values into operational persistence.
- The EF application database is authoritative for catalogue, search-observation, and operational data. The legacy observation database remains a read-only startup import source; legacy catalogue and operations databases are deliberately unused and untouched.

## Planned boundaries

Canonical ID reconciliation, provider/plugin catalogues, corrections, structured filtering, ratings, last-played capability work, and virtual-library-aware search remain planned. Keep them independent of operational history and the selected index adapter.
