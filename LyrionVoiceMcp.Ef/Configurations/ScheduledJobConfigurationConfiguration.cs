using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class ScheduledJobConfigurationConfiguration
    : IEntityTypeConfiguration<EntityScheduledJobConfiguration>
{
    public void Configure(EntityTypeBuilder<EntityScheduledJobConfiguration> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        ModelConventions.ConfigureCreatedUpdated(builder);

        builder.Property(item => item.Name).HasMaxLength(120).IsRequired();
        builder.Property(item => item.Enabled).IsRequired();
        builder.Property(item => item.CronExpression).HasMaxLength(120).IsRequired();
        builder.HasIndex(item => item.Name).IsUnique();

        builder.ToTable("ScheduledJobConfigurations");
    }
}
