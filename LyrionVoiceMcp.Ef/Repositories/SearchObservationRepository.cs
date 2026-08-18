using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.SearchObservations;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Repositories;

public sealed class SearchObservationRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntitySearchObservation>(ambientDbContextLocator),
        ISearchObservationRepository
{
    public async Task<IReadOnlySet<string>> ListExistingObservationIdsAsync(
        IReadOnlyCollection<string> observationIds,
        CancellationToken cancellationToken)
    {
        if (observationIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var ids = await Query()
            .Where(item => observationIds.Contains(item.ObservationId))
            .Select(item => item.ObservationId)
            .ToArrayAsync(cancellationToken);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<EntitySearchObservationPage> BrowseAsync(
        EntitySearchObservationQuery query,
        CancellationToken cancellationToken)
    {
        var observations = Query().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var pattern = $"%{EscapeLike(query.Text.Trim())}%";
            observations = observations.Where(item =>
                EF.Functions.Like(item.OriginalQuery, pattern, "\\"));
        }

        observations = query.Reviewed switch
        {
            true => observations.Where(item => item.Review != null),
            false => observations.Where(item => item.Review == null),
            _ => observations
        };
        observations = query.Result switch
        {
            EntitySearchObservationResultFilter.NoResults => observations.Where(item =>
                item.Status == EntitySearchObservationStatus.Completed
                && !item.Candidates.Any()),
            EntitySearchObservationResultFilter.Selected => observations.Where(item =>
                item.Candidates.Any(candidate => candidate.Selection != null)),
            EntitySearchObservationResultFilter.Failed => observations.Where(item =>
                item.Status == EntitySearchObservationStatus.Failed),
            _ => observations
        };

        var total = await observations.CountAsync(cancellationToken);
        var items = await observations
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(item => new EntitySearchObservationSummary(
                item.ObservationId,
                item.CreatedAtUtc,
                item.OriginalQuery,
                item.Resolver,
                item.ResolverVersion,
                item.Status,
                item.Candidates.Count,
                item.Candidates
                    .Where(candidate => candidate.Selection != null)
                    .Select(candidate => (int?)candidate.Position)
                    .Min(),
                item.TotalDurationMilliseconds,
                item.Review == null ? null : item.Review.Classification,
                item.Review != null && item.Review.IncludeInEvaluation))
            .ToArrayAsync(cancellationToken);

        return new EntitySearchObservationPage(
            items,
            total,
            query.Offset,
            query.Limit);
    }

    public Task<EntitySearchObservation?> GetAsync(
        string observationId,
        CancellationToken cancellationToken) =>
        FullObservationQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ObservationId == observationId,
                cancellationToken);

    public Task<EntitySearchObservation?> GetForReviewAsync(
        string observationId,
        CancellationToken cancellationToken) =>
        Query()
            .Include(item => item.Review)
            .SingleOrDefaultAsync(
                item => item.ObservationId == observationId,
                cancellationToken);

    public async Task<IReadOnlyList<EntitySearchObservationCandidate>>
        ListCandidatesForSelectionAsync(
            IReadOnlyCollection<string> correlationIds,
            CancellationToken cancellationToken)
    {
        if (correlationIds.Count == 0)
        {
            return [];
        }

        return await DbContext.SearchObservationCandidates
            .Include(item => item.Selection)
            .Where(item => correlationIds.Contains(item.CorrelationId))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EntitySearchObservation>> ListForExportAsync(
        CancellationToken cancellationToken) =>
        await Query()
            .AsNoTracking()
            .Where(item =>
                item.Status == EntitySearchObservationStatus.Completed
                && item.Review != null
                && item.Review.IncludeInEvaluation)
            .Include(item => item.Review)
            .Include(item => item.Candidates)
                .ThenInclude(item => item.Selection)
            .AsSplitQuery()
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);

    public Task<int> DeleteOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken) =>
        Query()
            .Where(item => item.CreatedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);

    private IQueryable<EntitySearchObservation> FullObservationQuery() =>
        Query()
            .Include(item => item.Requests)
            .Include(item => item.Candidates)
                .ThenInclude(item => item.Selection)
            .Include(item => item.Review)
            .AsSplitQuery();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
