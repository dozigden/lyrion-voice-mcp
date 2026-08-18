using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class SearchObservationReviewConfiguration
    : IEntityTypeConfiguration<EntitySearchObservationReview>
{
    public void Configure(EntityTypeBuilder<EntitySearchObservationReview> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);

        builder.Property(item => item.Classification).IsRequired();
        builder.Property(item => item.ExpectedCorrelationId).HasMaxLength(128);
        builder.Property(item => item.ExpectedKind);
        builder.Property(item => item.ExpectedTitle).HasMaxLength(1024);
        builder.Property(item => item.ExpectedArtist).HasMaxLength(1024);
        builder.Property(item => item.ExpectedAlbum).HasMaxLength(1024);
        builder.Property(item => item.Notes).HasMaxLength(8192);
        builder.Property(item => item.IncludeInEvaluation).IsRequired();
        builder.Property(item => item.ReviewedAtUtc).IsRequired();

        builder.HasIndex(item => item.SearchObservationId).IsUnique();
        builder.HasIndex(item => item.IncludeInEvaluation);

        builder.ToTable("SearchObservationReviews");
    }
}
