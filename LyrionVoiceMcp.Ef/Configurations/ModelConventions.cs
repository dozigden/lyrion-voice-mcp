using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

internal static class ModelConventions
{
    public static void ConfigureIntPrimaryKey<TEntity>(
        EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.HasKey("Id");
    }

    public static void ConfigureCreatedUpdated<TEntity>(
        EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ISupportCreatedUpdated
    {
        builder.Property(item => item.CreatedAtUtc).IsRequired();
        builder.Property(item => item.UpdatedAtUtc).IsRequired();
    }
}
