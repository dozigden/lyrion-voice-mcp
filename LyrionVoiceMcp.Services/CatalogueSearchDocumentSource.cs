using System.Runtime.CompilerServices;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.Catalogue;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;

namespace LyrionVoiceMcp.Services;

public sealed class CatalogueSearchDocumentSource(
    IDbContextScopeFactory scopeFactory,
    ICatalogueProjectionRepository projections,
    ICatalogueLifecycleService catalogue) : ICatalogueSearchDocumentSource
{
    private const int MaximumBatchSize = 500;

    public async IAsyncEnumerable<CatalogueSearchDocumentBatch> ReadBatchesAsync(
        string catalogueRefreshId,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                batchSize,
                $"Catalogue search-document batches must contain between 1 and {MaximumBatchSize} rows.");
        }

        await RequireReadyAsync(
            catalogueRefreshId,
            "The catalogue is not at the successful refresh requested by the search-index job.",
            cancellationToken);
        foreach (var kind in Enum.GetValues<EntityCatalogueProjectionKind>())
        {
            var afterSourceId = string.Empty;
            while (true)
            {
                IReadOnlyList<EntityCatalogueProjectionRow> rows;
                using (var scope = scopeFactory.CreateReadOnly(DbContextScopeOption.ForceCreateNew))
                {
                    rows = kind switch
                    {
                        EntityCatalogueProjectionKind.Artist =>
                            await projections.ReadArtistsAfterAsync(
                                afterSourceId,
                                batchSize,
                                cancellationToken),
                        EntityCatalogueProjectionKind.Album =>
                            await projections.ReadAlbumsAfterAsync(
                                afterSourceId,
                                batchSize,
                                cancellationToken),
                        EntityCatalogueProjectionKind.Track =>
                            await projections.ReadTracksAfterAsync(
                                afterSourceId,
                                batchSize,
                                cancellationToken),
                        _ => throw new InvalidOperationException(
                            "Unknown catalogue projection kind.")
                    };
                }

                if (rows.Count == 0)
                {
                    break;
                }

                yield return new CatalogueSearchDocumentBatch(
                    catalogueRefreshId,
                    rows.Select(ToDocument).ToArray());
                afterSourceId = rows[^1].SourceId;
            }
        }

        await RequireReadyAsync(
            catalogueRefreshId,
            "The catalogue changed while search documents were being streamed.",
            cancellationToken);
    }

    private async Task RequireReadyAsync(
        string catalogueRefreshId,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var state = await catalogue.GetStateAsync(cancellationToken);
        if (state?.Status != CatalogueStateStatus.Succeeded
            || state.Summary is null
            || !string.Equals(
                state.RefreshId,
                catalogueRefreshId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(failureMessage);
        }
    }

    private static CatalogueSearchDocument ToDocument(EntityCatalogueProjectionRow row) => new(
        new MediaIdentity(row.Kind switch
        {
            EntityCatalogueProjectionKind.Artist => MediaEntityKind.Artist,
            EntityCatalogueProjectionKind.Album => MediaEntityKind.Album,
            EntityCatalogueProjectionKind.Track => MediaEntityKind.Track,
            _ => throw new InvalidOperationException("Unknown catalogue projection kind.")
        }, row.SourceId),
        row.Title,
        row.Artist,
        row.Album,
        row.NativeRating,
        row.ArtistSourceIds,
        row.Year,
        (row.GenreNames ?? [])
            .Select(SearchConstraintPolicy.GenreKey)
            .Where(key => key is not null)
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
}
