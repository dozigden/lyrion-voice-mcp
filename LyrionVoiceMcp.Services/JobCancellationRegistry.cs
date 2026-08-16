using System.Collections.Concurrent;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class JobCancellationRegistry : IJobCancellationRegistry
{
    private readonly ConcurrentDictionary<long, CancellationTokenSource> activeJobs = new();
    private readonly ConcurrentDictionary<long, byte> cancellationRequests = new();

    public CancellationToken Register(long jobId, CancellationToken stoppingToken)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!activeJobs.TryAdd(jobId, cancellation))
        {
            cancellation.Dispose();
            throw new InvalidOperationException($"Job {jobId} is already registered for cancellation.");
        }

        return cancellation.Token;
    }

    public void Unregister(long jobId)
    {
        if (activeJobs.TryRemove(jobId, out var cancellation))
        {
            cancellation.Dispose();
        }

        cancellationRequests.TryRemove(jobId, out _);
    }

    public bool RequestCancellation(long jobId)
    {
        if (!activeJobs.TryGetValue(jobId, out var cancellation))
        {
            return false;
        }

        cancellationRequests.TryAdd(jobId, 0);
        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            cancellationRequests.TryRemove(jobId, out _);
            return false;
        }
    }

    public bool IsCancellationRequested(long jobId) =>
        cancellationRequests.ContainsKey(jobId);
}
