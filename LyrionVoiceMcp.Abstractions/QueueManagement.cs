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
    AmbiguousPlayer,
    MediaNotFound,
    QueueLimitExceeded,
    NoUsableItems
}

public abstract record QueueManagementOutcome;

public sealed record QueueManagementSucceeded(
    string PlayerId,
    int? QueueLength,
    int RequestedItemCount,
    int CompletedItemCount,
    IReadOnlyList<SkippedMediaItem> SkippedItems,
    string? StateRefreshError) : QueueManagementOutcome;

public sealed record QueueManagementRejected(
    QueueManagementRejectionReason Reason,
    string Message) : QueueManagementOutcome;

public sealed record QueueManagementFailed(
    string PlayerId,
    int? QueueLength,
    int RequestedItemCount,
    IReadOnlyList<SkippedMediaItem> SkippedItems,
    string? StateRefreshError,
    string Message) : QueueManagementOutcome;

public interface IQueueManagementService
{
    Task<QueueManagementOutcome> ManageAsync(
        string playerSelector,
        QueueManagementCommand command,
        IReadOnlyList<string>? references,
        CancellationToken cancellationToken);
}
