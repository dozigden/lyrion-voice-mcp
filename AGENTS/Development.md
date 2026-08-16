# Development Guidance

## Local services

- API: `http://127.0.0.1:5600`
- Vite: `http://localhost:5175`
- Vite proxies `/api` and `/mcp` to the API.

Use `dev.sh` or `dev.ps1` for the interactive BoardOil-inspired supervisor. Use `dev-startall.sh` or `dev-startall.ps1` when an unattended two-process run is needed.

The supervisor manages only this repository's API and Vite processes. It may stop a port listener only after recognising the expected command line. Logs belong under ignored `.data/dev/logs`.

## Evaluation

- `evaluate.sh` and `evaluate.ps1` default to the current LMS pass-through against the private sibling corpus and require `LVM_EVALUATION_LMS_BASE_URL` to identify the live, read-only LMS origin. Pass `--resolver catalogue-lexical` for the first catalogue baseline, `--resolver catalogue-phuzzy` for the full-scan voice-tolerant scorer, `--resolver catalogue-phuzzy-indexed` for its bounded SQLite retrieval comparator, `--resolver catalogue-lucene` for the Lucene candidate-lane comparator, or `--resolver catalogue-lucene-native` for the field-aware native-ranking Lucene comparator. They use the separate `.data/evaluation/catalogue.db` snapshot by default, create that snapshot from the live evaluation LMS when missing, and refresh it explicitly with `--refresh-catalogue`. An explicit `--catalogue` path follows the same missing-file and refresh rules.
- Evaluation must not fall back to `.data/dev/appsettings.local.json` or consume the application's `LyrionVoiceMcpLms__*` variables. This keeps real-corpus results separate from the artificial development LMS and prevents evaluation configuration from redirecting normal development.
- The default corpus is `../lyrion-voice-evaluation/corpus.json`; generated reports go to ignored `.data/evaluation`.
- Pass `--corpus`, `--output`, or catalogue resolver `--catalogue` only when overriding those path defaults. Use `--refresh-catalogue` when a new shared snapshot is wanted before comparing candidates; do not refresh separately for every candidate.
- The normal API exposes evaluator discovery at `GET /api/evaluation`, index state at `GET /api/evaluation/indexes`, manual rebuild at `POST /api/evaluation/indexes/{resolver}/rebuild`, and comparator execution at `POST /api/evaluation/search`. It uses the normal deployed/development catalogue. Search opens a published artifact and returns a conflict response while none exists; it never builds lazily. `LyrionVoiceMcpEvaluation:IndexDirectoryPath` overrides the derived-index directory, which otherwise sits beside the configured catalogue.

## Containers

- The Docker image is the production-shaped deployment unit and serves API, MCP, and Vue on port 5600.
- Container search observations, the canonical catalogue database, the operational database, and disposable evaluation search indexes live under `/data`; keep that path on a persistent volume.
- Supported architectures are `linux/amd64` and `linux/arm64` only.
- Do not bake LMS environment addresses or local credentials into an image.
- The current CI builds and smoke-tests images but does not publish them.

## LMS runtime configuration

- `dev.sh`, `dev.ps1`, and the unattended development launchers opt into `.data/dev/appsettings.local.json` automatically. Keep the machine's development LMS identity and origin there so normal startup needs no shell variables.
- The local settings file is ignored and must never be committed. Environment variables may override it for exceptional automation, but are not the normal interactive workflow.
- Compose maps `LVM_LMS_SERVER_ID`, `LVM_LMS_BASE_URL`, and optional `LVM_LMS_REQUEST_TIMEOUT_SECONDS` into the container.
- Compose accepts optional `LVM_SEARCH_RETENTION_DAYS`; operational search history defaults to 90 days.
- The operational database uses `LyrionVoiceMcpOperations:DatabasePath`, defaults to `.data/operations.db`, and is `/data/operations.db` in the container. Job, error, and MCP-call retention and schedule settings live below `LyrionVoiceMcpOperations`; catalogue automatic scheduling is disabled by default.
- Catalogue storage uses `LyrionVoiceMcpCatalogue:DatabasePath`, defaults to `.data/catalogue.db` in local development, and is fixed to `/data/catalogue.db` in the container. It is intentionally separate from both search observations and the future search index.
- Evaluation search indexes use `LyrionVoiceMcpEvaluation:IndexDirectoryPath`, default beside the catalogue, and `/data/search-indexes` in the container. Each resolver owns a published directory containing its index and manifest; job-specific staging directories are disposable and must never be opened by search.
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
