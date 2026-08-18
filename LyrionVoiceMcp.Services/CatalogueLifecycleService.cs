using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.Catalogue;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Services;

public sealed class CatalogueLifecycleService(
    IDbContextScopeFactory scopeFactory,
    ICatalogueStateRepository states,
    ICatalogueArtistRepository artists,
    ICatalogueAlbumRepository albums,
    ICatalogueGenreRepository genres,
    ICatalogueTrackRepository tracks,
    ICatalogueVirtualLibraryRepository virtualLibraries,
    ICatalogueValidationRepository validation,
    TimeProvider timeProvider) : ICatalogueLifecycleService
{
    private const int CleanupBatchSize = 500;

    public async Task RecoverInterruptedRefreshAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var state = await states.GetAsync(cancellationToken);
        if (state?.Status != EntityCatalogueStateStatus.Running)
        {
            return;
        }

        state.Status = EntityCatalogueStateStatus.Interrupted;
        state.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task<CatalogueState?> GetStateAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly(DbContextScopeOption.ForceCreateNew);
        var state = await states.GetAsync(cancellationToken);
        return state is null ? null : CatalogueEntityMapper.ToModel(state);
    }

    public async Task<CatalogueSummary?> GetSummaryAsync(
        CancellationToken cancellationToken) =>
        (await GetStateAsync(cancellationToken))?.Summary;

    public async Task BeginRefreshAsync(
        string refreshId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var state = await states.GetAsync(cancellationToken);
        if (state is null)
        {
            state = new EntityCatalogueState { Id = 1 };
            states.Add(state);
        }

        state.RefreshId = refreshId;
        state.Status = EntityCatalogueStateStatus.Running;
        state.StartedAtUtc = startedAt.UtcDateTime;
        ClearCompletion(state);
        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task<CatalogueRefreshCompletion> CompleteRefreshAsync(
        string refreshId,
        CatalogueSourceReadResult source,
        DateTimeOffset completedAt,
        int existingWarningCount,
        CancellationToken cancellationToken)
    {
        await RequireRunningRefreshAsync(refreshId, cancellationToken);
        await ValidateSourceCountsAsync(refreshId, source, cancellationToken);

        await DeleteUnseenVirtualLibraryTracksAsync(refreshId, cancellationToken);
        await DeleteUnseenVirtualLibrariesAsync(refreshId, cancellationToken);
        await DeleteUnseenTracksAsync(refreshId, cancellationToken);
        await DeleteUnseenAlbumsAsync(refreshId, cancellationToken);
        await DeleteUnseenGenresAsync(refreshId, cancellationToken);
        await DeleteUnseenArtistsAsync(refreshId, cancellationToken);
        await DeleteUnseenArtistLookupsAsync(refreshId, cancellationToken);

        EntityCatalogueCounts counts;
        EntityCatalogueReferentialCounts references;
        using (var scope = scopeFactory.CreateReadOnly(DbContextScopeOption.ForceCreateNew))
        {
            counts = await validation.ReadCountsAsync(cancellationToken);
            references = await validation.ReadReferentialCountsAsync(cancellationToken);
        }

        var warnings = CreateWarnings(references);
        var summary = new CatalogueSummary(
            source.Source.Id,
            source.Source.Provider,
            source.Source.Revision,
            source.Source.Version,
            source.CapturedAt,
            source.SourceLastScanAt,
            completedAt,
            counts.ArtistCount,
            counts.AlbumCount,
            counts.GenreCount,
            counts.TrackCount,
            counts.VirtualLibraryCount,
            existingWarningCount + warnings.Count);
        await StoreCompletedRefreshAsync(refreshId, summary, cancellationToken);
        return new CatalogueRefreshCompletion(summary, warnings);
    }

    public async Task FinishRefreshAsync(
        string refreshId,
        CatalogueStateStatus status,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        if (status is CatalogueStateStatus.Running or CatalogueStateStatus.Succeeded)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "An unsuccessful refresh must finish with a non-success terminal status.");
        }

        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var state = await states.GetAsync(cancellationToken);
        if (state is null
            || state.Status != EntityCatalogueStateStatus.Running
            || !string.Equals(state.RefreshId, refreshId, StringComparison.Ordinal))
        {
            return;
        }

        state.Status = status switch
        {
            CatalogueStateStatus.Failed => EntityCatalogueStateStatus.Failed,
            CatalogueStateStatus.Cancelled => EntityCatalogueStateStatus.Cancelled,
            CatalogueStateStatus.Interrupted => EntityCatalogueStateStatus.Interrupted,
            _ => throw new InvalidOperationException("Unknown catalogue terminal status.")
        };
        state.CompletedAtUtc = completedAt.UtcDateTime;
        await scope.SaveChangesAsync(cancellationToken);
    }

    private async Task RequireRunningRefreshAsync(
        string refreshId,
        CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(cancellationToken);
        if (state?.Status != CatalogueStateStatus.Running
            || !string.Equals(state.RefreshId, refreshId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The catalogue refresh state was not running when completion started.");
        }
    }

    private async Task ValidateSourceCountsAsync(
        string refreshId,
        CatalogueSourceReadResult source,
        CancellationToken cancellationToken)
    {
        EntityCatalogueSeenCounts counts;
        IReadOnlyDictionary<string, int> membershipCounts;
        using (var scope = scopeFactory.CreateReadOnly(DbContextScopeOption.ForceCreateNew))
        {
            counts = await validation.ReadSeenCountsAsync(refreshId, cancellationToken);
            membershipCounts = await validation.ReadVirtualLibrarySeenTrackCountsAsync(
                refreshId,
                cancellationToken);
        }

        ValidateCount("artist lookup", counts.ArtistLookupCount, source.ArtistLookupCount);
        ValidateCount("albums", counts.AlbumCount, source.AlbumCount);
        ValidateCount("genres", counts.GenreCount, source.GenreCount);
        ValidateCount("tracks", counts.TrackCount, source.TrackCount);
        ValidateCount(
            "virtual libraries",
            counts.VirtualLibraryCount,
            source.VirtualLibraryCount);
        if (source.VirtualLibraryMemberships.Count != source.VirtualLibraryCount)
        {
            throw new InvalidOperationException(
                "The catalogue refresh did not report membership counts for every virtual library.");
        }

        foreach (var membership in source.VirtualLibraryMemberships)
        {
            membershipCounts.TryGetValue(membership.LibrarySourceId, out var actualCount);
            ValidateCount(
                $"virtual library '{membership.LibrarySourceId}' tracks",
                actualCount,
                membership.TrackCount);
        }
    }

    private static void ValidateCount(string collection, int actual, int expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"The catalogue refresh wrote {actual} unique {collection} rows, but LMS returned {expected} rows.");
        }
    }

    private async Task DeleteUnseenVirtualLibraryTracksAsync(
        string refreshId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            var unseen = await virtualLibraries.ListUnseenTracksAsync(
                refreshId,
                CleanupBatchSize,
                cancellationToken);
            if (unseen.Count == 0)
            {
                return;
            }

            virtualLibraries.RemoveTracks(unseen);
            await scope.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DeleteUnseenVirtualLibrariesAsync(
        string refreshId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            var unseen = await virtualLibraries.ListUnseenAsync(
                refreshId,
                CleanupBatchSize,
                cancellationToken);
            if (unseen.Count == 0)
            {
                return;
            }

            virtualLibraries.Remove(unseen);
            await scope.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DeleteUnseenTracksAsync(
        string refreshId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            var unseen = await tracks.ListUnseenAsync(
                refreshId,
                CleanupBatchSize,
                cancellationToken);
            if (unseen.Count == 0)
            {
                return;
            }

            tracks.Remove(unseen);
            await scope.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DeleteUnseenAlbumsAsync(
        string refreshId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            var unseen = await albums.ListUnseenAsync(
                refreshId,
                CleanupBatchSize,
                cancellationToken);
            if (unseen.Count == 0)
            {
                return;
            }

            albums.Remove(unseen);
            await scope.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DeleteUnseenGenresAsync(
        string refreshId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            var unseen = await genres.ListUnseenAsync(
                refreshId,
                CleanupBatchSize,
                cancellationToken);
            if (unseen.Count == 0)
            {
                return;
            }

            genres.Remove(unseen);
            await scope.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DeleteUnseenArtistsAsync(
        string refreshId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            var unseen = await artists.ListUnseenAsync(
                refreshId,
                CleanupBatchSize,
                cancellationToken);
            if (unseen.Count == 0)
            {
                return;
            }

            artists.Remove(unseen);
            await scope.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DeleteUnseenArtistLookupsAsync(
        string refreshId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            var unseen = await artists.ListUnseenLookupsAsync(
                refreshId,
                CleanupBatchSize,
                cancellationToken);
            if (unseen.Count == 0)
            {
                return;
            }

            artists.RemoveLookups(unseen);
            await scope.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task StoreCompletedRefreshAsync(
        string refreshId,
        CatalogueSummary summary,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var state = await states.GetAsync(cancellationToken);
        if (state is null
            || state.Status != EntityCatalogueStateStatus.Running
            || !string.Equals(state.RefreshId, refreshId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The catalogue refresh state was not running when completion was recorded.");
        }

        state.Status = EntityCatalogueStateStatus.Succeeded;
        state.CompletedAtUtc = summary.RefreshedAt.UtcDateTime;
        state.SourceId = summary.SourceId;
        state.SourceProvider = summary.Provider;
        state.SourceRevision = summary.SourceRevision;
        state.SourceVersion = summary.SourceVersion;
        state.CapturedAtUtc = summary.CapturedAt.UtcDateTime;
        state.SourceLastScanAtUtc = summary.SourceLastScanAt?.UtcDateTime;
        state.RefreshedAtUtc = summary.RefreshedAt.UtcDateTime;
        state.ArtistCount = summary.ArtistCount;
        state.AlbumCount = summary.AlbumCount;
        state.GenreCount = summary.GenreCount;
        state.TrackCount = summary.TrackCount;
        state.VirtualLibraryCount = summary.VirtualLibraryCount;
        state.WarningCount = summary.WarningCount;
        await scope.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<CatalogueRefreshWarning> CreateWarnings(
        EntityCatalogueReferentialCounts counts)
    {
        var warnings = new List<CatalogueRefreshWarning>();
        AddWarning(
            warnings,
            counts.MissingTrackAlbums,
            "Track album references were not present in the imported album set.");
        AddWarning(
            warnings,
            counts.MissingArtists,
            "Track or album artist references were not present in the imported artist set.");
        AddWarning(
            warnings,
            counts.MissingGenres,
            "Track genre references were not present in the imported genre set.");
        AddWarning(
            warnings,
            counts.MissingVirtualLibraryTracks,
            "Virtual-library memberships referenced tracks outside the imported track set.");
        return warnings;
    }

    private static void AddWarning(
        ICollection<CatalogueRefreshWarning> warnings,
        int occurrences,
        string message)
    {
        if (occurrences > 0)
        {
            warnings.Add(new CatalogueRefreshWarning(
                CatalogueRefreshLogLevel.Warning,
                message,
                occurrences,
                null));
        }
    }

    private static void ClearCompletion(EntityCatalogueState state)
    {
        state.CompletedAtUtc = null;
        state.SourceId = null;
        state.SourceProvider = null;
        state.SourceRevision = null;
        state.SourceVersion = null;
        state.CapturedAtUtc = null;
        state.SourceLastScanAtUtc = null;
        state.RefreshedAtUtc = null;
        state.ArtistCount = null;
        state.AlbumCount = null;
        state.GenreCount = null;
        state.TrackCount = null;
        state.VirtualLibraryCount = null;
        state.WarningCount = null;
    }
}
