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

public interface ILmsSearchClient
{
    Task<IReadOnlyList<LmsSearchCandidate>> SearchAsync(
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

public sealed class LmsRequestException : Exception
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
