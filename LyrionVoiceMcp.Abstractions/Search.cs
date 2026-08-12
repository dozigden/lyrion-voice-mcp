namespace LyrionVoiceMcp.Abstractions;

public enum MediaEntityKind
{
    Artist,
    Album,
    Track,
    Playlist
}

public sealed record MediaIdentity(
    MediaEntityKind Kind,
    string Id);

public sealed record LmsSearchCandidate(
    MediaIdentity Identity,
    string Title,
    string? Artist,
    string? Album);

public enum LmsSearchRequestStatus
{
    Completed,
    Failed
}

public sealed record LmsSearchRequestObservation(
    string Source,
    string Command,
    LmsSearchRequestStatus Status,
    string? FailureMessage,
    long DurationMilliseconds,
    int ResultCount);

public sealed record LmsSearchResponse(
    IReadOnlyList<LmsSearchCandidate> Candidates,
    IReadOnlyList<LmsSearchRequestObservation> Requests,
    long RetrievalDurationMilliseconds);

public interface ILmsSearchClient
{
    Task<LmsSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken);
}

public sealed record SearchCandidateResult(
    string Reference,
    MediaEntityKind Kind,
    string Title,
    string? Artist,
    string? Album);

public enum SearchRejectionReason
{
    InvalidQuery
}

public abstract record SearchOutcome;

public sealed record SearchSucceeded(
    IReadOnlyList<SearchCandidateResult> Results) : SearchOutcome;

public sealed record SearchRejected(
    SearchRejectionReason Reason,
    string Message) : SearchOutcome;

public interface ISearchService
{
    Task<SearchOutcome> SearchAsync(
        string query,
        CancellationToken cancellationToken);
}

public sealed record SearchResultReferenceValue(
    string CorrelationId,
    MediaIdentity Identity);

public interface ISearchResultReferenceCodec
{
    string Encode(SearchResultReferenceValue value);

    SearchResultReferenceValue? TryDecode(string reference);
}

public class LmsRequestException : Exception
{
    public LmsRequestException(string message)
        : base(message)
    {
    }

    public LmsRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LmsSearchFailedException : LmsRequestException
{
    public LmsSearchFailedException(
        string message,
        LmsSearchResponse response,
        Exception innerException)
        : base(message, innerException)
    {
        Response = response;
    }

    public LmsSearchResponse Response { get; }
}
