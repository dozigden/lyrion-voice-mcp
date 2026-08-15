# Testing Guidance

## Commands

- Use `scripts/test-fast.sh` or `scripts/test-fast.ps1` for normal changed-area validation.
- Use `scripts/test-full.sh` or `scripts/test-full.ps1` for broad or release-shaped validation.
- Use explicit lanes such as `--api-only`, `--services-only`, `--lms-only`, `--persistence-only`, `--dev-only`, `--evaluation-only`, `--web-only`, or `--backend-only` when appropriate.
- Bypass the scripts only when changing them, diagnosing a failure they conceal, or when the user requests a raw command.

## Ownership

- Api tests prove routes, serialisation, middleware, DI, MCP negotiation, and SPA hosting. MCP endpoint coverage must include required nullable output fields with null values so structured content remains valid against advertised schemas.
- Api tests must override catalogue and operational persistence with isolated temporary databases; they must never read or write the developer's `.data` databases.
- Services tests own application behaviour, validation matrices, and edge cases.
- LMS tests own configuration validation, JSON-RPC request/response plumbing, and upstream failure mapping.
- Persistence tests own schema initialisation/rebuild, bounded catalogue batch durability, source-identity count validation, convergence, refresh-run recovery/logs, retention, filtering, selection correlation, review round-trips, and export privacy.
- Dev tests own command construction, process state, recognised listener detection, and bounded log handling.
- Evaluation tests own corpus parsing and validation, descriptive matching, scoring, and report privacy. They use fictional cases and fake LMS responses.
- Vitest owns frontend API/state/component behaviour.
- Container smoke tests prove release assembly and runtime wiring, not business-rule matrices.

## Style

- Prefer one clear Arrange/Act/Assert flow per test.
- Keep endpoint tests thin and avoid duplicating service-level rule permutations.
- Avoid arbitrary waits; observe readiness or externally visible state with bounded deadlines.
- Use isolated temporary directories and unconditional cleanup for process/container tests.
- Use fictional artist, album, track, playlist, and player names in committed tests and logs.
- Tests must not require the household live LMS unless they are explicitly invoked integration tests.
