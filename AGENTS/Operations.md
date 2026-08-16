# Operational Architecture Guidance

Read this before changing background work, scheduling, error capture, retention, or MCP invocation history.

## Durable jobs

- `IJobStore` owns durable lifecycle state, payload/result JSON, correlation and ordered logs. `IJobService` owns validation, enqueue and cancellation; `IJobRunner` is the only component that starts and finalises handlers.
- Implement background work as a typed `IJobHandler`. Do not create an in-memory queue or a feature-specific run/log table.
- Keep lifecycle mutations behind `IJobLifecycleGate`, register running cancellation tokens, and leave a running row for startup recovery when process shutdown interrupts execution.
- Expected handler outcomes return `JobHandlerResult`; unexpected exceptions are persisted through `IErrorLogService` with the job ID and then fail the job.
- Keep payload and result JSON inspectable and valid. Correlations are stable idempotency keys, not display labels.
- Catalogue refresh and deployed search-index rebuilds are separate jobs. A successful catalogue job queues one correlated rebuild per resolver; the single runner serialises their expensive work. Manual rebuilds use unique correlations, reject a concurrent rebuild of the same resolver, and always target the current successful catalogue refresh.

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

## Administration surface

- Jobs, schedules, errors, MCP calls, and diagnostic search-index controls are REST/UI administration features, never MCP tools.
- Maintain lightweight paged summaries and complete detail views. List queries must not load payloads, results, stack traces or context; keep those values and relevant cross-links inspectable through detail routes.
- Retention is enforced by scheduled maintenance jobs and must remain visible where relevant in the UI.
