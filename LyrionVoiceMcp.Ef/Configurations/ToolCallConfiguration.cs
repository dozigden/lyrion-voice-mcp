using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class ToolCallConfiguration : IEntityTypeConfiguration<EntityToolCall>
{
    public void Configure(EntityTypeBuilder<EntityToolCall> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        ModelConventions.ConfigureCreatedUpdated(builder);

        builder.Property(item => item.ToolCallId).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ToolName).HasMaxLength(256).IsRequired();
        builder.Property(item => item.Status).IsRequired();
        builder.Property(item => item.StartedAtUtc).IsRequired();
        builder.Property(item => item.ArgumentsJson).IsRequired();
        builder.Property(item => item.TraceIdentifier).HasMaxLength(128);

        builder.HasIndex(item => item.ToolCallId).IsUnique();
        builder.HasIndex(item => new { item.StartedAtUtc, item.Id });
        builder.HasIndex(item => new { item.ToolName, item.Status, item.StartedAtUtc });
        builder.HasIndex(item => item.ErrorLogId);

        builder
            .HasOne(item => item.ErrorLog)
            .WithMany()
            .HasForeignKey(item => item.ErrorLogId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("ToolCalls");
    }
}
