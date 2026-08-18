using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LyrionVoiceMcp.Ef;

public sealed class LyrionVoiceMcpDbContext(
    DbContextOptions<LyrionVoiceMcpDbContext> options) : DbContext(options)
{
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
