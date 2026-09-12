# Testing Guidance

## Commands

- Use `scripts/test-fast.sh` or `scripts/test-fast.ps1` for normal changed-area validation.
- Use `scripts/test-full.sh` or `scripts/test-full.ps1` for broad or release-shaped validation.
- Use explicit lanes such as `--api-only`, `--services-only`, `--lms-only`, `--persistence-only`, `--search-only`, `--dev-only`, `--evaluation-only`, `--web-only`, or `--backend-only` when appropriate.
- Bypass the scripts only when changing them, diagnosing a failure they conceal, or when the user requests a raw command.

## Ownership

- Api tests prove routes, serialisation, middleware, DI, MCP negotiation, the deployed production-search diagnostic surface, schedule-configuration response and validation contracts, and SPA hosting. MCP endpoint coverage must include required nullable output fields with null values so structured content remains valid against advertised schemas.
- Api tests must override catalogue and operational persistence with isolated temporary databases; they must never read or write the developer's `.data` databases.
- Services tests own application behaviour, schedule precedence/conversion/validation/effective availability/cursor reset and work preservation, opaque-handle expiry/eviction and routing, shared-registry composition across service scopes, durable catalogue-to-index enqueue policy, index-job catalogue validation, and edge cases.
- LMS tests own configuration validation, JSON-RPC request/response plumbing, and upstream failure mapping.
- Persistence tests own EF migration and context-scope behaviour, operational repository constraints and links, scheduled-job configuration persistence and audit fields, singleton catalogue readiness transitions, bounded catalogue batch durability, source-identity count validation, convergence, durable job/log lifecycle, scheduler state, error/tool-call retention and correlation, filtering, selection correlation, review round-trips, and export privacy.
- Search tests own bounded index construction, matching/scoring signals, numeric phonetic handling, publication, compatibility and no-index behaviour.
- Dev tests own command construction, process state, recognised listener detection, and bounded log handling.
- Evaluation tests own corpus parsing and validation, LMS-baseline reporting, descriptive matching, and report privacy. They use fictional cases and fake sources; routine tests never contact a real LMS.
- Vitest owns frontend API/state/component behaviour, including editable schedule loading, saving, errors, refreshed next-run state, and independent run-now actions.
- Container smoke tests prove release assembly and runtime wiring, not business-rule matrices.

## Style

- Prefer one clear Arrange/Act/Assert flow per test.
- Keep endpoint tests thin and avoid duplicating service-level rule permutations.
- Avoid arbitrary waits; observe readiness or externally visible state with bounded deadlines.
- Use isolated temporary directories and unconditional cleanup for process/container tests.
- Use fictional artist, album, track, playlist, and player names in committed tests and logs.
- Tests must not require the household live LMS unless they are explicitly invoked integration tests.

## Provider coverage

- Use fictional-provider tests to prove shared dispatch and failure isolation without coupling core tests to BBC classes. Provider feature tests cover canonical snapshot activation, refresh failure retention, matching, opaque targets and LMS menu/action boundaries.
- The LMS test suite includes an explicitly labelled, read-only BBC integration check. It is skipped unless `LVM_BBC_READONLY_INTEGRATION_URL` is set for that invocation. It only discovers subscriptions and episode audio; never include environment values or returned media in tracked fixtures or results. Playback tests use fake LMS responses; live playback still requires explicit player approval.
