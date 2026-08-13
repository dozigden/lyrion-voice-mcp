# Architecture Guidance

Read this before adding projects, dependencies, storage, or new integration boundaries.

## Implemented solution layers

- `LyrionVoiceMcp.Api`: ASP.NET composition root, HTTP endpoints, MCP transport, and built Vue hosting.
- `LyrionVoiceMcp.Contracts`: public HTTP DTOs and, later, stable MCP input/output DTOs.
- `LyrionVoiceMcp.Abstractions`: domain-facing interfaces and transport-neutral models.
- `LyrionVoiceMcp.Services`: application orchestration and policy.
- `LyrionVoiceMcp.Lms`: LMS JSON-RPC infrastructure behind abstractions.
- `LyrionVoiceMcp.Persistence`: SQLite-backed operational search-observation storage behind abstractions.
- `LyrionVoiceMcp.Evaluation`: offline corpus validation and sequential LMS baseline benchmarking; it is not part of the deployed service.
- `LyrionVoiceMcp.Web`: Vue administration and review UI.
- `LyrionVoiceMcp.Dev`: local API/Vite process supervisor only.

## Dependency rules

- Contracts and Abstractions have no project references.
- Services, Lms, and Persistence may depend on Abstractions.
- Evaluation depends on Abstractions and Lms so it can run the implemented LMS adapter without exposing an evaluation HTTP or MCP surface.
- Api composes Contracts, Abstractions, Services, Lms, and Persistence.
- Only Api owns ASP.NET and MCP SDK transport wiring.
- Endpoint and MCP handlers stay thin and delegate behaviour to Services.
- Services must not depend on ASP.NET, MCP SDK types, Vue, or a concrete future search engine.
- MCP tools must not call raw LMS JSON-RPC directly.

## Runtime shape

- One ASP.NET process serves `/api`, `/mcp`, and the compiled Vue SPA.
- A deployment targets one configured LMS server. Do not build cross-server routing into the initial runtime.
- Health is process liveness and must not depend on LMS availability.
- LMS connectivity is reported separately by `/api/lms`; an unavailable LMS must not make `/api/health` fail.
- Operational search observations use SQLite through `ISearchObservationStore`. Do not let persistence types leak into Services or reuse this database as a future catalogue or search index.

## Planned boundaries

A canonical catalogue and replaceable search index are planned but not implemented. Keep both independent of the operational observation store and selected index technology.
