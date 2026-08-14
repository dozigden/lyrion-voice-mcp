namespace LyrionVoiceMcp.Abstractions;

public enum QueueManagementCommand
{
    Clear,
    Append,
    InsertNext
}

public enum QueueManagementRejectionReason
{
    InvalidPlayer,
    InvalidAction,
    ItemsNotAllowed,
    EmptyItems,
    InvalidReference,
    PlayerNotFound,
    MediaNotFound,
    QueueLimitExceeded
}

public abstract record QueueManagementOutcome;

public sealed record QueueManagementSucceeded(
    string PlayerId,
    int QueueLength) : QueueManagementOutcome;

public sealed record QueueManagementRejected(
    QueueManagementRejectionReason Reason,
    string Message) : QueueManagementOutcome;

public interface IQueueManagementService
{
    Task<QueueManagementOutcome> ManageAsync(
        string playerId,
        QueueManagementCommand command,
        IReadOnlyList<string>? references,
        CancellationToken cancellationToken);
}
