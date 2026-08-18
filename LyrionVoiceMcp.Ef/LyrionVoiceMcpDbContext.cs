using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LyrionVoiceMcp.Ef;

public sealed class LyrionVoiceMcpDbContext(
    DbContextOptions<LyrionVoiceMcpDbContext> options) : DbContext(options)
{
    public DbSet<EntityJob> Jobs => Set<EntityJob>();

    public DbSet<EntityJobLog> JobLogs => Set<EntityJobLog>();

    public DbSet<EntityScheduledJobState> ScheduledJobStates => Set<EntityScheduledJobState>();

    public DbSet<EntityErrorLog> ErrorLogs => Set<EntityErrorLog>();

    public DbSet<EntityToolCall> ToolCalls => Set<EntityToolCall>();

    public DbSet<EntityCatalogueState> CatalogueStates => Set<EntityCatalogueState>();

    public DbSet<EntityCatalogueArtist> CatalogueArtists => Set<EntityCatalogueArtist>();

    public DbSet<EntityCatalogueArtistLookup> CatalogueArtistLookups =>
        Set<EntityCatalogueArtistLookup>();

    public DbSet<EntityCatalogueAlbum> CatalogueAlbums => Set<EntityCatalogueAlbum>();

    public DbSet<EntityCatalogueGenre> CatalogueGenres => Set<EntityCatalogueGenre>();

    public DbSet<EntityCatalogueTrack> CatalogueTracks => Set<EntityCatalogueTrack>();

    public DbSet<EntityCatalogueTrackArtist> CatalogueTrackArtists =>
        Set<EntityCatalogueTrackArtist>();

    public DbSet<EntityCatalogueTrackGenre> CatalogueTrackGenres =>
        Set<EntityCatalogueTrackGenre>();

    public DbSet<EntityCatalogueTrackStatistic> CatalogueTrackStatistics =>
        Set<EntityCatalogueTrackStatistic>();

    public DbSet<EntityCatalogueVirtualLibrary> CatalogueVirtualLibraries =>
        Set<EntityCatalogueVirtualLibrary>();

    public DbSet<EntityCatalogueVirtualLibraryTrack> CatalogueVirtualLibraryTracks =>
        Set<EntityCatalogueVirtualLibraryTrack>();

    public DbSet<EntitySearchObservation> SearchObservations =>
        Set<EntitySearchObservation>();

    public DbSet<EntitySearchObservationRequest> SearchObservationRequests =>
        Set<EntitySearchObservationRequest>();

    public DbSet<EntitySearchObservationCandidate> SearchObservationCandidates =>
        Set<EntitySearchObservationCandidate>();

    public DbSet<EntitySearchObservationSelection> SearchObservationSelections =>
        Set<EntitySearchObservationSelection>();

    public DbSet<EntitySearchObservationReview> SearchObservationReviews =>
        Set<EntitySearchObservationReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LyrionVoiceMcpDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampAuditFields()
    {
        var nowUtc = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ISupportCreatedUpdated>())
        {
            StampAuditFields(entry, nowUtc);
        }
    }

    private static void StampAuditFields(
        EntityEntry<ISupportCreatedUpdated> entry,
        DateTime nowUtc)
    {
        if (entry.State == EntityState.Added)
        {
            entry.Entity.CreatedAtUtc = nowUtc;
            entry.Entity.UpdatedAtUtc = nowUtc;
            return;
        }

        if (entry.State != EntityState.Modified)
        {
            return;
        }

        entry.Entity.UpdatedAtUtc = nowUtc;
        entry.Property(item => item.CreatedAtUtc).IsModified = false;
    }
}
