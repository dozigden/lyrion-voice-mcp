using System.Globalization;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Persistence;

public sealed class SqliteOperationalStore(
    OperationalSettings settings,
    TimeProvider timeProvider) :
    IOperationalStoreInitialiser,
    IJobStore,
    IErrorLogStore,
    IToolCallStore
{
    private const int SchemaVersion = 1;
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = settings.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true,
        DefaultTimeout = 10
    }.ToString();

    public async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settings.DatabasePath)!);
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        await ExecuteAsync(connection, SchemaSql, cancellationToken);

        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT version FROM operational_schema LIMIT 1;";
        var value = await version.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt32(value, CultureInfo.InvariantCulture) != SchemaVersion)
        {
            throw new InvalidOperationException("The operational database schema is not supported.");
        }

        var now = timeProvider.GetUtcNow();
        await using var interruptedCalls = connection.CreateCommand();
        interruptedCalls.CommandText = """
            UPDATE tool_calls
            SET status = 'interrupted',
                completed_at = $completedAt,
                duration_ms = MAX(0, CAST((julianday($completedAt) - julianday(started_at)) * 86400000 AS INTEGER)),
                error_message = 'Tool call was interrupted by server startup.'
            WHERE status = 'running';
            """;
        Add(interruptedCalls, "$completedAt", FormatDate(now));
        await interruptedCalls.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Job> CreateAsync(
        CreateJob request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            long id;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                INSERT INTO jobs (
                    type, status, run_after, payload_json, result_json, correlation_id,
                    created_at, updated_at)
                VALUES (
                    $type, 'pending', $runAfter, $payloadJson, '{}', $correlationId,
                    $now, $now)
                RETURNING id;
                """;
                Add(command, "$type", request.Type);
                Add(command, "$runAfter", FormatDate(request.RunAfter));
                Add(command, "$payloadJson", request.PayloadJson);
                Add(command, "$correlationId", request.CorrelationId);
                Add(command, "$now", FormatDate(now));
                id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            await InsertJobLogAsync(
                connection,
                transaction,
                id,
                JobLogLevel.Information,
                "Job enqueued.",
                null,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (await GetJobAsync(connection, id, cancellationToken))!;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new JobConflictException("A conflicting job already exists.", exception);
        }
    }

    public async Task<JobPage> BrowseAsync(JobQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var filter = BuildJobFilter(query);
        var total = await CountAsync(connection, "jobs", filter.Sql, filter.Parameters, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {JobSummaryColumns}
            FROM jobs
            {filter.Sql}
            ORDER BY created_at DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        AddParameters(command, filter.Parameters);
        Add(command, "$limit", query.Limit);
        Add(command, "$offset", query.Offset);

        var items = new List<JobSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadJobSummary(reader));
        }

        return new JobPage(items, total, query.Offset, query.Limit);
    }

    public async Task<JobDetails?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var job = await GetJobAsync(connection, id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var logs = await ListJobLogsAsync(connection, id, cancellationToken);
        return new JobDetails(job, logs);
    }

    public Task<Job?> GetLatestActiveByTypeAsync(string type, CancellationToken cancellationToken) =>
        GetSingleJobAsync(
            "WHERE type = $type AND status IN ('pending', 'running') ORDER BY created_at DESC, id DESC LIMIT 1",
            [new("$type", type)],
            cancellationToken);

    public Task<Job?> GetLatestByTypeAsync(string type, CancellationToken cancellationToken) =>
        GetSingleJobAsync(
            "WHERE type = $type ORDER BY created_at DESC, id DESC LIMIT 1",
            [new("$type", type)],
            cancellationToken);

    public Task<Job?> GetLatestActiveByCorrelationPrefixesAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken) =>
        GetSingleJobAsync(
            """
            WHERE status IN ('pending', 'running')
              AND (correlation_id LIKE $firstPrefix ESCAPE '\\'
                   OR correlation_id LIKE $secondPrefix ESCAPE '\\')
            ORDER BY created_at DESC, id DESC
            LIMIT 1
            """,
            PrefixParameters(firstPrefix, secondPrefix),
            cancellationToken);

    public Task<Job?> GetLatestStartedByCorrelationPrefixesAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken) =>
        GetSingleJobAsync(
            """
            WHERE started_at IS NOT NULL
              AND (correlation_id LIKE $firstPrefix ESCAPE '\\'
                   OR correlation_id LIKE $secondPrefix ESCAPE '\\')
            ORDER BY started_at DESC, id DESC
            LIMIT 1
            """,
            PrefixParameters(firstPrefix, secondPrefix),
            cancellationToken);

    public async Task<bool> ExistsByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM jobs WHERE correlation_id = $correlationId);";
        Add(command, "$correlationId", correlationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0;
    }

    public async Task<IReadOnlyList<Job>> MarkRunningInterruptedAsync(
        DateTimeOffset completedAt,
        string message,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var running = await ListJobsByStatusAsync(connection, JobStatus.Running, cancellationToken);
        if (running.Count == 0)
        {
            return running;
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var job in running)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE jobs
                SET status = 'failed', error_message = $message,
                    result_json = $resultJson, completed_at = $completedAt, updated_at = $completedAt
                WHERE id = $id AND status = 'running';
                """;
            Add(update, "$message", message);
            Add(update, "$resultJson", System.Text.Json.JsonSerializer.Serialize(new { errorMessage = message }));
            Add(update, "$completedAt", FormatDate(completedAt));
            Add(update, "$id", job.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
            await InsertJobLogAsync(
                connection,
                transaction,
                job.Id,
                JobLogLevel.Error,
                "Job interrupted by server startup.",
                null,
                completedAt,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return running;
    }

    public async Task<Job?> TryStartNextDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        long? id;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE jobs
                SET status = 'running', started_at = COALESCE(started_at, $now),
                    error_message = NULL, updated_at = $now
                WHERE id = (
                    SELECT id FROM jobs
                    WHERE status = 'pending' AND run_after <= $now
                    ORDER BY run_after, id
                    LIMIT 1)
                  AND status = 'pending'
                RETURNING id;
                """;
            Add(command, "$now", FormatDate(now));
            var value = await command.ExecuteScalarAsync(cancellationToken);
            id = value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        if (id is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await InsertJobLogAsync(
            connection,
            transaction,
            id.Value,
            JobLogLevel.Information,
            "Job started.",
            null,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetJobAsync(connection, id.Value, cancellationToken);
    }

    public Task<bool> CompleteAsync(
        long id,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        FinaliseJobAsync(
            id,
            JobStatus.Running,
            JobStatus.Completed,
            resultJson,
            null,
            JobLogLevel.Information,
            "Job completed.",
            completedAt,
            cancellationToken);

    public Task<bool> FailAsync(
        long id,
        string errorMessage,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        FinaliseJobAsync(
            id,
            JobStatus.Running,
            JobStatus.Failed,
            resultJson,
            errorMessage,
            JobLogLevel.Error,
            "Job failed.",
            completedAt,
            cancellationToken);

    public async Task<bool> RequeueAsync(
        long id,
        string resultJson,
        DateTimeOffset runAfter,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE jobs
            SET status = 'pending', result_json = $resultJson, run_after = $runAfter,
                error_message = NULL, updated_at = $updatedAt
            WHERE id = $id AND status = 'running';
            """;
        Add(command, "$resultJson", resultJson);
        Add(command, "$runAfter", FormatDate(runAfter));
        Add(command, "$updatedAt", FormatDate(updatedAt));
        Add(command, "$id", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await InsertJobLogAsync(
            connection,
            transaction,
            id,
            JobLogLevel.Information,
            "Job requeued.",
            resultJson,
            updatedAt,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<bool> CancelAsync(
        long id,
        JobStatus expectedStatus,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        FinaliseJobAsync(
            id,
            expectedStatus,
            JobStatus.Cancelled,
            resultJson,
            null,
            JobLogLevel.Warning,
            "Job cancelled.",
            completedAt,
            cancellationToken);

    public async Task AppendLogAsync(
        long jobId,
        JobLogLevel level,
        string message,
        string? dataJson,
        DateTimeOffset loggedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await InsertJobLogAsync(
            connection,
            null,
            jobId,
            level,
            message,
            dataJson,
            loggedAt,
            cancellationToken);
    }

    public async Task<int> DeleteTerminalBatchBeforeAsync(
        DateTimeOffset completedBefore,
        long excludingJobId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM jobs
            WHERE id IN (
                SELECT id FROM jobs
                WHERE completed_at < $completedBefore
                  AND status IN ('completed', 'failed', 'cancelled')
                  AND id <> $excludingJobId
                ORDER BY completed_at, id
                LIMIT $batchSize);
            """;
        Add(command, "$completedBefore", FormatDate(completedBefore));
        Add(command, "$excludingJobId", excludingJobId);
        Add(command, "$batchSize", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ScheduledJobState?> GetScheduledJobStateAsync(
        string name,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, last_run_at, last_evaluated_at
            FROM scheduled_job_states
            WHERE name = $name;
            """;
        Add(command, "$name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ScheduledJobState(
                reader.GetString(0),
                ParseDate(reader.GetString(1)),
                reader.IsDBNull(2) ? null : ParseDate(reader.GetString(2)))
            : null;
    }

    public async Task UpsertScheduledJobStateAsync(
        ScheduledJobState state,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO scheduled_job_states (name, last_run_at, last_evaluated_at)
            VALUES ($name, $lastRunAt, $lastEvaluatedAt)
            ON CONFLICT(name) DO UPDATE SET
                last_run_at = excluded.last_run_at,
                last_evaluated_at = excluded.last_evaluated_at;
            """;
        Add(command, "$name", state.Name);
        Add(command, "$lastRunAt", FormatDate(state.LastRunAt));
        Add(command, "$lastEvaluatedAt", state.LastEvaluatedAt is null
            ? null
            : FormatDate(state.LastEvaluatedAt.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long?> AddAsync(ErrorLogEntry entry, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO error_logs (
                report_id, occurred_at, source, area, exception_type, message,
                stack_trace, trace_identifier, request_method, request_path,
                job_id, context_json, created_at)
            VALUES (
                $reportId, $occurredAt, $source, $area, $exceptionType, $message,
                $stackTrace, $traceIdentifier, $requestMethod, $requestPath,
                $jobId, $contextJson, $createdAt)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """;
        Add(command, "$reportId", entry.ReportId?.ToString("D"));
        Add(command, "$occurredAt", FormatDate(entry.OccurredAt));
        Add(command, "$source", entry.Source);
        Add(command, "$area", entry.Area);
        Add(command, "$exceptionType", entry.ExceptionType);
        Add(command, "$message", entry.Message);
        Add(command, "$stackTrace", entry.StackTrace);
        Add(command, "$traceIdentifier", entry.TraceIdentifier);
        Add(command, "$requestMethod", entry.RequestMethod);
        Add(command, "$requestPath", entry.RequestPath);
        Add(command, "$jobId", entry.JobId);
        Add(command, "$contextJson", entry.ContextJson);
        Add(command, "$createdAt", FormatDate(timeProvider.GetUtcNow()));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public async Task<bool> ReportExistsAsync(Guid reportId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM error_logs WHERE report_id = $reportId);";
        Add(command, "$reportId", reportId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0;
    }

    public async Task<ErrorLogPage> BrowseAsync(
        ErrorLogQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var filter = BuildErrorFilter(query);
        var total = await CountAsync(connection, "error_logs", filter.Sql, filter.Parameters, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {ErrorLogSummaryColumns}
            FROM error_logs
            {filter.Sql}
            ORDER BY occurred_at DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        AddParameters(command, filter.Parameters);
        Add(command, "$limit", query.Limit);
        Add(command, "$offset", query.Offset);
        var items = new List<ErrorLogSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadErrorLogSummary(reader));
        }

        return new ErrorLogPage(items, total, query.Offset, query.Limit);
    }

    public async Task<ErrorLog?> GetErrorLogAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {ErrorLogColumns} FROM error_logs WHERE id = $id;";
        Add(command, "$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadErrorLog(reader) : null;
    }

    public Task<int> DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken) =>
        DeleteHistoryBatchAsync(
            "error_logs",
            "occurred_at",
            cutoff,
            batchSize,
            cancellationToken);

    public async Task StartAsync(ToolCallStart entry, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tool_calls (
                id, tool_name, status, started_at, arguments_json,
                arguments_truncated, trace_identifier)
            VALUES (
                $id, $toolName, 'running', $startedAt, $argumentsJson,
                $argumentsTruncated, $traceIdentifier);
            """;
        Add(command, "$id", entry.Id);
        Add(command, "$toolName", entry.ToolName);
        Add(command, "$startedAt", FormatDate(entry.StartedAt));
        Add(command, "$argumentsJson", entry.ArgumentsJson);
        Add(command, "$argumentsTruncated", entry.ArgumentsTruncated ? 1 : 0);
        Add(command, "$traceIdentifier", entry.TraceIdentifier);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteAsync(ToolCallCompletion completion, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tool_calls
            SET status = $status, completed_at = $completedAt, duration_ms = $duration,
                result_json = $resultJson, result_truncated = $resultTruncated,
                error_message = $errorMessage, error_log_id = $errorLogId
            WHERE id = $id AND status = 'running';
            """;
        Add(command, "$status", ToText(completion.Status));
        Add(command, "$completedAt", FormatDate(completion.CompletedAt));
        Add(command, "$duration", completion.DurationMilliseconds);
        Add(command, "$resultJson", completion.ResultJson);
        Add(command, "$resultTruncated", completion.ResultTruncated ? 1 : 0);
        Add(command, "$errorMessage", completion.ErrorMessage);
        Add(command, "$errorLogId", completion.ErrorLogId);
        Add(command, "$id", completion.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> MarkRunningInterruptedAsync(
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tool_calls
            SET status = 'interrupted', completed_at = $completedAt,
                duration_ms = MAX(0, CAST((julianday($completedAt) - julianday(started_at)) * 86400000 AS INTEGER)),
                error_message = 'Tool call was interrupted by server startup.'
            WHERE status = 'running';
            """;
        Add(command, "$completedAt", FormatDate(completedAt));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ToolCallPage> BrowseAsync(
        ToolCallQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var filter = BuildToolCallFilter(query);
        var total = await CountAsync(connection, "tool_calls", filter.Sql, filter.Parameters, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {ToolCallSummaryColumns}
            FROM tool_calls
            {filter.Sql}
            ORDER BY started_at DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        AddParameters(command, filter.Parameters);
        Add(command, "$limit", query.Limit);
        Add(command, "$offset", query.Offset);
        var items = new List<ToolCallSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadToolCallSummary(reader));
        }

        return new ToolCallPage(items, total, query.Offset, query.Limit);
    }

    public async Task<ToolCall?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {ToolCallColumns} FROM tool_calls WHERE id = $id;";
        Add(command, "$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadToolCall(reader) : null;
    }

    Task<int> IToolCallStore.DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken) =>
        DeleteHistoryBatchAsync(
            "tool_calls",
            "started_at",
            cutoff,
            batchSize,
            cancellationToken);

    private async Task<bool> FinaliseJobAsync(
        long id,
        JobStatus expectedStatus,
        JobStatus status,
        string resultJson,
        string? errorMessage,
        JobLogLevel logLevel,
        string logMessage,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE jobs
            SET status = $status, result_json = $resultJson, error_message = $errorMessage,
                completed_at = $completedAt, updated_at = $completedAt
            WHERE id = $id AND status = $expectedStatus;
            """;
        Add(command, "$status", ToText(status));
        Add(command, "$resultJson", resultJson);
        Add(command, "$errorMessage", errorMessage);
        Add(command, "$completedAt", FormatDate(completedAt));
        Add(command, "$id", id);
        Add(command, "$expectedStatus", ToText(expectedStatus));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await InsertJobLogAsync(
            connection,
            transaction,
            id,
            logLevel,
            logMessage,
            errorMessage is null ? resultJson : System.Text.Json.JsonSerializer.Serialize(new { errorMessage }),
            completedAt,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<Job?> GetSingleJobAsync(
        string whereSql,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {JobColumns} FROM jobs {whereSql};";
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    private static async Task<Job?> GetJobAsync(
        SqliteConnection connection,
        long id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {JobColumns} FROM jobs WHERE id = $id;";
        Add(command, "$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    private static async Task<IReadOnlyList<Job>> ListJobsByStatusAsync(
        SqliteConnection connection,
        JobStatus status,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {JobColumns} FROM jobs WHERE status = $status ORDER BY id;";
        Add(command, "$status", ToText(status));
        var jobs = new List<Job>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadJob(reader));
        }

        return jobs;
    }

    private static async Task<IReadOnlyList<JobLog>> ListJobLogsAsync(
        SqliteConnection connection,
        long jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, job_id, level, message, data_json, logged_at
            FROM job_logs
            WHERE job_id = $jobId
            ORDER BY id;
            """;
        Add(command, "$jobId", jobId);
        var logs = new List<JobLog>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(new JobLog(
                reader.GetInt64(0),
                reader.GetInt64(1),
                ParseJobLogLevel(reader.GetString(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                ParseDate(reader.GetString(5))));
        }

        return logs;
    }

    private static async Task InsertJobLogAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long jobId,
        JobLogLevel level,
        string message,
        string? dataJson,
        DateTimeOffset loggedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO job_logs (job_id, level, message, data_json, logged_at)
            VALUES ($jobId, $level, $message, $dataJson, $loggedAt);
            """;
        Add(command, "$jobId", jobId);
        Add(command, "$level", ToText(level));
        Add(command, "$message", message);
        Add(command, "$dataJson", dataJson);
        Add(command, "$loggedAt", FormatDate(loggedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> DeleteHistoryBatchAsync(
        string table,
        string dateColumn,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {table}
            WHERE rowid IN (
                SELECT rowid FROM {table}
                WHERE {dateColumn} < $cutoff
                ORDER BY {dateColumn}, rowid
                LIMIT $batchSize);
            """;
        Add(command, "$cutoff", FormatDate(cutoff));
        Add(command, "$batchSize", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountAsync(
        SqliteConnection connection,
        string table,
        string whereSql,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} {whereSql};";
        AddParameters(command, parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA busy_timeout = 10000;", cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Filter BuildJobFilter(JobQuery query)
    {
        var clauses = new List<string>();
        var parameters = new List<SqlParameter>();
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            clauses.Add("type = $type");
            parameters.Add(new SqlParameter("$type", query.Type.Trim()));
        }

        if (query.Status is { } status)
        {
            clauses.Add("status = $status");
            parameters.Add(new SqlParameter("$status", ToText(status)));
        }

        return Filter.Create(clauses, parameters);
    }

    private static Filter BuildErrorFilter(ErrorLogQuery query)
    {
        var clauses = new List<string>();
        var parameters = new List<SqlParameter>();
        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            clauses.Add("source = $source");
            parameters.Add(new SqlParameter("$source", query.Source.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.Area))
        {
            clauses.Add("area = $area");
            parameters.Add(new SqlParameter("$area", query.Area.Trim()));
        }

        return Filter.Create(clauses, parameters);
    }

    private static Filter BuildToolCallFilter(ToolCallQuery query)
    {
        var clauses = new List<string>();
        var parameters = new List<SqlParameter>();
        if (!string.IsNullOrWhiteSpace(query.ToolName))
        {
            clauses.Add("tool_name = $toolName");
            parameters.Add(new SqlParameter("$toolName", query.ToolName.Trim()));
        }

        if (query.Status is { } status)
        {
            clauses.Add("status = $status");
            parameters.Add(new SqlParameter("$status", ToText(status)));
        }

        return Filter.Create(clauses, parameters);
    }

    private static IReadOnlyList<SqlParameter> PrefixParameters(
        string firstPrefix,
        string secondPrefix) =>
    [
        new("$firstPrefix", EscapeLike(firstPrefix) + "%"),
        new("$secondPrefix", EscapeLike(secondPrefix) + "%")
    ];

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static Job ReadJob(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        ParseJobStatus(reader.GetString(2)),
        ParseDate(reader.GetString(3)),
        reader.GetString(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
        reader.IsDBNull(8) ? null : ParseDate(reader.GetString(8)),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        ParseDate(reader.GetString(10)),
        ParseDate(reader.GetString(11)));

    private static JobSummary ReadJobSummary(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        ParseJobStatus(reader.GetString(2)),
        ParseDate(reader.GetString(3)),
        reader.IsDBNull(4) ? null : ParseDate(reader.GetString(4)),
        reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        ParseDate(reader.GetString(7)),
        ParseDate(reader.GetString(8)));

    private static ErrorLog ReadErrorLog(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
        ParseDate(reader.GetString(2)),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetInt64(11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        ParseDate(reader.GetString(13)));

    private static ErrorLogSummary ReadErrorLogSummary(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        ParseDate(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetInt64(7));

    private static ToolCall ReadToolCall(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        ParseToolCallStatus(reader.GetString(2)),
        ParseDate(reader.GetString(3)),
        reader.IsDBNull(4) ? null : ParseDate(reader.GetString(4)),
        reader.IsDBNull(5) ? null : reader.GetInt64(5),
        reader.GetString(6),
        reader.GetInt32(7) != 0,
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.GetInt32(9) != 0,
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetInt64(12));

    private static ToolCallSummary ReadToolCallSummary(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        ParseToolCallStatus(reader.GetString(2)),
        ParseDate(reader.GetString(3)),
        reader.IsDBNull(4) ? null : ParseDate(reader.GetString(4)),
        reader.IsDBNull(5) ? null : reader.GetInt64(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetInt64(7));

    private static JobStatus ParseJobStatus(string value) => value switch
    {
        "pending" => JobStatus.Pending,
        "running" => JobStatus.Running,
        "completed" => JobStatus.Completed,
        "failed" => JobStatus.Failed,
        "cancelled" => JobStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unsupported job status '{value}'.")
    };

    private static JobLogLevel ParseJobLogLevel(string value) => value switch
    {
        "information" => JobLogLevel.Information,
        "warning" => JobLogLevel.Warning,
        "error" => JobLogLevel.Error,
        _ => throw new InvalidOperationException($"Unsupported job log level '{value}'.")
    };

    private static ToolCallStatus ParseToolCallStatus(string value) => value switch
    {
        "running" => ToolCallStatus.Running,
        "succeeded" => ToolCallStatus.Succeeded,
        "tool_error" => ToolCallStatus.ToolError,
        "cancelled" => ToolCallStatus.Cancelled,
        "failed" => ToolCallStatus.Failed,
        "interrupted" => ToolCallStatus.Interrupted,
        _ => throw new InvalidOperationException($"Unsupported tool-call status '{value}'.")
    };

    private static string ToText(JobStatus value) => value.ToString().ToLowerInvariant();

    private static string ToText(JobLogLevel value) => value.ToString().ToLowerInvariant();

    private static string ToText(ToolCallStatus value) => value switch
    {
        ToolCallStatus.ToolError => "tool_error",
        _ => value.ToString().ToLowerInvariant()
    };

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static void AddParameters(
        SqliteCommand command,
        IReadOnlyList<SqlParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            Add(command, parameter.Name, parameter.Value);
        }
    }

    private sealed record SqlParameter(string Name, object? Value);

    private sealed record Filter(string Sql, IReadOnlyList<SqlParameter> Parameters)
    {
        public static Filter Create(
            IReadOnlyList<string> clauses,
            IReadOnlyList<SqlParameter> parameters) =>
            new(clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses), parameters);
    }

    private const string JobColumns = """
        id, type, status, run_after, payload_json, result_json, error_message,
        started_at, completed_at, correlation_id, created_at, updated_at
        """;

    private const string JobSummaryColumns = """
        id, type, status, run_after, started_at, completed_at,
        correlation_id, created_at, updated_at
        """;

    private const string ErrorLogColumns = """
        id, report_id, occurred_at, source, area, exception_type, message,
        stack_trace, trace_identifier, request_method, request_path, job_id,
        context_json, created_at
        """;

    private const string ErrorLogSummaryColumns = """
        id, occurred_at, source, area, exception_type, message,
        trace_identifier, job_id
        """;

    private const string ToolCallColumns = """
        id, tool_name, status, started_at, completed_at, duration_ms,
        arguments_json, arguments_truncated, result_json, result_truncated,
        error_message, trace_identifier, error_log_id
        """;

    private const string ToolCallSummaryColumns = """
        id, tool_name, status, started_at, completed_at, duration_ms,
        trace_identifier, error_log_id
        """;

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS operational_schema (version INTEGER NOT NULL);
        INSERT INTO operational_schema (version)
        SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM operational_schema);

        CREATE TABLE IF NOT EXISTS jobs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            type TEXT NOT NULL,
            status TEXT NOT NULL,
            run_after TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            result_json TEXT NOT NULL,
            error_message TEXT NULL,
            started_at TEXT NULL,
            completed_at TEXT NULL,
            correlation_id TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            CHECK (status IN ('pending', 'running', 'completed', 'failed', 'cancelled'))
        );
        CREATE INDEX IF NOT EXISTS ix_jobs_due ON jobs(status, run_after, id);
        CREATE INDEX IF NOT EXISTS ix_jobs_created ON jobs(created_at DESC, id DESC);
        CREATE INDEX IF NOT EXISTS ix_jobs_type_status ON jobs(type, status, id DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_jobs_active_catalogue_refresh
            ON jobs(type)
            WHERE type = 'catalogue.refresh' AND status IN ('pending', 'running');
        CREATE UNIQUE INDEX IF NOT EXISTS ux_jobs_correlation
            ON jobs(correlation_id) WHERE correlation_id IS NOT NULL;

        CREATE TABLE IF NOT EXISTS job_logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id INTEGER NOT NULL,
            level TEXT NOT NULL,
            message TEXT NOT NULL,
            data_json TEXT NULL,
            logged_at TEXT NOT NULL,
            CHECK (level IN ('information', 'warning', 'error')),
            FOREIGN KEY (job_id) REFERENCES jobs(id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_job_logs_job ON job_logs(job_id, id);

        CREATE TABLE IF NOT EXISTS scheduled_job_states (
            name TEXT PRIMARY KEY,
            last_run_at TEXT NOT NULL,
            last_evaluated_at TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS error_logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            report_id TEXT NULL,
            occurred_at TEXT NOT NULL,
            source TEXT NOT NULL,
            area TEXT NOT NULL,
            exception_type TEXT NOT NULL,
            message TEXT NOT NULL,
            stack_trace TEXT NULL,
            trace_identifier TEXT NULL,
            request_method TEXT NULL,
            request_path TEXT NULL,
            job_id INTEGER NULL,
            context_json TEXT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY (job_id) REFERENCES jobs(id) ON DELETE SET NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_error_logs_report
            ON error_logs(report_id) WHERE report_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_error_logs_occurred ON error_logs(occurred_at DESC, id DESC);
        CREATE INDEX IF NOT EXISTS ix_error_logs_source_area ON error_logs(source, area);
        CREATE INDEX IF NOT EXISTS ix_error_logs_trace ON error_logs(trace_identifier);
        CREATE INDEX IF NOT EXISTS ix_error_logs_job ON error_logs(job_id);

        CREATE TABLE IF NOT EXISTS tool_calls (
            id TEXT PRIMARY KEY,
            tool_name TEXT NOT NULL,
            status TEXT NOT NULL,
            started_at TEXT NOT NULL,
            completed_at TEXT NULL,
            duration_ms INTEGER NULL,
            arguments_json TEXT NOT NULL,
            arguments_truncated INTEGER NOT NULL,
            result_json TEXT NULL,
            result_truncated INTEGER NOT NULL DEFAULT 0,
            error_message TEXT NULL,
            trace_identifier TEXT NULL,
            error_log_id INTEGER NULL,
            CHECK (status IN ('running', 'succeeded', 'tool_error', 'cancelled', 'failed', 'interrupted')),
            FOREIGN KEY (error_log_id) REFERENCES error_logs(id) ON DELETE SET NULL
        );
        CREATE INDEX IF NOT EXISTS ix_tool_calls_started ON tool_calls(started_at DESC, id DESC);
        CREATE INDEX IF NOT EXISTS ix_tool_calls_tool_status ON tool_calls(tool_name, status, started_at DESC);
        CREATE INDEX IF NOT EXISTS ix_tool_calls_error ON tool_calls(error_log_id);
        """;
}
