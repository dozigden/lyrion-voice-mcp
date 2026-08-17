using System.Text;

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

public interface ILmsPlaylistSearchClient
{
    Task<LmsSearchResponse> SearchPlaylistsAsync(
        string query,
        CancellationToken cancellationToken);
}

public sealed record CatalogueSearchDocument(
    MediaIdentity Identity,
    string Title,
    string? Artist,
    string? Album);

public sealed record CatalogueSearchDocumentBatch(
    string CatalogueRefreshId,
    IReadOnlyList<CatalogueSearchDocument> Documents);

public interface ICatalogueSearchDocumentSource
{
    IAsyncEnumerable<CatalogueSearchDocumentBatch> ReadBatchesAsync(
        string catalogueRefreshId,
        int batchSize,
        CancellationToken cancellationToken);
}

public sealed record CatalogueSearchCandidate(
    MediaIdentity Identity,
    string Title,
    string? Artist,
    string? Album,
    int Score);

public sealed record CatalogueSearchResponse(
    IReadOnlyList<CatalogueSearchCandidate> Candidates,
    long RetrievalDurationMilliseconds,
    long RerankDurationMilliseconds);

public interface ICatalogueSearchResolver
{
    Task<CatalogueSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken);
}

public sealed class CatalogueSearchUnavailableException(string message) : Exception(message);

public static class SearchQueryPolicy
{
    public const int MaximumLength = 500;
    public const int MaximumTokenCount = 20;

    public static int CountNormalisedTokens(string value)
    {
        var count = 0;
        var insideToken = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (!insideToken)
                {
                    count++;
                    insideToken = true;
                }

                continue;
            }

            insideToken = false;
        }

        return count;
    }
}

public sealed record SearchCandidateResult(
    string Reference,
    MediaEntityKind Kind,
    string Title,
    string? Artist,
    string? Album);

public enum SearchRejectionReason
{
    InvalidQuery,
    SearchUnavailable
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
