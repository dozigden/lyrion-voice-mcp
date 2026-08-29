using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class SearchObservationCandidateConfiguration
    : IEntityTypeConfiguration<EntitySearchObservationCandidate>
{
    public void Configure(EntityTypeBuilder<EntitySearchObservationCandidate> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);

        builder.Property(item => item.Position).IsRequired();
        builder.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Kind).IsRequired();
        builder.Property(item => item.MediaId).HasMaxLength(2048).IsRequired();
        builder.Property(item => item.Title).HasMaxLength(1024).IsRequired();
        builder.Property(item => item.Artist).HasMaxLength(1024);
        builder.Property(item => item.Album).HasMaxLength(1024);
        builder.Property(item => item.Rating);
        builder.Property(item => item.IsExactArtistMatch).IsRequired();
        builder.Property(item => item.MatchSignal).HasMaxLength(64);

        builder.HasIndex(item => item.CorrelationId).IsUnique();
        builder.HasIndex(item => new { item.SearchObservationId, item.Position }).IsUnique();

        builder
            .HasOne(item => item.Selection)
            .WithOne(item => item.SearchObservationCandidate)
            .HasForeignKey<EntitySearchObservationSelection>(
                item => item.SearchObservationCandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("SearchObservationCandidates");
    }
}
