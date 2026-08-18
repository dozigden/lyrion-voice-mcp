using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class ErrorLogConfiguration : IEntityTypeConfiguration<EntityErrorLog>
{
    public void Configure(EntityTypeBuilder<EntityErrorLog> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        ModelConventions.ConfigureCreatedUpdated(builder);

        builder.Property(item => item.OccurredAtUtc).IsRequired();
        builder.Property(item => item.Source).HasMaxLength(32).IsRequired();
        builder.Property(item => item.Area).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ExceptionType).HasMaxLength(512).IsRequired();
        builder.Property(item => item.Message).HasMaxLength(2048).IsRequired();
        builder.Property(item => item.StackTrace).HasMaxLength(32768);
        builder.Property(item => item.TraceIdentifier).HasMaxLength(128);
        builder.Property(item => item.RequestMethod).HasMaxLength(16);
        builder.Property(item => item.RequestPath).HasMaxLength(2048);
        builder.Property(item => item.ContextJson).HasMaxLength(32768);

        builder.HasIndex(item => item.ReportId).IsUnique();
        builder.HasIndex(item => new { item.OccurredAtUtc, item.Id });
        builder.HasIndex(item => new { item.Source, item.Area });
        builder.HasIndex(item => item.TraceIdentifier);
        builder.HasIndex(item => item.JobId);

        builder
            .HasOne(item => item.Job)
            .WithMany()
            .HasForeignKey(item => item.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("ErrorLogs");
    }
}
