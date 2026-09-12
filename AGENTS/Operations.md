# Operational Architecture Guidance

Read this before changing background work, scheduling, error capture, retention, or MCP invocation history.

## Durable jobs

- EF job and job-log repositories own focused entity persistence and queries. `IJobService` owns validation, enqueue and cancellation; `IJobRunner` is the only component that starts and finalises handlers. Services own context scopes and save each state-and-log unit atomically.
- Implement background work as a typed `IJobHandler`. Do not create an in-memory queue or a feature-specific run/log table.
- Keep lifecycle mutations behind `IJobLifecycleGate`, register running cancellation tokens, and leave a running row for startup recovery when process shutdown interrupts execution.
- The runner selects and tracks the next due job while holding the process-local lifecycle gate, registers its cancellation token before saving `Pending` to `Running`, and unregisters it if that save fails. This deliberately relies on the single-instance runtime; do not add distributed claim mechanics without a concrete requirement.
- Expected handler outcomes return `JobHandlerResult`; unexpected exceptions are persisted through `IErrorLogService` with the job ID and then fail the job.
- Keep payload and result JSON inspectable and valid. Correlations are stable idempotency keys, not display labels.
- Catalogue refresh and production search-index rebuild are separate jobs. A successful catalogue job queues one correlated production rebuild; the single runner serialises expensive work. Manual rebuilds use unique correlations, reject a concurrent rebuild, and target the current successful catalogue refresh.
- `catalogue.change-check` is cheap durable work that reads an optional source-owned token and may enqueue the ordinary catalogue-refresh job. It performs no ingestion and never mutates catalogue readiness or its successful baseline. Provider-request failures are job failures with warning logs; cancellation propagates.
- After interrupted-job recovery and the first scheduled-job check, startup readiness runs once in the background scheduler. When LMS is configured, it requests a catalogue refresh whenever the catalogue is not successful. Otherwise, it requests an inspectably correlated index rebuild when the successful catalogue has no matching compatible artifact. These checks are independent of recurring schedules. A failed readiness check is retried by the scheduler loop and must not stop ordinary job processing.

## Scheduling

- Each `IScheduledJobDefinition` supplies its effective configuration and one or more deterministic occurrences. Editable catalogue definitions resolve durable operator configuration ahead of deployment defaults while retention definitions remain read-only. Scheduled correlation IDs must identify a unique occurrence; ad-hoc run-now correlations must also be unique per emitted job.
- Cron expressions are evaluated in the configured operational time zone through `ICronOccurrenceCalculator`.
- Scheduler state and jobs are durable. Polling may repeat; idempotent correlation checks prevent duplicate enqueue.
- Full catalogue refresh is configured enabled by default at `0 3 * * *`, but is effectively disabled without a configured source. Its editable daily time has minute precision. The lightweight `catalogue-change-check` schedule is independently enabled by default at `*/5 * * * *` and accepts only 1, 5, 10, 15, 30, or 60 minute intervals. Both use the operational time zone without staggering, offsets, or jitter. Saving either configuration persists its enabled state and canonical cron, resets only its evaluation cursor to the save time, and leaves queued or running jobs untouched. Configuration saves and due-job evaluation share a process-local gate so an evaluation that read the old cursor cannot enqueue an older occurrence after the reset. Existing custom deployment cron remains effective and visible until replaced with a supported simple value. The change-check schedule's first registration establishes scheduler state without running immediately, and an active check suppresses another occurrence. Retention schedules are enabled by default and read-only.

## Error log

- Use `IErrorLogService` for unexpected failures only. Validation and normal business rejections are not exceptions and do not enter the error log.
- API middleware, the job runner, scheduler and MCP filter must link the best available trace, request, job and structured context.
- Error persistence is best effort: a failure to write the error log is reported through `ILogger` and must not replace the original outcome.
- Bound stored fields, but otherwise retain diagnostic values as supplied so failures remain inspectable. Do not add credentials to error contexts. The error UI has no authentication and is not safe to expose publicly.

## MCP tool calls

- Instrument calls centrally with the official SDK call-tool filter. Do not add per-tool observation code.
- Persist ordered arguments and the complete SDK result, including returned tool errors, subject only to the configured explicit JSON bound. Truncation must produce valid explanatory JSON and set the corresponding flag.
- At call start, capture ordered reference display snapshots for `browse.browseRef` and `play.items` or queue-addition `manage_queue.items`. Persist every original reference with nullable bounded metadata, never infer labels from a handle, and keep capture failure isolated from both history recording and the tool outcome.
- Observation is best effort and must not turn a successful tool call into a failed call.
- Mark abandoned running calls interrupted on startup. Link unexpected failures to the durable error record.
- Error and MCP-call observation writes use independent forced scopes so they survive a failed ambient unit of work. They remain best effort and must not change the original request or tool outcome.

## Administration surface

- Jobs, schedules, errors, MCP calls and production search-index controls are REST/UI administration features, never MCP tools. Search observations retain their administration API and persisted winning resolver match signal, but have no frontend pages.
- `PUT /api/scheduled-jobs/{name}/configuration` accepts only editable catalogue schedules and their matching simple interval or daily-time value. Validation, conversion to canonical cron, configured-versus-effective availability, and cursor reset remain Services policy rather than endpoint logic.
- Maintain lightweight paged summaries and complete detail views. Error and tool-call history endpoints accept at most 100 rows per page. List queries must not load results, stack traces or context; keep those values and relevant cross-links inspectable through detail routes. Tool-call list queries project the already bounded arguments, reference snapshots and truncation flags for the requested page only, so Services can derive a nullable, single-line request summary of at most 240 characters. The HTTP list exposes that summary rather than full arguments or snapshots. Other history lists do not load payloads. Summaries use recorded argument fields and persisted reference snapshots only, never live lookups; malformed or truncated requests may have no summary, and unresolved references retain their opaque value as the honest fallback.
- Retention is enforced by scheduled maintenance jobs. Keep the maintenance schedules inspectable in System; the tool log deliberately omits retention text.

## Optional provider work

- Catalogue contributors enqueue their own correlated refresh jobs after full catalogue work. Startup contributors enqueue non-blocking restoration jobs independently of local catalogue readiness. The shared runner and scheduler contain no BBC-specific dispatch.
- BBC Sounds owns `bbc-sounds.refresh` and `bbc-sounds.restore-index`. Refresh records unavailable/empty success separately from failed reads, preserving the last successful snapshot on failure. Restoration reads canonical EF subscriptions without contacting LMS. Existing job results and logs expose provider outcomes; there is no provider enable switch or separate schedule.
