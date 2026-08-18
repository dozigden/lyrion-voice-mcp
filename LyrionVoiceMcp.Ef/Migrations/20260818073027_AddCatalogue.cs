using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogueAlbums",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AlbumArtistSourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    DiscCount = table.Column<int>(type: "INTEGER", nullable: true),
                    IsCompilation = table.Column<bool>(type: "INTEGER", nullable: true),
                    ReleaseType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ArtworkTrackSourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SeenRefreshId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueAlbums", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueArtistLookups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SeenRefreshId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueArtistLookups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueArtists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SeenRefreshId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueArtists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueGenres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SeenRefreshId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueGenres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RefreshId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SourceProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourceRevision = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SourceVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourceLastScanAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RefreshedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArtistCount = table.Column<int>(type: "INTEGER", nullable: true),
                    AlbumCount = table.Column<int>(type: "INTEGER", nullable: true),
                    GenreCount = table.Column<int>(type: "INTEGER", nullable: true),
                    TrackCount = table.Column<int>(type: "INTEGER", nullable: true),
                    VirtualLibraryCount = table.Column<int>(type: "INTEGER", nullable: true),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueStates", x => x.Id);
                    table.CheckConstraint("CK_CatalogueStates_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "CatalogueTracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IsRemote = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AlbumSourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    DiscNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    DiscCount = table.Column<int>(type: "INTEGER", nullable: true),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    SampleRate = table.Column<int>(type: "INTEGER", nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourceModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourceUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReleaseType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IsCompilation = table.Column<bool>(type: "INTEGER", nullable: true),
                    ArtworkTrackSourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    WorkSourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    WorkTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Performance = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Grouping = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    SeenRefreshId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueTracks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueVirtualLibraries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SeenRefreshId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueVirtualLibraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueTrackArtists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    ArtistSourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueTrackArtists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogueTrackArtists_CatalogueTracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "CatalogueTracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueTrackGenres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    GenreSourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueTrackGenres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogueTrackGenres_CatalogueTracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "CatalogueTracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueTrackStatistics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true),
                    PlayCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LastPlayedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueTrackStatistics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogueTrackStatistics_CatalogueTracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "CatalogueTracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogueVirtualLibraryTracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VirtualLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackSourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SeenRefreshId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueVirtualLibraryTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogueVirtualLibraryTracks_CatalogueVirtualLibraries_VirtualLibraryId",
                        column: x => x.VirtualLibraryId,
                        principalTable: "CatalogueVirtualLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueAlbums_AlbumArtistSourceId",
                table: "CatalogueAlbums",
                column: "AlbumArtistSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueAlbums_SeenRefreshId",
                table: "CatalogueAlbums",
                column: "SeenRefreshId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueAlbums_SourceId",
                table: "CatalogueAlbums",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueArtistLookups_SeenRefreshId",
                table: "CatalogueArtistLookups",
                column: "SeenRefreshId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueArtistLookups_SourceId",
                table: "CatalogueArtistLookups",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueArtists_SeenRefreshId",
                table: "CatalogueArtists",
                column: "SeenRefreshId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueArtists_SourceId",
                table: "CatalogueArtists",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueGenres_SeenRefreshId",
                table: "CatalogueGenres",
                column: "SeenRefreshId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueGenres_SourceId",
                table: "CatalogueGenres",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueTrackArtists_ArtistSourceId",
                table: "CatalogueTrackArtists",
                column: "ArtistSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueTrackArtists_TrackId_ArtistSourceId",
                table: "CatalogueTrackArtists",
                columns: new[] { "TrackId", "ArtistSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueTrackGenres_GenreSourceId",
                table: "CatalogueTrackGenres",
                column: "GenreSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueTrackGenres_TrackId_GenreSourceId",
                table: "CatalogueTrackGenres",
                columns: new[] { "TrackId", "GenreSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueTracks_AlbumSourceId",
                table: "CatalogueTracks",
                column: "AlbumSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueTracks_SeenRefreshId",
                table: "CatalogueTracks",
                column: "SeenRefreshId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueTracks_SourceId",
                table: "CatalogueTracks",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueTrackStatistics_TrackId_Source",
                table: "CatalogueTrackStatistics",
                columns: new[] { "TrackId", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueVirtualLibraries_SeenRefreshId",
                table: "CatalogueVirtualLibraries",
                column: "SeenRefreshId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueVirtualLibraries_SourceId",
                table: "CatalogueVirtualLibraries",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueVirtualLibraryTracks_SeenRefreshId",
                table: "CatalogueVirtualLibraryTracks",
                column: "SeenRefreshId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueVirtualLibraryTracks_TrackSourceId",
                table: "CatalogueVirtualLibraryTracks",
                column: "TrackSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueVirtualLibraryTracks_VirtualLibraryId_TrackSourceId",
                table: "CatalogueVirtualLibraryTracks",
                columns: new[] { "VirtualLibraryId", "TrackSourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogueAlbums");

            migrationBuilder.DropTable(
                name: "CatalogueArtistLookups");

            migrationBuilder.DropTable(
                name: "CatalogueArtists");

            migrationBuilder.DropTable(
                name: "CatalogueGenres");

            migrationBuilder.DropTable(
                name: "CatalogueStates");

            migrationBuilder.DropTable(
                name: "CatalogueTrackArtists");

            migrationBuilder.DropTable(
                name: "CatalogueTrackGenres");

            migrationBuilder.DropTable(
                name: "CatalogueTrackStatistics");

            migrationBuilder.DropTable(
                name: "CatalogueVirtualLibraryTracks");

            migrationBuilder.DropTable(
                name: "CatalogueTracks");

            migrationBuilder.DropTable(
                name: "CatalogueVirtualLibraries");
        }
    }
}
