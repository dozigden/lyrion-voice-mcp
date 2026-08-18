using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class SearchObservationRequestConfiguration
    : IEntityTypeConfiguration<EntitySearchObservationRequest>
{
    public void Configure(EntityTypeBuilder<EntitySearchObservationRequest> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);

        builder.Property(item => item.Sequence).IsRequired();
        builder.Property(item => item.Source).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Command).HasMaxLength(4096).IsRequired();
        builder.Property(item => item.Status).IsRequired();
        builder.Property(item => item.FailureMessage).HasMaxLength(4096);
        builder.Property(item => item.DurationMilliseconds).IsRequired();
        builder.Property(item => item.ResultCount).IsRequired();

        builder.HasIndex(item => new { item.SearchObservationId, item.Sequence }).IsUnique();

        builder.ToTable("SearchObservationRequests");
    }
}
