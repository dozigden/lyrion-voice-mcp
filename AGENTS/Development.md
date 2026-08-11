# Development Guidance

## Local services

- API: `http://127.0.0.1:5600`
- Vite: `http://localhost:5175`
- Vite proxies `/api` and `/mcp` to the API.

Use `dev.sh` or `dev.ps1` for the interactive BoardOil-inspired supervisor. Use `dev-startall.sh` or `dev-startall.ps1` when an unattended two-process run is needed.

The supervisor manages only this repository's API and Vite processes. It may stop a port listener only after recognising the expected command line. Logs belong under ignored `.data/dev/logs`.

## Containers

- The Docker image is the production-shaped deployment unit and serves API, MCP, and Vue on port 5600.
- Supported architectures are `linux/amd64` and `linux/arm64` only.
- Do not bake LMS environment addresses or local credentials into an image.
- The current CI builds and smoke-tests images but does not publish them.

## LMS runtime configuration

- `dev.sh`, `dev.ps1`, and the unattended development launchers opt into `.data/dev/appsettings.local.json` automatically. Keep the machine's development LMS identity and origin there so normal startup needs no shell variables.
- The local settings file is ignored and must never be committed. Environment variables may override it for exceptional automation, but are not the normal interactive workflow.
- Compose maps `LVM_LMS_SERVER_ID`, `LVM_LMS_BASE_URL`, and optional `LVM_LMS_REQUEST_TIMEOUT_SECONDS` into the container.
- Do not commit environment-specific LMS values. An unconfigured runtime is valid and reports `not_configured` from `/api/lms`.

## Build metadata

Use these configuration keys:

- `LyrionVoiceMcpBuild__Version`
- `LyrionVoiceMcpBuild__Channel`
- `LyrionVoiceMcpBuild__Build`
- `LyrionVoiceMcpBuild__Commit`
