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

## Scheduling

- Each `IScheduledJobDefinition` supplies its configuration and one or more deterministic occurrences. Scheduled correlation IDs must identify a unique occurrence; ad-hoc run-now correlations must also be unique per emitted job.
- Cron expressions are evaluated in the configured operational time zone through `ICronOccurrenceCalculator`.
- Scheduler state and jobs are durable. Polling may repeat; idempotent correlation checks prevent duplicate enqueue.
- Catalogue refresh is defined but disabled by default. Retention schedules are enabled by default.

## Error log

- Use `IErrorLogService` for unexpected failures only. Validation and normal business rejections are not exceptions and do not enter the error log.
- API middleware, the job runner, scheduler and MCP filter must link the best available trace, request, job and structured context.
- Error persistence is best effort: a failure to write the error log is reported through `ILogger` and must not replace the original outcome.
- Bound stored fields, but otherwise retain diagnostic values as supplied so failures remain inspectable. Do not add credentials to error contexts. This remains trusted-LAN software and the error UI is not safe to expose publicly.

## MCP tool calls

- Instrument calls centrally with the official SDK call-tool filter. Do not add per-tool observation code.
- Persist ordered arguments and the complete SDK result, including returned tool errors, subject only to the configured explicit JSON bound. Truncation must produce valid explanatory JSON and set the corresponding flag.
- Observation is best effort and must not turn a successful tool call into a failed call.
- Mark abandoned running calls interrupted on startup. Link unexpected failures to the durable error record.
- Error and MCP-call observation writes use independent forced scopes so they survive a failed ambient unit of work. They remain best effort and must not change the original request or tool outcome.

## Administration surface

- Jobs, schedules, errors, MCP calls, and production search-index controls are REST/UI administration features, never MCP tools.
- Maintain lightweight paged summaries and complete detail views. List queries must not load payloads, results, stack traces or context; keep those values and relevant cross-links inspectable through detail routes.
- Retention is enforced by scheduled maintenance jobs and must remain visible where relevant in the UI.
