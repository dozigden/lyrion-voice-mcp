using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Services;

internal static class CatalogueEntityMapper
{
    public static DateTime? ToUtcDateTime(DateTimeOffset? value) =>
        value?.UtcDateTime;

    public static DateTime ToUtcDateTime(DateTimeOffset value) => value.UtcDateTime;

    public static DateTimeOffset? ToDateTimeOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    public static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static CatalogueState ToModel(EntityCatalogueState entity)
    {
        CatalogueSummary? summary = null;
        if (entity.Status == EntityCatalogueStateStatus.Succeeded)
        {
            summary = new CatalogueSummary(
                entity.SourceId ?? throw MissingSummary(nameof(entity.SourceId)),
                entity.SourceProvider ?? throw MissingSummary(nameof(entity.SourceProvider)),
                entity.SourceRevision,
                entity.SourceVersion,
                ToDateTimeOffset(entity.CapturedAtUtc)
                    ?? throw MissingSummary(nameof(entity.CapturedAtUtc)),
                ToDateTimeOffset(entity.SourceLastScanAtUtc),
                ToDateTimeOffset(entity.RefreshedAtUtc)
                    ?? throw MissingSummary(nameof(entity.RefreshedAtUtc)),
                entity.ArtistCount ?? throw MissingSummary(nameof(entity.ArtistCount)),
                entity.AlbumCount ?? throw MissingSummary(nameof(entity.AlbumCount)),
                entity.GenreCount ?? throw MissingSummary(nameof(entity.GenreCount)),
                entity.TrackCount ?? throw MissingSummary(nameof(entity.TrackCount)),
                entity.VirtualLibraryCount
                    ?? throw MissingSummary(nameof(entity.VirtualLibraryCount)),
                entity.WarningCount ?? throw MissingSummary(nameof(entity.WarningCount)));
        }

        return new CatalogueState(
            entity.RefreshId,
            entity.Status switch
            {
                EntityCatalogueStateStatus.Running => CatalogueStateStatus.Running,
                EntityCatalogueStateStatus.Succeeded => CatalogueStateStatus.Succeeded,
                EntityCatalogueStateStatus.Failed => CatalogueStateStatus.Failed,
                EntityCatalogueStateStatus.Cancelled => CatalogueStateStatus.Cancelled,
                EntityCatalogueStateStatus.Interrupted => CatalogueStateStatus.Interrupted,
                _ => throw new InvalidOperationException("Unknown catalogue state status.")
            },
            ToDateTimeOffset(entity.StartedAtUtc),
            ToDateTimeOffset(entity.CompletedAtUtc),
            summary);
    }

    private static InvalidOperationException MissingSummary(string field) =>
        new($"A successful catalogue state is missing {field}.");
}
