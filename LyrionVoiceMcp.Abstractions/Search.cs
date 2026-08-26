using System.Text;

namespace LyrionVoiceMcp.Abstractions;

public enum MediaEntityKind
{
    Artist,
    Album,
    Track,
    Playlist
}

public sealed record SearchResolverDescriptor(
    string Name,
    string Version);

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
    string? Album,
    int NativeRating = 0,
    IReadOnlyList<string>? ArtistIds = null,
    int? Year = null,
    IReadOnlyList<string>? GenreKeys = null);

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
    int Score,
    int NativeRating = 0,
    bool IsExactTitleMatch = false);

public enum RatingMatchMode
{
    Exact,
    AtLeast
}

public sealed record RatingSearchConstraint(
    decimal Rating,
    RatingMatchMode Match);

public sealed record CatalogueTrackSearchConstraint(
    RatingSearchConstraint? RatingConstraint = null,
    string? GenreKey = null,
    int? FromYear = null,
    int? ToYear = null);

public sealed record CatalogueAlbumSearchConstraint(
    int FromYear,
    int ToYear);

public sealed record CatalogueSearchConstraint(
    CatalogueTrackSearchConstraint TrackConstraint,
    CatalogueAlbumSearchConstraint? AlbumConstraint = null)
{
    public static CatalogueSearchConstraint ForRequest(
        CatalogueTrackSearchConstraint trackConstraint)
    {
        ArgumentNullException.ThrowIfNull(trackConstraint);
        CatalogueAlbumSearchConstraint? albumConstraint = null;
        if (trackConstraint.RatingConstraint is null
            && trackConstraint.GenreKey is null
            && trackConstraint.FromYear is not null
            && trackConstraint.ToYear is not null)
        {
            albumConstraint = new CatalogueAlbumSearchConstraint(
                trackConstraint.FromYear.Value,
                trackConstraint.ToYear.Value);
        }

        return new CatalogueSearchConstraint(trackConstraint, albumConstraint);
    }
}

public sealed record CatalogueSearchResponse(
    IReadOnlyList<CatalogueSearchCandidate> Candidates,
    long RetrievalDurationMilliseconds,
    long RerankDurationMilliseconds);

public interface ICatalogueSearchResolver
{
    SearchResolverDescriptor Descriptor { get; }

    Task<CatalogueSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken);

    Task<CatalogueSearchResponse> SearchAsync(
        string query,
        RatingSearchConstraint? ratingConstraint,
        CancellationToken cancellationToken) =>
        ratingConstraint is null
            ? SearchAsync(query, cancellationToken)
            : throw new NotSupportedException(
                "This catalogue search resolver does not support rating constraints.");

    Task<CatalogueSearchResponse> SearchAsync(
        string query,
        CatalogueTrackSearchConstraint? constraint,
        CancellationToken cancellationToken) =>
        constraint is null || constraint is { GenreKey: null, FromYear: null, ToYear: null }
            ? SearchAsync(query, constraint?.RatingConstraint, cancellationToken)
            : throw new NotSupportedException(
                "This catalogue search resolver does not support genre or year constraints.");

    Task<CatalogueSearchResponse> SearchAsync(
        string query,
        CatalogueSearchConstraint? constraint,
        CancellationToken cancellationToken)
    {
        if (constraint is null)
        {
            return SearchAsync(query, cancellationToken);
        }

        if (constraint.AlbumConstraint is null)
        {
            return SearchAsync(query, constraint.TrackConstraint, cancellationToken);
        }

        throw new NotSupportedException(
            "This catalogue search resolver does not support album constraints.");
    }
}

