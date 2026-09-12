using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Ef.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Ef.Providers.BbcSounds;

public sealed class BbcSubscriptionStateConfiguration : IEntityTypeConfiguration<EntityBbcSubscriptionState>
{
    public void Configure(EntityTypeBuilder<EntityBbcSubscriptionState> builder)
    {
        builder.ToTable("BbcSubscriptionState");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SnapshotId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Available).IsRequired();
        builder.Property(x => x.ShowCount).IsRequired();
    }
}

public sealed class BbcSubscribedShowConfiguration : IEntityTypeConfiguration<EntityBbcSubscribedShow>
{
    public void Configure(EntityTypeBuilder<EntityBbcSubscribedShow> builder)
    {
        builder.ToTable("BbcSubscribedShows");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SnapshotId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProgrammeId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(1024).IsRequired();
        builder.HasIndex(x => new { x.SnapshotId, x.ProgrammeId }).IsUnique();
        builder.HasIndex(x => new { x.SnapshotId, x.Id });
    }
}

internal sealed class BbcSubscriptionRepository(IAmbientDbContextLocator locator)
    : RepositoryBase<EntityBbcSubscribedShow>(locator), IBbcSubscriptionRepository
{
    public Task<EntityBbcSubscriptionState?> GetStateAsync(CancellationToken cancellationToken) =>
        DbContext.Set<EntityBbcSubscriptionState>().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
    public void AddState(EntityBbcSubscriptionState state) => DbContext.Add(state);
    public void AddShows(IEnumerable<EntityBbcSubscribedShow> shows) => AddRange(shows);
    public async Task<IReadOnlyList<EntityBbcSubscribedShow>> ReadPageAsync(
        string snapshotId, int afterId, CancellationToken cancellationToken) =>
        await Query().Where(x => x.SnapshotId == snapshotId && x.Id > afterId)
            .OrderBy(x => x.Id).Take(500).ToArrayAsync(cancellationToken);
    public async Task<int> DeleteInactivePageAsync(string snapshotId, CancellationToken cancellationToken)
    {
        var rows = await Query().Where(x => x.SnapshotId != snapshotId).OrderBy(x => x.Id)
            .Take(500).ToArrayAsync(cancellationToken);
        RemoveRange(rows);
        return rows.Length;
    }
}

internal static class BbcSoundsPersistenceRegistration
{
    public static IServiceCollection AddBbcSoundsPersistence(this IServiceCollection services) =>
        services.AddTransient<IBbcSubscriptionRepository, BbcSubscriptionRepository>();
}
