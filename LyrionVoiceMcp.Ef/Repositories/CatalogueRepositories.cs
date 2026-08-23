using LyrionVoiceMcp.Ef.Abstractions.Catalogue;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Repositories;

public sealed class CatalogueStateRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityCatalogueState>(ambientDbContextLocator),
        ICatalogueStateRepository
{
    public Task<EntityCatalogueState?> GetAsync(CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
}

public sealed class CatalogueArtistRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase(ambientDbContextLocator), ICatalogueArtistRepository
{
    public async Task<IReadOnlyList<EntityCatalogueArtistLookup>> ListLookupsAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken) =>
        await DbContext.CatalogueArtistLookups
            .Where(item => sourceIds.Contains(item.SourceId))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<EntityCatalogueArtist>> ListAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken) =>
        await DbContext.CatalogueArtists
            .Where(item => sourceIds.Contains(item.SourceId))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> ListReferencedSourceIdsAsync(
        string refreshId,
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken)
    {
        var albumArtists = DbContext.CatalogueAlbums
            .Where(item =>
                item.SeenRefreshId == refreshId
                && item.AlbumArtistSourceId != null
                && sourceIds.Contains(item.AlbumArtistSourceId))
            .Select(item => item.AlbumArtistSourceId!);
        var trackArtists = DbContext.CatalogueTrackArtists
            .Where(item =>
                item.Track.SeenRefreshId == refreshId
                && sourceIds.Contains(item.ArtistSourceId))
            .Select(item => item.ArtistSourceId);
        var referenced = await albumArtists
            .Concat(trackArtists)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return referenced.ToHashSet(StringComparer.Ordinal);
    }

    public void AddLookup(EntityCatalogueArtistLookup artistLookup) =>
        DbContext.CatalogueArtistLookups.Add(artistLookup);

    public void Add(EntityCatalogueArtist artist) =>
        DbContext.CatalogueArtists.Add(artist);

    public async Task<IReadOnlyList<EntityCatalogueArtistLookup>> ListUnseenLookupsAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken) =>
        await DbContext.CatalogueArtistLookups
            .Where(item => item.SeenRefreshId != refreshId)
            .OrderBy(item => item.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<EntityCatalogueArtist>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken) =>
        await DbContext.CatalogueArtists
            .Where(item => item.SeenRefreshId != refreshId)
            .OrderBy(item => item.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public void RemoveLookups(IEnumerable<EntityCatalogueArtistLookup> artistLookups) =>
        DbContext.CatalogueArtistLookups.RemoveRange(artistLookups);

    public void Remove(IEnumerable<EntityCatalogueArtist> artists) =>
        DbContext.CatalogueArtists.RemoveRange(artists);
}

public sealed class CatalogueAlbumRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityCatalogueAlbum>(ambientDbContextLocator),
        ICatalogueAlbumRepository
{
    public async Task<IReadOnlyList<EntityCatalogueAlbum>> ListAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken) =>
        await Query()
            .Where(item => sourceIds.Contains(item.SourceId))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<EntityCatalogueAlbum>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken) =>
        await Query()
            .Where(item => item.SeenRefreshId != refreshId)
            .OrderBy(item => item.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public void Remove(IEnumerable<EntityCatalogueAlbum> albums) => RemoveRange(albums);
}

public sealed class CatalogueGenreRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityCatalogueGenre>(ambientDbContextLocator),
        ICatalogueGenreRepository
{
    public async Task<IReadOnlyList<EntityCatalogueGenre>> ListAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken) =>
        await Query()
            .Where(item => sourceIds.Contains(item.SourceId))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<EntityCatalogueGenre>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken) =>
        await Query()
            .Where(item => item.SeenRefreshId != refreshId)
            .OrderBy(item => item.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public void Remove(IEnumerable<EntityCatalogueGenre> genres) => RemoveRange(genres);
}

