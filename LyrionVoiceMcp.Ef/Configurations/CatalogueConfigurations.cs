using LyrionVoiceMcp.Ef.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LyrionVoiceMcp.Ef.Configurations;

public sealed class CatalogueStateConfiguration
    : IEntityTypeConfiguration<EntityCatalogueState>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueState> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        builder.Property(item => item.RefreshId)
            .HasMaxLength(128)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(item => item.Status).IsRequired().IsConcurrencyToken();
        builder.Property(item => item.StartedAtUtc).IsRequired();
        builder.Property(item => item.SourceId).HasMaxLength(512);
        builder.Property(item => item.SourceProvider).HasMaxLength(128);
        builder.Property(item => item.SourceRevision).HasMaxLength(512);
        builder.Property(item => item.SourceVersion).HasMaxLength(128);
        builder.ToTable(
            "CatalogueStates",
            table => table.HasCheckConstraint("CK_CatalogueStates_Singleton", "\"Id\" = 1"));
    }
}

public sealed class CatalogueArtistConfiguration
    : IEntityTypeConfiguration<EntityCatalogueArtist>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueArtist> builder)
    {
        ConfigureIdentity(builder);
        builder.Property(item => item.Name).HasMaxLength(1024).IsRequired();
        builder.Property(item => item.ExternalId).HasMaxLength(512);
        builder.ToTable("CatalogueArtists");
    }

    internal static void ConfigureIdentity<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        builder.Property("SourceId").HasMaxLength(512).IsRequired();
        builder.Property("SeenRefreshId").HasMaxLength(128).IsRequired();
        builder.HasIndex("SourceId").IsUnique();
        builder.HasIndex("SeenRefreshId");
    }
}

public sealed class CatalogueArtistLookupConfiguration
    : IEntityTypeConfiguration<EntityCatalogueArtistLookup>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueArtistLookup> builder)
    {
        CatalogueArtistConfiguration.ConfigureIdentity(builder);
        builder.ToTable("CatalogueArtistLookups");
    }
}

public sealed class CatalogueAlbumConfiguration
    : IEntityTypeConfiguration<EntityCatalogueAlbum>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueAlbum> builder)
    {
        CatalogueArtistConfiguration.ConfigureIdentity(builder);
        builder.Property(item => item.Title).HasMaxLength(1024).IsRequired();
        builder.Property(item => item.AlbumArtistSourceId).HasMaxLength(512);
        builder.Property(item => item.ReleaseType).HasMaxLength(128);
        builder.Property(item => item.ArtworkTrackSourceId).HasMaxLength(512);
        builder.Property(item => item.ExternalId).HasMaxLength(512);
        builder.HasIndex(item => item.AlbumArtistSourceId);
        builder.ToTable("CatalogueAlbums");
    }
}

public sealed class CatalogueGenreConfiguration
    : IEntityTypeConfiguration<EntityCatalogueGenre>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueGenre> builder)
    {
        CatalogueArtistConfiguration.ConfigureIdentity(builder);
        builder.Property(item => item.Name).HasMaxLength(1024).IsRequired();
        builder.ToTable("CatalogueGenres");
    }
}

public sealed class CatalogueTrackConfiguration
    : IEntityTypeConfiguration<EntityCatalogueTrack>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueTrack> builder)
    {
        CatalogueArtistConfiguration.ConfigureIdentity(builder);
        builder.Property(item => item.Title).HasMaxLength(1024).IsRequired();
        builder.Property(item => item.Subtitle).HasMaxLength(1024);
        builder.Property(item => item.Url).HasMaxLength(4096).IsRequired();
        builder.Property(item => item.ContentType).HasMaxLength(128);
        builder.Property(item => item.ExternalId).HasMaxLength(512);
        builder.Property(item => item.AlbumSourceId).HasMaxLength(512);
        builder.Property(item => item.ReleaseType).HasMaxLength(128);
        builder.Property(item => item.ArtworkTrackSourceId).HasMaxLength(512);
        builder.Property(item => item.WorkSourceId).HasMaxLength(512);
        builder.Property(item => item.WorkTitle).HasMaxLength(1024);
        builder.Property(item => item.Performance).HasMaxLength(1024);
        builder.Property(item => item.Grouping).HasMaxLength(1024);
        builder.HasIndex(item => item.AlbumSourceId);
        builder
            .HasMany(item => item.Artists)
            .WithOne(item => item.Track)
            .HasForeignKey(item => item.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasMany(item => item.Genres)
            .WithOne(item => item.Track)
            .HasForeignKey(item => item.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasMany(item => item.Statistics)
            .WithOne(item => item.Track)
            .HasForeignKey(item => item.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable("CatalogueTracks");
    }
}

public sealed class CatalogueTrackArtistConfiguration
    : IEntityTypeConfiguration<EntityCatalogueTrackArtist>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueTrackArtist> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        builder.Property(item => item.ArtistSourceId).HasMaxLength(512).IsRequired();
        builder.HasIndex(item => new { item.TrackId, item.ArtistSourceId }).IsUnique();
        builder.HasIndex(item => item.ArtistSourceId);
        builder.ToTable("CatalogueTrackArtists");
    }
}

public sealed class CatalogueTrackGenreConfiguration
    : IEntityTypeConfiguration<EntityCatalogueTrackGenre>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueTrackGenre> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        builder.Property(item => item.GenreSourceId).HasMaxLength(512).IsRequired();
        builder.HasIndex(item => new { item.TrackId, item.GenreSourceId }).IsUnique();
        builder.HasIndex(item => item.GenreSourceId);
        builder.ToTable("CatalogueTrackGenres");
    }
}

public sealed class CatalogueTrackStatisticConfiguration
    : IEntityTypeConfiguration<EntityCatalogueTrackStatistic>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueTrackStatistic> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        builder.Property(item => item.Source).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Rating).IsRequired();
        builder.ToTable(item => item.HasCheckConstraint(
            "CK_CatalogueTrackStatistics_Rating",
            "Rating BETWEEN 0 AND 100"));
        builder.HasIndex(item => new { item.TrackId, item.Source }).IsUnique();
        builder.ToTable("CatalogueTrackStatistics");
    }
}

public sealed class CatalogueVirtualLibraryConfiguration
    : IEntityTypeConfiguration<EntityCatalogueVirtualLibrary>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueVirtualLibrary> builder)
    {
        CatalogueArtistConfiguration.ConfigureIdentity(builder);
        builder.Property(item => item.Name).HasMaxLength(1024).IsRequired();
        builder
            .HasMany(item => item.Tracks)
            .WithOne(item => item.VirtualLibrary)
            .HasForeignKey(item => item.VirtualLibraryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable("CatalogueVirtualLibraries");
    }
}

public sealed class CatalogueVirtualLibraryTrackConfiguration
    : IEntityTypeConfiguration<EntityCatalogueVirtualLibraryTrack>
{
    public void Configure(EntityTypeBuilder<EntityCatalogueVirtualLibraryTrack> builder)
    {
        ModelConventions.ConfigureIntPrimaryKey(builder);
        builder.Property(item => item.TrackSourceId).HasMaxLength(512).IsRequired();
        builder.Property(item => item.SeenRefreshId).HasMaxLength(128).IsRequired();
        builder.HasIndex(item => new { item.VirtualLibraryId, item.TrackSourceId }).IsUnique();
        builder.HasIndex(item => item.TrackSourceId);
        builder.HasIndex(item => item.SeenRefreshId);
        builder.ToTable("CatalogueVirtualLibraryTracks");
    }
}
