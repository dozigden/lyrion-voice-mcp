using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Api.Endpoints;

public static class OperationalHistoryEndpoints
{
    public static IEndpointRouteBuilder MapOperationalHistoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/jobs", BrowseJobsAsync);
        endpoints.MapGet("/api/jobs/{id:long}", GetJobAsync);
        endpoints.MapPost("/api/jobs/{id:long}/cancel", CancelJobAsync);
        endpoints.MapGet("/api/scheduled-jobs", ListSchedulesAsync);
        endpoints.MapPost("/api/scheduled-jobs/{name}/run", RunScheduleAsync);
        endpoints.MapGet("/api/error-logs", BrowseErrorsAsync);
        endpoints.MapGet("/api/error-logs/{id:long}", GetErrorAsync);
        endpoints.MapGet("/api/tool-calls", BrowseToolCallsAsync);
        endpoints.MapGet("/api/tool-calls/{id}", GetToolCallAsync);
        return endpoints;
    }

    private static async Task<IResult> BrowseJobsAsync(
        int? offset,
        int? limit,
        string? type,
        string? status,
        IJobService service,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptional(status, out JobStatus? parsedStatus))
        {
            return Invalid("Unknown job status.");
        }

        var query = new JobQuery(offset ?? 0, limit ?? 50, EmptyToNull(type), parsedStatus);
        if (JobService.ValidateQuery(query) is { } validation)
        {
            return Invalid(validation);
        }

        var page = await service.BrowseAsync(query, cancellationToken);
        return Results.Ok(new JobPageResponse(
            page.Items.Select(ToSummaryResponse).ToArray(),
            page.Total,
            page.Offset,
            page.Limit,
            service.RetentionDays));
    }

    private static async Task<IResult> GetJobAsync(
        long id,
        IJobService service,
        CancellationToken cancellationToken)
    {
        var details = await service.GetAsync(id, cancellationToken);
        return details is null
            ? Results.NotFound()
            : Results.Ok(new JobDetailsResponse(
                ToResponse(details.Job),
                details.Logs.Select(log => new JobLogResponse(
                    log.Id,
                    ToText(log.Level),
                    log.Message,
                    log.DataJson,
                    log.LoggedAt)).ToArray()));
    }

    private static async Task<IResult> CancelJobAsync(
        long id,
        IJobService service,
        CancellationToken cancellationToken) =>
        await service.RequestCancellationAsync(id, cancellationToken) switch
        {
            JobCancellationAccepted accepted => Results.Accepted(
                $"/api/jobs/{id}",
                ToResponse(accepted.Job)),
            JobCancellationRejected rejected => Results.Conflict(new ApiErrorResponse(rejected.Message)),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };

    private static async Task<IResult> ListSchedulesAsync(
        IScheduledJobService service,
        CancellationToken cancellationToken) => Results.Ok(
        (await service.ListAsync(cancellationToken)).Select(schedule => new ScheduledJobResponse(
            schedule.Name,
            schedule.DisplayName,
            schedule.Enabled,
            schedule.CronExpression,
            schedule.TimeZoneId,
            schedule.LastEvaluatedAt,
            schedule.NextOccurrenceAt,
            ToResponse(schedule.CurrentJob),
            ToResponse(schedule.LastStartedJob))).ToArray());

    private static async Task<IResult> RunScheduleAsync(
        string name,
        IScheduledJobService service,
        CancellationToken cancellationToken) =>
        await service.RunNowAsync(name, cancellationToken) switch
        {
            ScheduledJobRunStarted started => Results.Accepted(
                "/api/jobs",
                new ScheduledJobRunNowResponse(started.EnqueuedCount, started.JobIds)),
            ScheduledJobRunRejected rejected => Results.Conflict(new ApiErrorResponse(rejected.Message)),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };

    private static async Task<IResult> BrowseErrorsAsync(
        int? offset,
        int? limit,
        string? source,
        string? area,
        IErrorLogService service,
        CancellationToken cancellationToken)
    {
        if (!ValidPage(offset, limit))
        {
            return Invalid("Use offset >= 0 and limit between 1 and 200.");
        }

        var page = await service.BrowseAsync(
            new ErrorLogQuery(offset ?? 0, limit ?? 50, EmptyToNull(source), EmptyToNull(area)),
            cancellationToken);
        return Results.Ok(new ErrorLogPageResponse(
            page.Items.Select(ToSummaryResponse).ToArray(),
            page.Total,
            page.Offset,
            page.Limit,
            service.RetentionDays));
    }

    private static async Task<IResult> GetErrorAsync(
        long id,
        IErrorLogService service,
        CancellationToken cancellationToken)
    {
        var item = await service.GetAsync(id, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> BrowseToolCallsAsync(
        int? offset,
        int? limit,
        string? toolName,
        string? status,
        IToolCallHistoryService service,
        CancellationToken cancellationToken)
    {
        if (!ValidPage(offset, limit)
            || !TryParseOptional(status?.Replace("_", string.Empty, StringComparison.Ordinal), out ToolCallStatus? parsedStatus))
        {
            return Invalid("Use a valid tool-call status, offset >= 0 and limit between 1 and 200.");
        }

        var page = await service.BrowseAsync(
            new ToolCallQuery(offset ?? 0, limit ?? 50, EmptyToNull(toolName), parsedStatus),
            cancellationToken);
        return Results.Ok(new ToolCallPageResponse(
            page.Items.Select(ToSummaryResponse).ToArray(),
            page.Total,
            page.Offset,
            page.Limit,
            service.RetentionDays));
    }

    private static async Task<IResult> GetToolCallAsync(
        string id,
        IToolCallHistoryService service,
        CancellationToken cancellationToken)
    {
        var item = await service.GetAsync(id, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
    }

    private static JobResponse ToResponse(Job job) => new(
        job.Id, job.Type, ToText(job.Status), job.RunAfter, job.PayloadJson,
        job.ResultJson, job.ErrorMessage, job.StartedAt, job.CompletedAt,
        job.CorrelationId, job.CreatedAt, job.UpdatedAt);

    private static JobSummaryResponse ToSummaryResponse(JobSummary job) => new(
        job.Id, job.Type, ToText(job.Status), job.RunAfter, job.StartedAt,
        job.CompletedAt, job.CorrelationId, job.CreatedAt, job.UpdatedAt);

    private static ScheduledJobRunResponse? ToResponse(ScheduledJobRun? run) => run is null
        ? null
        : new ScheduledJobRunResponse(run.Id, ToText(run.Status), run.StartedAt);

    private static ErrorLogResponse ToResponse(ErrorLog item) => new(
        item.Id, item.ReportId, item.OccurredAt, item.Source, item.Area,
        item.ExceptionType, item.Message, item.StackTrace, item.TraceIdentifier,
        item.RequestMethod, item.RequestPath, item.JobId, item.ContextJson, item.CreatedAt);

    private static ErrorLogSummaryResponse ToSummaryResponse(ErrorLogSummary item) => new(
        item.Id, item.OccurredAt, item.Source, item.Area, item.ExceptionType,
        item.Message, item.TraceIdentifier, item.JobId);

    private static ToolCallResponse ToResponse(ToolCall item) => new(
        item.Id, item.ToolName, ToText(item.Status), item.StartedAt, item.CompletedAt,
        item.DurationMilliseconds, item.ArgumentsJson, item.ArgumentsTruncated,
        item.ResultJson, item.ResultTruncated, item.ErrorMessage, item.TraceIdentifier,
        item.ErrorLogId);

    private static ToolCallSummaryResponse ToSummaryResponse(ToolCallSummary item) => new(
        item.Id, item.ToolName, ToText(item.Status), item.StartedAt, item.CompletedAt,
        item.DurationMilliseconds, item.TraceIdentifier, item.ErrorLogId);

    private static bool ValidPage(int? offset, int? limit) =>
        offset is null or >= 0 && limit is null or >= 1 and <= 200;

    private static bool TryParseOptional<T>(string? value, out T? parsed) where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<T>(value, true, out var candidate) || !Enum.IsDefined(candidate))
        {
            return false;
        }

        parsed = candidate;
        return true;
    }

    private static IResult Invalid(string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["filters"] = [message] });
    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string ToText(ToolCallStatus value) => value switch
    {
        ToolCallStatus.ToolError => "tool_error",
        _ => value.ToString().ToLowerInvariant()
    };
    private static string ToText(Enum value) => value.ToString().ToLowerInvariant();
}
