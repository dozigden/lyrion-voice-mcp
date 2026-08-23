using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class SearchObservationConfiguration
    : IEntityTypeConfiguration<EntitySearchObservation>
{
    public void Configure(EntityTypeBuilder<EntitySearchObservation> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);

        builder.Property(item => item.ObservationId).HasMaxLength(64).IsRequired();
        builder.Property(item => item.CreatedAtUtc).IsRequired();
        builder.Property(item => item.OriginalQuery).HasMaxLength(500).IsRequired();
        builder.Property(item => item.NormalisedQuery).HasMaxLength(500).IsRequired();
        builder.Property(item => item.Rating);
        builder.Property(item => item.RatingMatch);
        builder.Property(item => item.Genre).HasMaxLength(500);
        builder.Property(item => item.RequestedFromYear);
        builder.Property(item => item.RequestedToYear);
        builder.Property(item => item.EffectiveFromYear);
        builder.Property(item => item.EffectiveToYear);
        builder.Property(item => item.RequestedKind);
        builder.Property(item => item.Provider).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Collection).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Resolver).HasMaxLength(128).IsRequired();
        builder.Property(item => item.ResolverVersion).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Status).IsRequired();
        builder.Property(item => item.FailureMessage).HasMaxLength(4096);
        builder.Property(item => item.TotalDurationMilliseconds).IsRequired();
        builder.Property(item => item.RetrievalDurationMilliseconds).IsRequired();
        builder.Property(item => item.ProcessingDurationMilliseconds).IsRequired();

        builder.HasIndex(item => item.ObservationId).IsUnique();
        builder.HasIndex(item => item.CreatedAtUtc);
        builder.HasIndex(item => item.Status);

        builder
            .HasMany(item => item.Requests)
            .WithOne(item => item.SearchObservation)
            .HasForeignKey(item => item.SearchObservationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasMany(item => item.Candidates)
            .WithOne(item => item.SearchObservation)
            .HasForeignKey(item => item.SearchObservationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne(item => item.Review)
            .WithOne(item => item.SearchObservation)
            .HasForeignKey<EntitySearchObservationReview>(item => item.SearchObservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("SearchObservations");
    }
}
