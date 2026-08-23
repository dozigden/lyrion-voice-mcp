using System.Data;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.SearchObservations;

namespace LyrionVoiceMcp.Services;

public sealed class EfSearchObservationStore(
    IDbContextScopeFactory scopeFactory,
    ISearchObservationRepository repository,
    SearchObservationRetentionPolicy retentionPolicy,
    TimeProvider timeProvider) : ISearchObservationStore
{
    public int RetentionDays => retentionPolicy.RetentionDays;

    public async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-RetentionDays);
        using var scope = scopeFactory.CreateWithTransaction(IsolationLevel.Serializable);
        await repository.DeleteOlderThanAsync(cutoff.UtcDateTime, cancellationToken);
        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAsync(
        SearchObservation observation,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateWithTransaction(IsolationLevel.Serializable);
        await repository.DeleteOlderThanAsync(
            timeProvider.GetUtcNow().AddDays(-RetentionDays).UtcDateTime,
            cancellationToken);
        repository.Add(ToEntity(observation));
        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkSelectedAsync(
        IReadOnlyCollection<string> correlationIds,
        DateTimeOffset selectedAt,
        CancellationToken cancellationToken)
    {
        if (correlationIds.Count == 0)
        {
            return;
        }

        using var scope = scopeFactory.Create();
        var candidates = await repository.ListCandidatesForSelectionAsync(
            correlationIds.Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken);
        foreach (var candidate in candidates.Where(item => item.Selection is null))
        {
            candidate.Selection = new EntitySearchObservationSelection
            {
                SelectedAtUtc = selectedAt.UtcDateTime
            };
        }

        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task<SearchObservationPage> BrowseAsync(
        SearchObservationQuery query,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var page = await repository.BrowseAsync(
            new EntitySearchObservationQuery(
                query.Text,
                query.Review switch
                {
                    SearchObservationReviewFilter.Reviewed => true,
                    SearchObservationReviewFilter.Unreviewed => false,
                    _ => null
                },
                ToEntity(query.Result),
                query.Offset,
                query.Limit),
            cancellationToken);
        return new SearchObservationPage(
            page.Items.Select(ToSummary).ToArray(),
            page.Total,
            page.Offset,
            page.Limit);
    }

    public async Task<SearchObservation?> GetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var observation = await repository.GetAsync(id, cancellationToken);
        return observation is null ? null : ToModel(observation);
    }

    public async Task<bool> SaveReviewAsync(
        string id,
        SearchObservationReview review,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.Create();
        var observation = await repository.GetForReviewAsync(id, cancellationToken);
        if (observation is null)
        {
            return false;
        }

        if (observation.Review is null)
        {
            observation.Review = ToEntity(review);
        }
        else
        {
            UpdateEntity(observation.Review, review);
        }

        await scope.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<SearchEvaluationCase>> ExportAsync(
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var observations = await repository.ListForExportAsync(cancellationToken);
        return observations.Select(ToEvaluationCase).ToArray();
    }

    private static EntitySearchObservation ToEntity(SearchObservation observation) => new()
    {
        ObservationId = observation.Id,
        CreatedAtUtc = observation.CreatedAt.UtcDateTime,
        OriginalQuery = observation.OriginalQuery,
        NormalisedQuery = observation.NormalisedQuery,
        Rating = observation.RatingConstraint?.Rating,
        RatingMatch = observation.RatingConstraint is null
            ? null
            : ToEntity(observation.RatingConstraint.Match),
        RequestedKind = observation.RequestedKind is null
            ? null
            : ToEntity(observation.RequestedKind.Value),
        Provider = observation.Provider,
        Collection = observation.Collection,
        Resolver = observation.Resolver,
        ResolverVersion = observation.ResolverVersion,
        Status = ToEntity(observation.Status),
        FailureMessage = observation.FailureMessage,
        TotalDurationMilliseconds = observation.TotalDurationMilliseconds,
        RetrievalDurationMilliseconds = observation.RetrievalDurationMilliseconds,
        ProcessingDurationMilliseconds = observation.ProcessingDurationMilliseconds,
        Requests = observation.Requests.Select((request, index) => new EntitySearchObservationRequest
        {
            Sequence = index + 1,
            Source = request.Source,
            Command = request.Command,
            Status = ToEntity(request.Status),
            FailureMessage = request.FailureMessage,
            DurationMilliseconds = request.DurationMilliseconds,
            ResultCount = request.ResultCount
        }).ToList(),
        Candidates = observation.Candidates.Select(candidate => new EntitySearchObservationCandidate
        {
            Position = candidate.Position,
            CorrelationId = candidate.CorrelationId,
            Kind = ToEntity(candidate.Identity.Kind),
            MediaId = candidate.Identity.Id,
            Title = candidate.Title,
            Artist = candidate.Artist,
            Album = candidate.Album,
            Rating = candidate.Rating,
            IsExactArtistMatch = candidate.IsExactArtistMatch,
            Selection = candidate.SelectedAt is null
                ? null
                : new EntitySearchObservationSelection
                {
                    SelectedAtUtc = candidate.SelectedAt.Value.UtcDateTime
                }
        }).ToList(),
        Review = observation.Review is null ? null : ToEntity(observation.Review)
    };

    private static EntitySearchObservationReview ToEntity(
        SearchObservationReview review)
    {
        var entity = new EntitySearchObservationReview();
        UpdateEntity(entity, review);
        return entity;
    }

    private static void UpdateEntity(
        EntitySearchObservationReview entity,
        SearchObservationReview review)
    {
        entity.Classification = ToEntity(review.Classification);
        entity.ExpectedCorrelationId = review.ExpectedCorrelationId;
        entity.ExpectedKind = review.ExpectedKind is null
            ? null
            : ToEntity(review.ExpectedKind.Value);
        entity.ExpectedTitle = review.ExpectedTitle;
        entity.ExpectedArtist = review.ExpectedArtist;
        entity.ExpectedAlbum = review.ExpectedAlbum;
        entity.Notes = review.Notes;
        entity.IncludeInEvaluation = review.IncludeInEvaluation;
        entity.ReviewedAtUtc = review.ReviewedAt.UtcDateTime;
    }

    private static SearchObservation ToModel(EntitySearchObservation observation) => new(
        observation.ObservationId,
        ToDateTimeOffset(observation.CreatedAtUtc),
        observation.OriginalQuery,
        observation.NormalisedQuery,
        observation.RequestedKind is null ? null : ToModel(observation.RequestedKind.Value),
        observation.Provider,
        observation.Collection,
        observation.Resolver,
        observation.ResolverVersion,
        ToModel(observation.Status),
        observation.FailureMessage,
        observation.TotalDurationMilliseconds,
        observation.RetrievalDurationMilliseconds,
        observation.ProcessingDurationMilliseconds,
        observation.Requests
            .OrderBy(item => item.Sequence)
            .Select(item => new LmsSearchRequestObservation(
                item.Source,
                item.Command,
                ToModel(item.Status),
                item.FailureMessage,
                item.DurationMilliseconds,
                item.ResultCount))
            .ToArray(),
        observation.Candidates
            .OrderBy(item => item.Position)
            .Select(item => new SearchObservationCandidate(
                item.Position,
                item.CorrelationId,
                new MediaIdentity(ToModel(item.Kind), item.MediaId),
                item.Title,
                item.Artist,
                item.Album,
                item.Selection is null
                    ? null
                    : ToDateTimeOffset(item.Selection.SelectedAtUtc),
                item.Rating,
                item.IsExactArtistMatch))
            .ToArray(),
        observation.Review is null ? null : ToModel(observation.Review),
        observation.Rating is null || observation.RatingMatch is null
            ? null
            : new RatingSearchConstraint(
                observation.Rating.Value,
                ToModel(observation.RatingMatch.Value)));

    private static SearchObservationReview ToModel(EntitySearchObservationReview review) =>
        new(
            ToModel(review.Classification),
            review.ExpectedCorrelationId,
            review.ExpectedKind is null ? null : ToModel(review.ExpectedKind.Value),
            review.ExpectedTitle,
            review.ExpectedArtist,
            review.ExpectedAlbum,
            review.Notes,
            review.IncludeInEvaluation,
            ToDateTimeOffset(review.ReviewedAtUtc));

    private static SearchObservationSummary ToSummary(
        EntitySearchObservationSummary summary) => new(
            summary.ObservationId,
            ToDateTimeOffset(summary.CreatedAtUtc),
            summary.OriginalQuery,
            summary.Resolver,
            summary.ResolverVersion,
            ToModel(summary.Status),
            summary.ResultCount,
            summary.SelectedPosition,
            summary.TotalDurationMilliseconds,
            summary.Classification is null ? null : ToModel(summary.Classification.Value),
            summary.IncludeInEvaluation);

    private static SearchEvaluationCase ToEvaluationCase(
        EntitySearchObservation observation)
    {
        var review = observation.Review!;
        return new SearchEvaluationCase(
            observation.OriginalQuery,
            ToModel(review.Classification),
            review.ExpectedKind is null ? null : ToModel(review.ExpectedKind.Value),
            review.ExpectedTitle,
            review.ExpectedArtist,
            review.ExpectedAlbum,
            observation.Candidates
                .OrderBy(candidate => candidate.Position)
                .Select(candidate => new EvaluationCandidate(
                    candidate.Position,
                    ToModel(candidate.Kind),
                    candidate.Title,
                    candidate.Artist,
                    candidate.Album,
                    candidate.Selection is not null,
                    string.Equals(
                        candidate.CorrelationId,
                        review.ExpectedCorrelationId,
                        StringComparison.Ordinal),
                    candidate.Rating))
                .ToArray(),
            observation.Rating is null || observation.RatingMatch is null
                ? null
                : new RatingSearchConstraint(
                    observation.Rating.Value,
                    ToModel(observation.RatingMatch.Value)));
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static EntitySearchObservationResultFilter ToEntity(
        SearchObservationResultFilter value) => value switch
        {
            SearchObservationResultFilter.NoResults =>
                EntitySearchObservationResultFilter.NoResults,
            SearchObservationResultFilter.Selected =>
                EntitySearchObservationResultFilter.Selected,
            SearchObservationResultFilter.Failed =>
                EntitySearchObservationResultFilter.Failed,
            _ => EntitySearchObservationResultFilter.All
        };

    private static EntitySearchObservationStatus ToEntity(
        SearchObservationStatus value) => value switch
        {
            SearchObservationStatus.Completed => EntitySearchObservationStatus.Completed,
            SearchObservationStatus.Failed => EntitySearchObservationStatus.Failed,
            _ => throw new InvalidOperationException("Unknown search observation status.")
        };

    private static EntityRatingMatchMode ToEntity(RatingMatchMode value) => value switch
    {
        RatingMatchMode.Exact => EntityRatingMatchMode.Exact,
        RatingMatchMode.AtLeast => EntityRatingMatchMode.AtLeast,
        _ => throw new InvalidOperationException("Unknown rating match mode.")
    };

    private static RatingMatchMode ToModel(EntityRatingMatchMode value) => value switch
    {
        EntityRatingMatchMode.Exact => RatingMatchMode.Exact,
        EntityRatingMatchMode.AtLeast => RatingMatchMode.AtLeast,
        _ => throw new InvalidOperationException("Unknown stored rating match mode.")
    };

    private static SearchObservationStatus ToModel(
        EntitySearchObservationStatus value) => value switch
        {
            EntitySearchObservationStatus.Completed => SearchObservationStatus.Completed,
            EntitySearchObservationStatus.Failed => SearchObservationStatus.Failed,
            _ => throw new InvalidOperationException("Unknown stored search observation status.")
        };

    private static EntitySearchObservationRequestStatus ToEntity(
        LmsSearchRequestStatus value) => value switch
        {
            LmsSearchRequestStatus.Completed => EntitySearchObservationRequestStatus.Completed,
            LmsSearchRequestStatus.Failed => EntitySearchObservationRequestStatus.Failed,
            _ => throw new InvalidOperationException("Unknown search request status.")
        };

    private static LmsSearchRequestStatus ToModel(
        EntitySearchObservationRequestStatus value) => value switch
        {
            EntitySearchObservationRequestStatus.Completed => LmsSearchRequestStatus.Completed,
            EntitySearchObservationRequestStatus.Failed => LmsSearchRequestStatus.Failed,
            _ => throw new InvalidOperationException("Unknown stored search request status.")
        };

    private static EntityMediaKind ToEntity(MediaEntityKind value) => value switch
    {
        MediaEntityKind.Artist => EntityMediaKind.Artist,
        MediaEntityKind.Album => EntityMediaKind.Album,
        MediaEntityKind.Track => EntityMediaKind.Track,
        MediaEntityKind.Playlist => EntityMediaKind.Playlist,
        _ => throw new InvalidOperationException("Unknown media kind.")
    };

    private static MediaEntityKind ToModel(EntityMediaKind value) => value switch
    {
        EntityMediaKind.Artist => MediaEntityKind.Artist,
        EntityMediaKind.Album => MediaEntityKind.Album,
        EntityMediaKind.Track => MediaEntityKind.Track,
        EntityMediaKind.Playlist => MediaEntityKind.Playlist,
        _ => throw new InvalidOperationException("Unknown stored media kind.")
    };

    private static EntitySearchReviewClassification ToEntity(
        SearchReviewClassification value) => value switch
        {
            SearchReviewClassification.Good => EntitySearchReviewClassification.Good,
            SearchReviewClassification.WrongOrder => EntitySearchReviewClassification.WrongOrder,
            SearchReviewClassification.NoMatch => EntitySearchReviewClassification.NoMatch,
            SearchReviewClassification.Ambiguous => EntitySearchReviewClassification.Ambiguous,
            SearchReviewClassification.TranscriptionError =>
                EntitySearchReviewClassification.TranscriptionError,
            SearchReviewClassification.Other => EntitySearchReviewClassification.Other,
            _ => throw new InvalidOperationException("Unknown search review classification.")
        };

    private static SearchReviewClassification ToModel(
        EntitySearchReviewClassification value) => value switch
        {
            EntitySearchReviewClassification.Good => SearchReviewClassification.Good,
            EntitySearchReviewClassification.WrongOrder => SearchReviewClassification.WrongOrder,
            EntitySearchReviewClassification.NoMatch => SearchReviewClassification.NoMatch,
            EntitySearchReviewClassification.Ambiguous => SearchReviewClassification.Ambiguous,
            EntitySearchReviewClassification.TranscriptionError =>
                SearchReviewClassification.TranscriptionError,
            EntitySearchReviewClassification.Other => SearchReviewClassification.Other,
            _ => throw new InvalidOperationException("Unknown stored review classification.")
        };
}