public sealed class CatalogueTrackRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityCatalogueTrack>(ambientDbContextLocator),
        ICatalogueTrackRepository
{
    public async Task<IReadOnlyList<EntityCatalogueTrack>> ListForUpdateAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken) =>
        await Query()
            .Where(item => sourceIds.Contains(item.SourceId))
            .Include(item => item.Artists)
            .Include(item => item.Genres)
            .Include(item => item.Statistics)
            .AsSplitQuery()
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<EntityCatalogueTrack>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken) =>
        await Query()
            .Where(item => item.SeenRefreshId != refreshId)
            .OrderBy(item => item.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public void Remove(IEnumerable<EntityCatalogueTrack> tracks) => RemoveRange(tracks);
}

public sealed class CatalogueVirtualLibraryRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityCatalogueVirtualLibrary>(ambientDbContextLocator),
        ICatalogueVirtualLibraryRepository
{
    public async Task<IReadOnlyList<EntityCatalogueVirtualLibrary>> ListAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken) =>
        await Query()
            .Where(item => sourceIds.Contains(item.SourceId))
            .ToArrayAsync(cancellationToken);

    public Task<EntityCatalogueVirtualLibrary?> GetAsync(
        string sourceId,
        CancellationToken cancellationToken) =>
        Query()
            .SingleOrDefaultAsync(item => item.SourceId == sourceId, cancellationToken);

    public async Task<IReadOnlyList<EntityCatalogueVirtualLibraryTrack>> ListTracksAsync(
        int virtualLibraryId,
        IReadOnlyCollection<string> trackSourceIds,
        CancellationToken cancellationToken) =>
        await DbContext.CatalogueVirtualLibraryTracks
            .Where(item =>
                item.VirtualLibraryId == virtualLibraryId
                && trackSourceIds.Contains(item.TrackSourceId))
            .ToArrayAsync(cancellationToken);

    public void AddTrack(EntityCatalogueVirtualLibraryTrack track) =>
        DbContext.CatalogueVirtualLibraryTracks.Add(track);

    public async Task<IReadOnlyList<EntityCatalogueVirtualLibraryTrack>> ListUnseenTracksAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken) =>
        await DbContext.CatalogueVirtualLibraryTracks
            .Where(item => item.SeenRefreshId != refreshId)
            .OrderBy(item => item.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<EntityCatalogueVirtualLibrary>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken) =>
        await Query()
            .Where(item => item.SeenRefreshId != refreshId)
            .OrderBy(item => item.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public void RemoveTracks(IEnumerable<EntityCatalogueVirtualLibraryTrack> tracks) =>
        DbContext.CatalogueVirtualLibraryTracks.RemoveRange(tracks);

    public void Remove(IEnumerable<EntityCatalogueVirtualLibrary> libraries) =>
        RemoveRange(libraries);
}

public sealed class CatalogueValidationRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase(ambientDbContextLocator), ICatalogueValidationRepository
{
    public async Task<EntityCatalogueSeenCounts> ReadSeenCountsAsync(
        string refreshId,
        CancellationToken cancellationToken) => new(
        await DbContext.CatalogueArtistLookups.CountAsync(
            item => item.SeenRefreshId == refreshId,
            cancellationToken),
        await DbContext.CatalogueAlbums.CountAsync(
            item => item.SeenRefreshId == refreshId,
            cancellationToken),
        await DbContext.CatalogueGenres.CountAsync(
            item => item.SeenRefreshId == refreshId,
            cancellationToken),
        await DbContext.CatalogueTracks.CountAsync(
            item => item.SeenRefreshId == refreshId,
            cancellationToken),
        await DbContext.CatalogueVirtualLibraries.CountAsync(
            item => item.SeenRefreshId == refreshId,
            cancellationToken));

