namespace LyrionVoiceMcp.Services;

public sealed class ScheduledJobEvaluationGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
