using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api.Endpoints;

public static class SearchObservationEndpoints
{
    public static IEndpointRouteBuilder MapSearchObservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/search-observations", BrowseAsync);
        endpoints.MapGet("/api/search-observations/export", ExportAsync);
        endpoints.MapGet("/api/search-observations/{id}", GetAsync);
        endpoints.MapPut("/api/search-observations/{id}/review", SaveReviewAsync);
        return endpoints;
    }

    private static async Task<IResult> BrowseAsync(
        string? query,
        string? review,
        string? result,
        int? offset,
        int? limit,
        ISearchObservationReviewService service,
        ISearchObservationStore store,
        CancellationToken cancellationToken)
    {
        if (!TryParseReviewFilter(review, out var reviewFilter)
            || !TryParseResultFilter(result, out var resultFilter)
            || offset is < 0
            || limit is < 1 or > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["filters"] = ["Use valid review/result filters, offset >= 0, and limit between 1 and 100."]
            });
        }

        var page = await service.BrowseAsync(
            new SearchObservationQuery(query, reviewFilter, resultFilter, offset ?? 0, limit ?? 50),
            cancellationToken);
        return Results.Ok(new SearchObservationPageResponse(
            page.Items.Select(ToSummaryResponse).ToArray(),
            page.Total,
            page.Offset,
            page.Limit,
            store.RetentionDays));
    }

    private static async Task<IResult> GetAsync(
        string id,
        ISearchObservationReviewService service,
        ISearchObservationStore store,
        CancellationToken cancellationToken)
    {
        var observation = await service.GetAsync(id, cancellationToken);
        return observation is null
            ? Results.NotFound()
            : Results.Ok(ToDetailResponse(observation, store.RetentionDays));
    }

    private static async Task<IResult> SaveReviewAsync(
        string id,
        SaveSearchObservationReviewRequest request,
        ISearchObservationReviewService service,
        ISearchObservationStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryParseClassification(request.Classification, out var classification)
            || !TryParseOptionalKind(request.ExpectedKind, out var expectedKind))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["review"] = ["The review classification or expected kind is invalid."]
            });
        }

        var outcome = await service.SaveReviewAsync(
            id,
            new SearchObservationReview(
                classification,
                EmptyToNull(request.ExpectedCorrelationId),
                expectedKind,
                EmptyToNull(request.ExpectedTitle),
                EmptyToNull(request.ExpectedArtist),
                EmptyToNull(request.ExpectedAlbum),
                EmptyToNull(request.Notes),
                request.IncludeInEvaluation,
                timeProvider.GetUtcNow()),
            cancellationToken);

        return outcome switch
        {
            SearchReviewSaved saved => Results.Ok(ToDetailResponse(saved.Observation, store.RetentionDays)),
            SaveSearchReviewRejected { Reason: SaveSearchReviewRejectionReason.NotFound } => Results.NotFound(),
            SaveSearchReviewRejected rejected => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["review"] = [rejected.Message]
            }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> ExportAsync(
        ISearchObservationReviewService service,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var cases = await service.ExportAsync(cancellationToken);
        var response = new SearchEvaluationExportResponse(
            3,
            timeProvider.GetUtcNow(),
            cases.Select(item => new SearchEvaluationCaseResponse(
                item.Query,
                item.RatingConstraint?.Rating,
                item.RatingConstraint is null ? null : ToText(item.RatingConstraint.Match),
                item.Genre,
                item.RequestedFromYear,
                item.RequestedToYear,
                item.EffectiveFromYear,
                item.EffectiveToYear,
                ToText(item.Classification),
                item.ExpectedKind is null ? null : ToText(item.ExpectedKind.Value),
                item.ExpectedTitle,
                item.ExpectedArtist,
                item.ExpectedAlbum,
                item.OriginalCandidates.Select(candidate => new SearchEvaluationCandidateResponse(
                    candidate.Position,
                    ToText(candidate.Kind),
                    candidate.Title,
                    candidate.Artist,
                    candidate.Album,
                    candidate.Rating,
                    candidate.Selected,
                    candidate.Expected)).ToArray())).ToArray());
        return Results.File(
            JsonSerializer.SerializeToUtf8Bytes(response, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            "application/json",
            $"lyrion-search-evaluation-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}.json");
    }

    private static SearchObservationSummaryResponse ToSummaryResponse(SearchObservationSummary item) => new(
        item.Id, item.CreatedAt, item.OriginalQuery, item.Resolver, item.ResolverVersion,
        ToText(item.Status), item.ResultCount, item.SelectedPosition, item.TotalDurationMilliseconds,
        item.Classification is null ? null : ToText(item.Classification.Value), item.IncludeInEvaluation);

    private static SearchObservationDetailResponse ToDetailResponse(SearchObservation item, int retentionDays) => new(
        item.Id, item.CreatedAt, item.OriginalQuery, item.NormalisedQuery,
        item.RatingConstraint?.Rating,
        item.RatingConstraint is null ? null : ToText(item.RatingConstraint.Match),
        item.Genre,
        item.RequestedFromYear,
        item.RequestedToYear,
        item.EffectiveFromYear,
        item.EffectiveToYear,
        item.RequestedKind is null ? null : ToText(item.RequestedKind.Value), item.Provider, item.Collection,
        item.Resolver, item.ResolverVersion, ToText(item.Status), item.FailureMessage,
        item.TotalDurationMilliseconds, item.RetrievalDurationMilliseconds, item.ProcessingDurationMilliseconds,
        item.Requests.Select(request => new SearchRequestObservationResponse(
            request.Source, request.Command, ToText(request.Status), request.FailureMessage,
            request.DurationMilliseconds, request.ResultCount)).ToArray(),
        item.Candidates.Select(candidate => new SearchCandidateObservationResponse(
            candidate.Position, candidate.CorrelationId, ToText(candidate.Identity.Kind), candidate.Title,
            candidate.Artist, candidate.Album, candidate.Rating, candidate.SelectedAt,
            candidate.IsExactArtistMatch)).ToArray(),
        item.Review is null ? null : new SearchObservationReviewResponse(
            ToText(item.Review.Classification), item.Review.ExpectedCorrelationId,
            item.Review.ExpectedKind is null ? null : ToText(item.Review.ExpectedKind.Value),
            item.Review.ExpectedTitle, item.Review.ExpectedArtist, item.Review.ExpectedAlbum,
            item.Review.Notes, item.Review.IncludeInEvaluation, item.Review.ReviewedAt),
        retentionDays);

    private static bool TryParseReviewFilter(string? value, out SearchObservationReviewFilter filter) =>
        Enum.TryParse(value ?? "all", true, out filter) && Enum.IsDefined(filter);

    private static bool TryParseResultFilter(string? value, out SearchObservationResultFilter filter)
    {
        var candidate = value?.Replace("-", string.Empty, StringComparison.Ordinal) ?? "all";
        return Enum.TryParse(candidate, true, out filter) && Enum.IsDefined(filter);
    }

    private static bool TryParseClassification(string? value, out SearchReviewClassification classification)
    {
        var candidate = value?.Replace("_", string.Empty, StringComparison.Ordinal) ?? string.Empty;
        return Enum.TryParse(candidate, true, out classification) && Enum.IsDefined(classification);
    }

    private static bool TryParseOptionalKind(string? value, out MediaEntityKind? kind)
    {
        kind = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<MediaEntityKind>(value, true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return false;
        }

        kind = parsed;
        return true;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string ToText(MediaEntityKind value) => value.ToString().ToLowerInvariant();
    private static string ToText(LmsSearchRequestStatus value) => value.ToString().ToLowerInvariant();
    private static string ToText(SearchObservationStatus value) => value.ToString().ToLowerInvariant();
    private static string ToText(RatingMatchMode value) => value switch
    {
        RatingMatchMode.Exact => "exact",
        RatingMatchMode.AtLeast => "at_least",
        _ => throw new InvalidOperationException("Unknown rating match mode.")
    };
    private static string ToText(SearchReviewClassification value) => value switch
    {
        SearchReviewClassification.WrongOrder => "wrong_order",
        SearchReviewClassification.NoMatch => "no_match",
        SearchReviewClassification.TranscriptionError => "transcription_error",
        _ => value.ToString().ToLowerInvariant()
    };
}