    public async Task<IReadOnlyDictionary<string, int>>
        ReadVirtualLibrarySeenTrackCountsAsync(
            string refreshId,
            CancellationToken cancellationToken) =>
        await DbContext.CatalogueVirtualLibraryTracks
            .Where(item => item.SeenRefreshId == refreshId)
            .GroupBy(item => item.VirtualLibrary.SourceId)
            .Select(group => new { SourceId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.SourceId, item => item.Count, cancellationToken);

    public async Task<EntityCatalogueCounts> ReadCountsAsync(
        CancellationToken cancellationToken) => new(
        await DbContext.CatalogueArtists.CountAsync(cancellationToken),
        await DbContext.CatalogueAlbums.CountAsync(cancellationToken),
        await DbContext.CatalogueGenres.CountAsync(cancellationToken),
        await DbContext.CatalogueTracks.CountAsync(cancellationToken),
        await DbContext.CatalogueVirtualLibraries.CountAsync(cancellationToken));

    public async Task<EntityCatalogueReferentialCounts> ReadReferentialCountsAsync(
        CancellationToken cancellationToken)
    {
        var missingTrackAlbums = await DbContext.CatalogueTracks.CountAsync(
            track => track.AlbumSourceId != null
                && !DbContext.CatalogueAlbums.Any(
                    album => album.SourceId == track.AlbumSourceId),
            cancellationToken);
        var missingAlbumArtists = await DbContext.CatalogueAlbums.CountAsync(
            album => album.AlbumArtistSourceId != null
                && !DbContext.CatalogueArtists.Any(
                    artist => artist.SourceId == album.AlbumArtistSourceId),
            cancellationToken);
        var missingTrackArtists = await DbContext.CatalogueTrackArtists.CountAsync(
            trackArtist => !DbContext.CatalogueArtists.Any(
                artist => artist.SourceId == trackArtist.ArtistSourceId),
            cancellationToken);
        var missingGenres = await DbContext.CatalogueTrackGenres.CountAsync(
            trackGenre => !DbContext.CatalogueGenres.Any(
                genre => genre.SourceId == trackGenre.GenreSourceId),
            cancellationToken);
        var missingVirtualLibraryTracks = await DbContext.CatalogueVirtualLibraryTracks.CountAsync(
            member => !DbContext.CatalogueTracks.Any(
                track => track.SourceId == member.TrackSourceId),
            cancellationToken);
        return new EntityCatalogueReferentialCounts(
            missingTrackAlbums,
            missingAlbumArtists + missingTrackArtists,
            missingGenres,
            missingVirtualLibraryTracks);
    }
}

public sealed class CatalogueProjectionRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase(ambientDbContextLocator), ICatalogueProjectionRepository
{
    public async Task<IReadOnlyList<EntityCatalogueProjectionRow>> ReadArtistsAfterAsync(
        string afterSourceId,
        int limit,
        CancellationToken cancellationToken) =>
        await DbContext.CatalogueArtists
            .AsNoTracking()
            .Where(item => string.Compare(item.SourceId, afterSourceId) > 0)
            .OrderBy(item => item.SourceId)
            .Take(limit)
            .Select(item => new EntityCatalogueProjectionRow(
                EntityCatalogueProjectionKind.Artist,
                item.SourceId,
                item.Name,
                null,
                null,
                0))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<EntityCatalogueProjectionRow>> ReadAlbumsAfterAsync(
        string afterSourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        var albums = await DbContext.CatalogueAlbums
            .AsNoTracking()
            .Where(item => string.Compare(item.SourceId, afterSourceId) > 0)
            .OrderBy(item => item.SourceId)
            .Take(limit)
            .Select(item => new AlbumProjection(
                item.SourceId,
                item.Title,
                item.AlbumArtistSourceId,
                item.Year,
                DbContext.CatalogueArtists
                    .Where(artist => artist.SourceId == item.AlbumArtistSourceId)
                    .Select(artist => artist.Name)
                    .SingleOrDefault()))
            .ToArrayAsync(cancellationToken);
        return albums.Select(item => new EntityCatalogueProjectionRow(
            EntityCatalogueProjectionKind.Album,
            item.SourceId,
            item.Title,
            item.Artist,
            null,
            0,
            item.ArtistSourceId is null ? [] : [item.ArtistSourceId],
            item.Year)).ToArray();
    }

