namespace LyrionVoiceMcp.Abstractions;

public static class QueueLimits
{
    public const int MaximumItems = 300;
}

public sealed record LmsPlayerQueue(
    string PlayerId,
    int? CurrentIndex,
    IReadOnlyList<LmsQueueItem> Items);

public sealed record LmsQueueItem(
    int Index,
    string Title,
    string? Artist,
    string? Album,
    double? DurationSeconds);

public enum QueueRejectionReason
{
    InvalidPlayer,
    PlayerNotFound
}

public abstract record QueueOutcome;

public sealed record QueueSucceeded(LmsPlayerQueue Queue) : QueueOutcome;

public sealed record QueueRejected(
    QueueRejectionReason Reason,
    string Message) : QueueOutcome;

public interface ILmsQueueClient
{
    Task<LmsPlayerQueue> GetQueueAsync(
        string playerId,
        CancellationToken cancellationToken);
}

public interface IQueueService
{
    Task<QueueOutcome> GetQueueAsync(
        string playerId,
        CancellationToken cancellationToken);
}
