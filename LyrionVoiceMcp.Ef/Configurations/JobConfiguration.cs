using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class JobConfiguration : IEntityTypeConfiguration<EntityJob>
{
    public void Configure(EntityTypeBuilder<EntityJob> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        ModelConventions.ConfigureCreatedUpdated(builder);

        builder.Property(item => item.Type).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Status).IsRequired();
        builder.Property(item => item.RunAfterUtc).IsRequired();
        builder.Property(item => item.PayloadJson).IsRequired();
        builder.Property(item => item.ResultJson).IsRequired();
        builder.Property(item => item.ErrorMessage).HasMaxLength(2048);
        builder.Property(item => item.CorrelationId).HasMaxLength(256);

        builder.HasIndex(item => new { item.Status, item.RunAfterUtc, item.Id });
        builder.HasIndex(item => new { item.CreatedAtUtc, item.Id });
        builder.HasIndex(item => new { item.Type, item.Status, item.Id });
        builder.HasIndex(item => item.CorrelationId)
            .IsUnique()
            .HasFilter("\"CorrelationId\" IS NOT NULL");
        builder.HasIndex(item => item.Type)
            .IsUnique()
            .HasDatabaseName("UX_Jobs_ActiveCatalogueRefresh")
            .HasFilter("\"Type\" = 'catalogue.refresh' AND \"Status\" IN (0, 1)");

        builder
            .HasMany(item => item.Logs)
            .WithOne(item => item.Job)
            .HasForeignKey(item => item.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("Jobs");
    }
}