    public async Task<IReadOnlyList<EntityCatalogueProjectionRow>> ReadTracksAfterAsync(
        string afterSourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        var tracks = await DbContext.CatalogueTracks
            .AsNoTracking()
            .Where(item => string.Compare(item.SourceId, afterSourceId) > 0)
            .OrderBy(item => item.SourceId)
            .Take(limit)
            .Select(item => new TrackProjection(
                item.Id,
                item.SourceId,
                item.Title,
                item.AlbumSourceId,
                item.Year,
                item.Statistics
                    .Where(statistic => statistic.Source == "lms-core")
                    .Select(statistic => statistic.Rating)
                    .SingleOrDefault()))
            .ToArrayAsync(cancellationToken);
        if (tracks.Length == 0)
        {
            return [];
        }

        var trackIds = tracks.Select(item => item.Id).ToArray();
        var albumIds = tracks
            .Where(item => item.AlbumSourceId is not null)
            .Select(item => item.AlbumSourceId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var artistRows = await DbContext.CatalogueTrackArtists
            .AsNoTracking()
            .Where(item => trackIds.Contains(item.TrackId))
            .Join(
                DbContext.CatalogueArtists,
                trackArtist => trackArtist.ArtistSourceId,
                artist => artist.SourceId,
                (trackArtist, artist) => new { TrackArtist = trackArtist, Artist = artist })
            .OrderBy(item => item.TrackArtist.Id)
            .Select(item => new TrackArtistProjection(
                item.TrackArtist.TrackId,
                item.TrackArtist.Id,
                item.TrackArtist.ArtistSourceId,
                item.Artist.Name))
            .ToArrayAsync(cancellationToken);
        var artistsByTrack = artistRows
            .GroupBy(item => item.TrackId)
            .ToDictionary(
                group => group.Key,
                group => new TrackArtistsProjection(
                    string.Join(", ", group.Select(item => item.Name)),
                    group.Select(item => item.ArtistSourceId).ToArray()));
        var genreRows = await DbContext.CatalogueTrackGenres
            .AsNoTracking()
            .Where(item => trackIds.Contains(item.TrackId))
            .Join(
                DbContext.CatalogueGenres,
                trackGenre => trackGenre.GenreSourceId,
                genre => genre.SourceId,
                (trackGenre, genre) => new TrackGenreProjection(
                    trackGenre.TrackId,
                    genre.Name))
            .ToArrayAsync(cancellationToken);
        var genresByTrack = genreRows
            .GroupBy(item => item.TrackId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(item => item.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        var albums = await DbContext.CatalogueAlbums
            .AsNoTracking()
            .Where(item => albumIds.Contains(item.SourceId))
            .Select(item => new AlbumProjection(
                item.SourceId,
                item.Title,
                item.AlbumArtistSourceId,
                item.Year,
                DbContext.CatalogueArtists
                    .Where(artist => artist.SourceId == item.AlbumArtistSourceId)
                    .Select(artist => artist.Name)
                    .SingleOrDefault()))
            .ToDictionaryAsync(item => item.SourceId, StringComparer.Ordinal, cancellationToken);

        return tracks.Select(track =>
        {
            albums.TryGetValue(track.AlbumSourceId ?? string.Empty, out var album);
            artistsByTrack.TryGetValue(track.Id, out var artists);
            genresByTrack.TryGetValue(track.Id, out var genres);
            return new EntityCatalogueProjectionRow(
                EntityCatalogueProjectionKind.Track,
                track.SourceId,
                track.Title,
                artists?.Names ?? album?.Artist,
                album?.Title,
                track.NativeRating,
                artists?.SourceIds
                    ?? (album?.ArtistSourceId is null
                        ? []
                        : [album.ArtistSourceId]),
                track.Year ?? album?.Year,
                genres ?? []);
        }).ToArray();
    }

    private sealed record TrackProjection(
        int Id,
        string SourceId,
        string Title,
        string? AlbumSourceId,
        int? Year,
        int NativeRating);

    private sealed record TrackArtistProjection(
        int TrackId,
        int RelationId,
        string ArtistSourceId,
        string Name);

    private sealed record TrackArtistsProjection(
        string Names,
        IReadOnlyList<string> SourceIds);

    private sealed record TrackGenreProjection(
        int TrackId,
        string Name);

    private sealed record AlbumProjection(
        string SourceId,
        string Title,
        string? ArtistSourceId,
        int? Year,
        string? Artist);
}
