using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class JobLogWriter(
    IJobStore store,
    TimeProvider timeProvider) : IJobLogWriter
{
    public Task WriteAsync(
        long jobId,
        JobLogLevel level,
        string message,
        object? data,
        CancellationToken cancellationToken) =>
        store.AppendLogAsync(
            jobId,
            level,
            message,
            data is null ? null : JsonSerializer.Serialize(data),
            timeProvider.GetUtcNow(),
            cancellationToken);
}
