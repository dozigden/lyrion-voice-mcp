using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class ScheduledJobStateConfiguration
    : IEntityTypeConfiguration<EntityScheduledJobState>
{
    public void Configure(EntityTypeBuilder<EntityScheduledJobState> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        ModelConventions.ConfigureCreatedUpdated(builder);

        builder.Property(item => item.Name).HasMaxLength(120).IsRequired();
        builder.Property(item => item.LastRunAtUtc).IsRequired();
        builder.HasIndex(item => item.Name).IsUnique();

        builder.ToTable("ScheduledJobStates");
    }
}
