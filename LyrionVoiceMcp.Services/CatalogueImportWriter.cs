using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.Catalogue;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Services;

public sealed class CatalogueImportWriter(
    IDbContextScopeFactory scopeFactory,
    ICatalogueAlbumRepository albums,
    ICatalogueArtistRepository artists,
    ICatalogueGenreRepository genres,
    ICatalogueTrackRepository tracks,
    ICatalogueVirtualLibraryRepository virtualLibraries) : ICatalogueImportWriter
{
    public async Task WriteAlbumsAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportAlbum> page,
        CancellationToken cancellationToken)
    {
        var imports = UniqueBySourceId(page, item => item.SourceId);
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var existing = (await albums.ListAsync(
            imports.Select(item => item.SourceId).ToArray(),
            cancellationToken)).ToDictionary(item => item.SourceId, StringComparer.Ordinal);
        foreach (var import in imports)
        {
            if (!existing.TryGetValue(import.SourceId, out var entity))
            {
                entity = new EntityCatalogueAlbum { SourceId = import.SourceId };
                albums.Add(entity);
            }

            entity.Title = import.Title;
            entity.AlbumArtistSourceId = import.AlbumArtistSourceId;
            entity.Year = import.Year;
            entity.DiscCount = import.DiscCount;
            entity.IsCompilation = import.IsCompilation;
            entity.ReleaseType = import.ReleaseType;
            entity.ArtworkTrackSourceId = import.ArtworkTrackSourceId;
            entity.ExternalId = import.ExternalId;
            entity.SeenRefreshId = refreshId;
        }

        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteGenresAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportGenre> page,
        CancellationToken cancellationToken)
    {
        var imports = UniqueBySourceId(page, item => item.SourceId);
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var existing = (await genres.ListAsync(
            imports.Select(item => item.SourceId).ToArray(),
            cancellationToken)).ToDictionary(item => item.SourceId, StringComparer.Ordinal);
        foreach (var import in imports)
        {
            if (!existing.TryGetValue(import.SourceId, out var entity))
            {
                entity = new EntityCatalogueGenre { SourceId = import.SourceId };
                genres.Add(entity);
            }

            entity.Name = import.Name;
            entity.SeenRefreshId = refreshId;
        }

        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteTracksAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportTrack> page,
        CancellationToken cancellationToken)
    {
        var imports = UniqueBySourceId(page, item => item.SourceId);
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var existing = (await tracks.ListForUpdateAsync(
            imports.Select(item => item.SourceId).ToArray(),
            cancellationToken)).ToDictionary(item => item.SourceId, StringComparer.Ordinal);
        foreach (var import in imports)
        {
            if (!existing.TryGetValue(import.SourceId, out var entity))
            {
                entity = new EntityCatalogueTrack { SourceId = import.SourceId };
                tracks.Add(entity);
            }

            ApplyTrack(import, entity, refreshId);
        }

        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteArtistsAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportArtist> page,
        CancellationToken cancellationToken)
    {
        var imports = UniqueBySourceId(page, item => item.SourceId);
        var sourceIds = imports.Select(item => item.SourceId).ToArray();
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var existingLookups = (await artists.ListLookupsAsync(sourceIds, cancellationToken))
            .ToDictionary(item => item.SourceId, StringComparer.Ordinal);
        foreach (var import in imports)
        {
            if (!existingLookups.TryGetValue(import.SourceId, out var lookup))
            {
                lookup = new EntityCatalogueArtistLookup { SourceId = import.SourceId };
                artists.AddLookup(lookup);
            }

            lookup.SeenRefreshId = refreshId;
        }

        var referenced = await artists.ListReferencedSourceIdsAsync(
            refreshId,
            sourceIds,
            cancellationToken);
        var referencedImports = imports
            .Where(item => referenced.Contains(item.SourceId))
            .ToArray();
        var existingArtists = (await artists.ListAsync(
            referencedImports.Select(item => item.SourceId).ToArray(),
            cancellationToken)).ToDictionary(item => item.SourceId, StringComparer.Ordinal);
        foreach (var import in referencedImports)
        {
            if (!existingArtists.TryGetValue(import.SourceId, out var entity))
            {
                entity = new EntityCatalogueArtist { SourceId = import.SourceId };
                artists.Add(entity);
            }

            entity.Name = import.Name;
            entity.ExternalId = import.ExternalId;
            entity.SeenRefreshId = refreshId;
        }

        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteVirtualLibrariesAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportVirtualLibrary> page,
        CancellationToken cancellationToken)
    {
        var imports = UniqueBySourceId(page, item => item.SourceId);
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var existing = (await virtualLibraries.ListAsync(
            imports.Select(item => item.SourceId).ToArray(),
            cancellationToken)).ToDictionary(item => item.SourceId, StringComparer.Ordinal);
        foreach (var import in imports)
        {
            if (!existing.TryGetValue(import.SourceId, out var entity))
            {
                entity = new EntityCatalogueVirtualLibrary { SourceId = import.SourceId };
                virtualLibraries.Add(entity);
            }

            entity.Name = import.Name;
            entity.SeenRefreshId = refreshId;
        }

        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteVirtualLibraryTracksAsync(
        string refreshId,
        string librarySourceId,
        IReadOnlyList<string> trackSourceIds,
        CancellationToken cancellationToken)
    {
        var uniqueTrackIds = trackSourceIds.Distinct(StringComparer.Ordinal).ToArray();
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var library = await virtualLibraries.GetAsync(librarySourceId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Virtual library '{librarySourceId}' was not written before its tracks.");
        var existing = (await virtualLibraries.ListTracksAsync(
            library.Id,
            uniqueTrackIds,
            cancellationToken)).ToDictionary(item => item.TrackSourceId, StringComparer.Ordinal);
        foreach (var trackSourceId in uniqueTrackIds)
        {
            if (!existing.TryGetValue(trackSourceId, out var entity))
            {
                entity = new EntityCatalogueVirtualLibraryTrack
                {
                    VirtualLibraryId = library.Id,
                    TrackSourceId = trackSourceId
                };
                virtualLibraries.AddTrack(entity);
            }

            entity.SeenRefreshId = refreshId;
        }

        await scope.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyTrack(
        CatalogueImportTrack source,
        EntityCatalogueTrack target,
        string refreshId)
    {
        target.Title = source.Title;
        target.Subtitle = source.Subtitle;
        target.Url = source.Url;
        target.ContentType = source.ContentType;
        target.IsRemote = source.IsRemote;
        target.ExternalId = source.ExternalId;
        target.AlbumSourceId = source.AlbumSourceId;
        target.Year = source.Year;
        target.DiscNumber = source.DiscNumber;
        target.DiscCount = source.DiscCount;
        target.TrackNumber = source.TrackNumber;
        target.DurationSeconds = source.DurationSeconds;
        target.FileSizeBytes = source.FileSizeBytes;
        target.SampleRate = source.SampleRate;
        target.AddedAtUtc = CatalogueEntityMapper.ToUtcDateTime(source.AddedAt);
        target.SourceModifiedAtUtc = CatalogueEntityMapper.ToUtcDateTime(source.SourceModifiedAt);
        target.SourceUpdatedAtUtc = CatalogueEntityMapper.ToUtcDateTime(source.SourceUpdatedAt);
        target.ReleaseType = source.ReleaseType;
        target.IsCompilation = source.IsCompilation;
        target.ArtworkTrackSourceId = source.ArtworkTrackSourceId;
        target.WorkSourceId = source.WorkSourceId;
        target.WorkTitle = source.WorkTitle;
        target.Performance = source.Performance;
        target.Grouping = source.Grouping;
        target.SeenRefreshId = refreshId;

        target.Artists.Clear();
        target.Artists.AddRange(source.ArtistSourceIds
            .Distinct(StringComparer.Ordinal)
            .Select(sourceId => new EntityCatalogueTrackArtist { ArtistSourceId = sourceId }));
        target.Genres.Clear();
        target.Genres.AddRange(source.GenreSourceIds
            .Distinct(StringComparer.Ordinal)
            .Select(sourceId => new EntityCatalogueTrackGenre { GenreSourceId = sourceId }));
        target.Statistics.Clear();
        target.Statistics.AddRange(source.Statistics
            .GroupBy(item => item.Source, StringComparer.Ordinal)
            .Select(group => group.Last())
            .Select(item => new EntityCatalogueTrackStatistic
            {
                Source = item.Source,
                Rating = item.Rating,
                PlayCount = item.PlayCount,
                LastPlayedAtUtc = CatalogueEntityMapper.ToUtcDateTime(item.LastPlayedAt)
            }));
    }

    private static IReadOnlyList<T> UniqueBySourceId<T>(
        IReadOnlyList<T> items,
        Func<T, string> sourceId) =>
        items.GroupBy(sourceId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
}
