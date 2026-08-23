using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Ef.Abstractions.Catalogue;

public interface ICatalogueStateRepository
{
    Task<EntityCatalogueState?> GetAsync(CancellationToken cancellationToken);

    void Add(EntityCatalogueState state);
}

public interface ICatalogueArtistRepository
{
    Task<IReadOnlyList<EntityCatalogueArtistLookup>> ListLookupsAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EntityCatalogueArtist>> ListAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> ListReferencedSourceIdsAsync(
        string refreshId,
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken);

    void AddLookup(EntityCatalogueArtistLookup artistLookup);

    void Add(EntityCatalogueArtist artist);

    Task<IReadOnlyList<EntityCatalogueArtistLookup>> ListUnseenLookupsAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EntityCatalogueArtist>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken);

    void RemoveLookups(IEnumerable<EntityCatalogueArtistLookup> artistLookups);

    void Remove(IEnumerable<EntityCatalogueArtist> artists);
}

public interface ICatalogueAlbumRepository
{
    Task<IReadOnlyList<EntityCatalogueAlbum>> ListAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken);

    void Add(EntityCatalogueAlbum album);

    Task<IReadOnlyList<EntityCatalogueAlbum>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken);

    void Remove(IEnumerable<EntityCatalogueAlbum> albums);
}

public interface ICatalogueGenreRepository
{
    Task<IReadOnlyList<EntityCatalogueGenre>> ListAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken);

    void Add(EntityCatalogueGenre genre);

    Task<IReadOnlyList<EntityCatalogueGenre>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken);

    void Remove(IEnumerable<EntityCatalogueGenre> genres);
}

public interface ICatalogueTrackRepository
{
    Task<IReadOnlyList<EntityCatalogueTrack>> ListForUpdateAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken);

    void Add(EntityCatalogueTrack track);

    Task<IReadOnlyList<EntityCatalogueTrack>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken);

    void Remove(IEnumerable<EntityCatalogueTrack> tracks);
}

public interface ICatalogueVirtualLibraryRepository
{
    Task<IReadOnlyList<EntityCatalogueVirtualLibrary>> ListAsync(
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken);

    Task<EntityCatalogueVirtualLibrary?> GetAsync(
        string sourceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EntityCatalogueVirtualLibraryTrack>> ListTracksAsync(
        int virtualLibraryId,
        IReadOnlyCollection<string> trackSourceIds,
        CancellationToken cancellationToken);

    void AddTrack(EntityCatalogueVirtualLibraryTrack track);

    void Add(EntityCatalogueVirtualLibrary library);

    Task<IReadOnlyList<EntityCatalogueVirtualLibraryTrack>> ListUnseenTracksAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EntityCatalogueVirtualLibrary>> ListUnseenAsync(
        string refreshId,
        int limit,
        CancellationToken cancellationToken);

    void RemoveTracks(IEnumerable<EntityCatalogueVirtualLibraryTrack> tracks);

    void Remove(IEnumerable<EntityCatalogueVirtualLibrary> libraries);
}

public interface ICatalogueValidationRepository
{
    Task<EntityCatalogueSeenCounts> ReadSeenCountsAsync(
        string refreshId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, int>> ReadVirtualLibrarySeenTrackCountsAsync(
        string refreshId,
        CancellationToken cancellationToken);

    Task<EntityCatalogueCounts> ReadCountsAsync(CancellationToken cancellationToken);

    Task<EntityCatalogueReferentialCounts> ReadReferentialCountsAsync(
        CancellationToken cancellationToken);
}

public interface ICatalogueProjectionRepository
{
    Task<IReadOnlyList<EntityCatalogueProjectionRow>> ReadArtistsAfterAsync(
        string afterSourceId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EntityCatalogueProjectionRow>> ReadAlbumsAfterAsync(
        string afterSourceId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EntityCatalogueProjectionRow>> ReadTracksAfterAsync(
        string afterSourceId,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record EntityCatalogueSeenCounts(
    int ArtistLookupCount,
    int AlbumCount,
    int GenreCount,
    int TrackCount,
    int VirtualLibraryCount);

public sealed record EntityCatalogueCounts(
    int ArtistCount,
    int AlbumCount,
    int GenreCount,
    int TrackCount,
    int VirtualLibraryCount);

public sealed record EntityCatalogueReferentialCounts(
    int MissingTrackAlbums,
    int MissingArtists,
    int MissingGenres,
    int MissingVirtualLibraryTracks);

public enum EntityCatalogueProjectionKind
{
    Artist,
    Album,
    Track
}

public sealed record EntityCatalogueProjectionRow(
    EntityCatalogueProjectionKind Kind,
    string SourceId,
    string Title,
    string? Artist,
    string? Album,
    int NativeRating = 0,
    IReadOnlyList<string>? ArtistSourceIds = null);