public interface ICatalogueArtistTrackResolver
{
    IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistTracksAsync(
        string artistId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistTracksAsync(
        string artistId,
        CatalogueTrackSearchConstraint? constraint,
        CancellationToken cancellationToken) =>
        constraint is null
            ? ReadArtistTracksAsync(artistId, cancellationToken)
            : throw new NotSupportedException(
                "This catalogue artist-track resolver does not support constraints.");
}

public interface ICatalogueTrackResolver
{
    IAsyncEnumerable<CatalogueSearchCandidate> ReadTracksAsync(
        CatalogueTrackSearchConstraint constraint,
        CancellationToken cancellationToken);
}

public interface ICatalogueAlbumResolver
{
    IAsyncEnumerable<CatalogueSearchCandidate> ReadAlbumsAsync(
        CatalogueAlbumSearchConstraint constraint,
        CancellationToken cancellationToken);
}

public interface ICatalogueArtistAlbumResolver
{
    IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistAlbumsAsync(
        string artistId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistAlbumsAsync(
        string artistId,
        CatalogueAlbumSearchConstraint? constraint,
        CancellationToken cancellationToken) =>
        constraint is null
            ? ReadArtistAlbumsAsync(artistId, cancellationToken)
            : throw new NotSupportedException(
                "This catalogue artist-album resolver does not support constraints.");
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

public sealed record YearSearchRange(
    int RequestedFromYear,
    int RequestedToYear,
    int FromYear,
    int ToYear);

public sealed record YearSearchRangeValidation(
    YearSearchRange? Value,
    string? Error);

public static class SearchConstraintPolicy
{
    public static string? NormaliseGenre(string? genre) =>
        string.IsNullOrWhiteSpace(genre) ? null : genre.Trim();

    public static string? GenreKey(string? genre) =>
        NormaliseGenre(genre)?.ToUpperInvariant();

    public static YearSearchRangeValidation NormaliseYearRange(
        int? fromYear,
        int? toYear,
        int currentYear)
    {
        if (fromYear is null && toYear is null)
        {
            return new YearSearchRangeValidation(null, null);
        }

        if (fromYear is null || toYear is null)
        {
            return new YearSearchRangeValidation(
                null,
                "fromYear and toYear must be supplied together.");
        }

        if (currentYear is < 1000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(currentYear));
        }

        var normalisedFrom = NormaliseYear(fromYear.Value, currentYear);
        var normalisedTo = NormaliseYear(toYear.Value, currentYear);
        if (normalisedFrom is null || normalisedTo is null)
        {
            return new YearSearchRangeValidation(
                null,
                $"fromYear and toYear must each be 0–99 shorthand or a full year from 1000 to {currentYear + 1}.");
        }

        return new YearSearchRangeValidation(
            new YearSearchRange(
                fromYear.Value,
                toYear.Value,
                Math.Min(normalisedFrom.Value, normalisedTo.Value),
                Math.Max(normalisedFrom.Value, normalisedTo.Value)),
            null);
    }

    private static int? NormaliseYear(int year, int currentYear)
    {
        if (year is >= 1000 && year <= currentYear + 1)
        {
            return year;
        }

        if (year is < 0 or > 99)
        {
            return null;
        }

        var currentSuffix = currentYear % 100;
        var currentCentury = currentYear - currentSuffix;
        return year <= currentSuffix
            ? currentCentury + year
            : currentCentury - 100 + year;
    }
}

public static class SearchResultPolicy
{
    public const int ArtistLimit = 5;
    public const int AlbumLimit = 5;
    public const int TopTrackLimit = 5;
    public const int TrackLimit = 30;
    public const int PreparedTrackLimit = TrackLimit + TopTrackLimit;
    public const int TrackCandidateLimit = 80;
    public const int ArtistTrackReservoirLimit = 200;
    public const int AlbumReservoirLimit = 200;
    public const int PlaylistLimit = 5;
}

public sealed record SearchCandidateResult(
    string Reference,
    MediaEntityKind Kind,
    string Title,
    string? Artist,
    string? Album,
    int NativeRating = 0);

public sealed record ExactArtistMatchResult(
    string Name,
    string DiscographyReference);

public sealed record SearchCriteria(
    string? Query,
    RatingSearchConstraint? RatingConstraint = null,
    string? Genre = null,
    int? FromYear = null,
    int? ToYear = null);

public enum SearchRejectionReason
{
    InvalidQuery,
    SearchUnavailable
}

public abstract record SearchOutcome;

public sealed record SearchSucceeded(
    IReadOnlyList<SearchCandidateResult> Results,
    IReadOnlyList<SearchCandidateResult> TopTracks,
    ExactArtistMatchResult? ExactArtistMatch = null) : SearchOutcome;

public sealed record SearchRejected(
    SearchRejectionReason Reason,
    string Message) : SearchOutcome;

public interface ISearchService
{
    Task<SearchOutcome> SearchAsync(
        string query,
        CancellationToken cancellationToken);

    Task<SearchOutcome> SearchAsync(
        SearchCriteria criteria,
        CancellationToken cancellationToken) =>
        criteria is { Query: not null, RatingConstraint: null, Genre: null, FromYear: null, ToYear: null }
            ? SearchAsync(criteria.Query, cancellationToken)
            : throw new NotSupportedException(
                "This search service does not support structured constraints.");
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
