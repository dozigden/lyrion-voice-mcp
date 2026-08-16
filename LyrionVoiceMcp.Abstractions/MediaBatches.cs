namespace LyrionVoiceMcp.Abstractions;

public enum MediaItemSkipReason
{
    InvalidReference,
    MediaUnavailable,
    QueueCapacity,
    LmsError,
    NotAttempted
}

public sealed record SkippedMediaItem(
    int Index,
    MediaItemSkipReason Reason,
    string Message);

public static class MediaItemSkipReasonExtensions
{
    public static string ToStableName(this MediaItemSkipReason reason) => reason switch
    {
        MediaItemSkipReason.InvalidReference => "invalid_reference",
        MediaItemSkipReason.MediaUnavailable => "media_unavailable",
        MediaItemSkipReason.QueueCapacity => "queue_capacity",
        MediaItemSkipReason.LmsError => "lms_error",
        MediaItemSkipReason.NotAttempted => "not_attempted",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };
}
