using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class JobLogConfiguration : IEntityTypeConfiguration<EntityJobLog>
{
    public void Configure(EntityTypeBuilder<EntityJobLog> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        ModelConventions.ConfigureCreatedUpdated(builder);

        builder.Property(item => item.Level).IsRequired();
        builder.Property(item => item.Message).IsRequired();
        builder.Property(item => item.LoggedAtUtc).IsRequired();
        builder.HasIndex(item => new { item.JobId, item.Id });

        builder.ToTable("JobLogs");
    }
}
