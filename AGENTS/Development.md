# Development Guidance

## Local services

- API: `http://127.0.0.1:5600`
- Vite: `http://localhost:5175`
- Vite proxies `/api` and `/mcp` to the API.

Use `dev.sh` or `dev.ps1` for the interactive BoardOil-inspired supervisor. Use `dev-startall.sh` or `dev-startall.ps1` when an unattended two-process run is needed.

The supervisor manages only this repository's API and Vite processes. It may stop a port listener only after recognising the expected command line. Logs belong under ignored `.data/dev/logs`.

## Evaluation

- `evaluate.sh` and `evaluate.ps1` run the LMS pass-through baseline against the private sibling corpus and require `LVM_EVALUATION_LMS_BASE_URL` to identify the live, read-only LMS origin. Production resolver evaluation uses the deployed diagnostic REST endpoint so it measures the actual published artifact and target hardware.
- Evaluation must not fall back to `.data/dev/appsettings.local.json` or consume the application's `LyrionVoiceMcpLms__*` variables. This keeps real-corpus results separate from the artificial development LMS and prevents evaluation configuration from redirecting normal development.
- The default corpus is `../lyrion-voice-evaluation/corpus.json`; generated reports go to ignored `.data/evaluation`.
- Pass `--corpus` or `--output` only when overriding those defaults.
- The API exposes production diagnostic discovery at `GET /api/evaluation`, diagnostic execution at `POST /api/evaluation/search`, production index state at `GET /api/search/index`, and manual rebuild at `POST /api/search/index/rebuild`. Search returns conflict while no compatible artifact exists and never builds lazily.

## Containers

- The Docker image is the production-shaped deployment unit and serves API, MCP, and Vue on port 5600.
- Container search observations, the canonical catalogue database, the operational database, and the disposable production search index live under `/data`; keep that path on a persistent volume.
- Supported architectures are `linux/amd64` and `linux/arm64` only.
- Do not bake LMS environment addresses or local credentials into an image.
- The current CI builds and smoke-tests images but does not publish them.

## LMS runtime configuration

- `dev.sh`, `dev.ps1`, and the unattended development launchers opt into `.data/dev/appsettings.local.json` automatically. Keep the machine's development LMS identity and origin there so normal startup needs no shell variables.
- The local settings file is ignored and must never be committed. Environment variables may override it for exceptional automation, but are not the normal interactive workflow.
- Compose maps `LVM_LMS_SERVER_ID`, `LVM_LMS_BASE_URL`, and optional `LVM_LMS_REQUEST_TIMEOUT_SECONDS` into the container.
- Compose accepts optional `LVM_SEARCH_RETENTION_DAYS`; operational search history defaults to 90 days.
- The operational database uses `LyrionVoiceMcpOperations:DatabasePath`, defaults to `.data/operations.db`, and is `/data/operations.db` in the container. Job, error, and MCP-call retention and schedule settings live below `LyrionVoiceMcpOperations`; catalogue automatic scheduling is disabled by default.
- Catalogue storage uses `LyrionVoiceMcpCatalogue:DatabasePath`, defaults to `.data/catalogue.db` in local development, and is fixed to `/data/catalogue.db` in the container. It is intentionally separate from observations and the production search index.
- Production search uses `LyrionVoiceMcpSearch:IndexDirectoryPath`, defaults to `.data/search-index`, and is `/data/search-index` in the container. Generation directories contain the index and manifest; the current pointer selects the published generation and job-specific staging is disposable.
- Do not commit environment-specific LMS values. An unconfigured runtime is valid and reports `not_configured` from `/api/lms`.

## Build metadata

Use these configuration keys:

- `LyrionVoiceMcpBuild__Version`
- `LyrionVoiceMcpBuild__Channel`
- `LyrionVoiceMcpBuild__Build`
- `LyrionVoiceMcpBuild__Commit`

## Dependencies

- Check the licence, provenance, transitive dependency set, and redistribution requirements before adding a new package or frontend dependency.
- Prefer an official or actively maintained implementation when equivalent choices exist. Do not accept a dependency merely because its declared licence is permissive when its package omits required notices or its API does not meet the use case.
- Record any notices required by dependencies distributed with the application before committing the dependency.
