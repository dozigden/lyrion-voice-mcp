using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class SearchObservationSelectionConfiguration
    : IEntityTypeConfiguration<EntitySearchObservationSelection>
{
    public void Configure(EntityTypeBuilder<EntitySearchObservationSelection> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);

        builder.Property(item => item.SelectedAtUtc).IsRequired();
        builder.HasIndex(item => item.SearchObservationCandidateId).IsUnique();
        builder.HasIndex(item => item.SelectedAtUtc);

        builder.ToTable("SearchObservationSelections");
    }
}
