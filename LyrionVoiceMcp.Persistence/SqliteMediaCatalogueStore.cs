using System.Globalization;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Persistence;

public sealed class SqliteMediaCatalogueStore(
    CatalogueSettings settings,
    TimeProvider? configuredTimeProvider = null) : IMediaCatalogueStore
{
    private const int SchemaVersion = 6;
    private readonly TimeProvider timeProvider = configuredTimeProvider ?? TimeProvider.System;
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = settings.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true
    }.ToString();

    public async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settings.DatabasePath)!);
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        await ExecuteAsync(
            connection,
            "CREATE TABLE IF NOT EXISTS catalogue_schema (version INTEGER NOT NULL);",
            cancellationToken);

        var storedVersion = await ReadSchemaVersionAsync(connection, cancellationToken);
        if (storedVersion != 0 && storedVersion != SchemaVersion)
        {
            await ExecuteAsync(connection, ResetSchemaSql, cancellationToken);
        }

        await ExecuteAsync(connection, SchemaSql, cancellationToken);
        storedVersion = await ReadSchemaVersionAsync(connection, cancellationToken);
        if (storedVersion != SchemaVersion)
        {
            throw new InvalidOperationException("The catalogue database schema could not be initialised.");
        }

        await using var interrupted = connection.CreateCommand();
        interrupted.CommandText = """
            UPDATE catalogue_state
            SET refresh_status = 'interrupted',
                refresh_completed_at = $completedAt
            WHERE refresh_status = 'running';
            """;
        Add(interrupted, "$completedAt", FormatDate(timeProvider.GetUtcNow()));
        await interrupted.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CatalogueState?> GetStateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT refresh_id, refresh_status, refresh_started_at, refresh_completed_at,
                   source_id, source_provider, source_revision, source_version,
                   captured_at, source_last_scan_at, refreshed_at,
                   artist_count, album_count, genre_count, track_count,
                   virtual_library_count, warning_count
            FROM catalogue_state
            WHERE id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var status = ParseCatalogueStateStatus(reader.GetString(1));
        var summary = status == CatalogueStateStatus.Succeeded
            ? ReadSummary(reader, 4)
            : null;
        return new CatalogueState(
            reader.GetString(0),
            status,
            ParseDate(reader.GetString(2)),
            reader.IsDBNull(3) ? null : ParseDate(reader.GetString(3)),
            summary);
    }

    public async Task<CatalogueSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
        (await GetStateAsync(cancellationToken))?.Summary;

    public async Task BeginRefreshAsync(
        string refreshId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO catalogue_state (
                id, refresh_id, refresh_status, refresh_started_at, refresh_completed_at,
                source_id, source_provider, source_version, source_revision,
                captured_at, source_last_scan_at, refreshed_at,
                artist_count, album_count, genre_count, track_count,
                virtual_library_count, warning_count)
            VALUES (
                1, $refreshId, 'running', $startedAt, NULL,
                NULL, NULL, NULL, NULL,
                NULL, NULL, NULL,
                NULL, NULL, NULL, NULL,
                NULL, NULL)
            ON CONFLICT(id) DO UPDATE SET
                refresh_id = excluded.refresh_id,
                refresh_status = excluded.refresh_status,
                refresh_started_at = excluded.refresh_started_at,
                refresh_completed_at = NULL,
                source_id = NULL,
                source_provider = NULL,
                source_version = NULL,
                source_revision = NULL,
                captured_at = NULL,
                source_last_scan_at = NULL,
                refreshed_at = NULL,
                artist_count = NULL,
                album_count = NULL,
                genre_count = NULL,
                track_count = NULL,
                virtual_library_count = NULL,
                warning_count = NULL;
            """;
        Add(command, "$refreshId", refreshId);
        Add(command, "$startedAt", FormatDate(startedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task WriteAlbumsAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportAlbum> albums,
        CancellationToken cancellationToken) =>
        WriteBatchAsync(
            albums,
            """
            INSERT INTO catalogue_albums (
                source_id, title, album_artist_source_id, year, disc_count,
                is_compilation, release_type, artwork_track_source_id, external_id, seen_refresh_id)
            VALUES (
                $sourceId, $title, $artistId, $year, $discCount,
                $compilation, $releaseType, $artworkTrackId, $externalId, $refreshId)
            ON CONFLICT(source_id) DO UPDATE SET
                title = excluded.title,
                album_artist_source_id = excluded.album_artist_source_id,
                year = excluded.year,
                disc_count = excluded.disc_count,
                is_compilation = excluded.is_compilation,
                release_type = excluded.release_type,
                artwork_track_source_id = excluded.artwork_track_source_id,
                external_id = excluded.external_id,
                seen_refresh_id = excluded.seen_refresh_id;
            """,
            command =>
            {
                Add(command, "$sourceId", null);
                Add(command, "$title", null);
                Add(command, "$artistId", null);
                Add(command, "$year", null);
                Add(command, "$discCount", null);
                Add(command, "$compilation", null);
                Add(command, "$releaseType", null);
                Add(command, "$artworkTrackId", null);
                Add(command, "$externalId", null);
                Add(command, "$refreshId", refreshId);
            },
            (command, album) =>
            {
                Set(command, "$sourceId", album.SourceId);
                Set(command, "$title", album.Title);
                Set(command, "$artistId", album.AlbumArtistSourceId);
                Set(command, "$year", album.Year);
                Set(command, "$discCount", album.DiscCount);
                Set(command, "$compilation", ToDatabaseBoolean(album.IsCompilation));
                Set(command, "$releaseType", album.ReleaseType);
                Set(command, "$artworkTrackId", album.ArtworkTrackSourceId);
                Set(command, "$externalId", album.ExternalId);
            },
            cancellationToken);

    public Task WriteGenresAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportGenre> genres,
        CancellationToken cancellationToken) =>
        WriteBatchAsync(
            genres,
            """
            INSERT INTO catalogue_genres (source_id, name, seen_refresh_id)
            VALUES ($sourceId, $name, $refreshId)
            ON CONFLICT(source_id) DO UPDATE SET
                name = excluded.name,
                seen_refresh_id = excluded.seen_refresh_id;
            """,
            command =>
            {
                Add(command, "$sourceId", null);
                Add(command, "$name", null);
                Add(command, "$refreshId", refreshId);
            },
            (command, genre) =>
            {
                Set(command, "$sourceId", genre.SourceId);
                Set(command, "$name", genre.Name);
            },
            cancellationToken);

    public async Task WriteTracksAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportTrack> tracks,
        CancellationToken cancellationToken)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var trackCommand = CreateTrackCommand(connection, transaction, refreshId);
        await using var deleteArtists = CreateDeleteCommand(
            connection,
            transaction,
            "DELETE FROM catalogue_track_artists WHERE track_source_id = $trackId;");
        await using var deleteGenres = CreateDeleteCommand(
            connection,
            transaction,
            "DELETE FROM catalogue_track_genres WHERE track_source_id = $trackId;");
        await using var deleteStatistics = CreateDeleteCommand(
            connection,
            transaction,
            "DELETE FROM catalogue_track_statistics WHERE track_source_id = $trackId;");
        await using var artistCommand = CreateTrackArtistCommand(connection, transaction);
        await using var genreCommand = CreateTrackGenreCommand(connection, transaction);
        await using var statisticsCommand = CreateTrackStatisticsCommand(connection, transaction);

        foreach (var track in tracks)
        {
            SetTrackParameters(trackCommand, track);
            await trackCommand.ExecuteNonQueryAsync(cancellationToken);
            await DeleteTrackChildrenAsync(deleteArtists, track.SourceId, cancellationToken);
            await DeleteTrackChildrenAsync(deleteGenres, track.SourceId, cancellationToken);
            await DeleteTrackChildrenAsync(deleteStatistics, track.SourceId, cancellationToken);

            foreach (var artistId in track.ArtistSourceIds)
            {
                Set(artistCommand, "$trackId", track.SourceId);
                Set(artistCommand, "$artistId", artistId);
                await artistCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var genreId in track.GenreSourceIds)
            {
                Set(genreCommand, "$trackId", track.SourceId);
                Set(genreCommand, "$genreId", genreId);
                await genreCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var statistics in track.Statistics)
            {
                Set(statisticsCommand, "$trackId", track.SourceId);
                Set(statisticsCommand, "$source", statistics.Source);
                Set(statisticsCommand, "$rating", statistics.Rating);
                Set(statisticsCommand, "$playCount", statistics.PlayCount);
                Set(statisticsCommand, "$lastPlayedAt", DbDate(statistics.LastPlayedAt));
                await statisticsCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task WriteArtistsAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportArtist> artists,
        CancellationToken cancellationToken)
    {
        if (artists.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = """
            INSERT INTO catalogue_artist_lookup (source_id, seen_refresh_id)
            VALUES ($sourceId, $refreshId)
            ON CONFLICT(source_id) DO UPDATE SET
                seen_refresh_id = excluded.seen_refresh_id;
            """;
        Add(lookup, "$sourceId", null);
        Add(lookup, "$refreshId", refreshId);

        await using var artist = connection.CreateCommand();
        artist.Transaction = transaction;
        artist.CommandText = """
            INSERT INTO catalogue_artists (source_id, name, external_id, seen_refresh_id)
            SELECT $sourceId, $name, $externalId, $refreshId
            WHERE EXISTS (
                SELECT 1
                FROM catalogue_albums
                WHERE seen_refresh_id = $refreshId
                  AND album_artist_source_id = $sourceId
            ) OR EXISTS (
                SELECT 1
                FROM catalogue_track_artists artist
                JOIN catalogue_tracks track ON track.source_id = artist.track_source_id
                WHERE track.seen_refresh_id = $refreshId
                  AND artist.artist_source_id = $sourceId
            )
            ON CONFLICT(source_id) DO UPDATE SET
                name = excluded.name,
                external_id = excluded.external_id,
                seen_refresh_id = excluded.seen_refresh_id;
            """;
        Add(artist, "$sourceId", null);
        Add(artist, "$name", null);
        Add(artist, "$externalId", null);
        Add(artist, "$refreshId", refreshId);

        foreach (var item in artists)
        {
            Set(lookup, "$sourceId", item.SourceId);
            await lookup.ExecuteNonQueryAsync(cancellationToken);

            Set(artist, "$sourceId", item.SourceId);
            Set(artist, "$name", item.Name);
            Set(artist, "$externalId", item.ExternalId);
            await artist.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task WriteVirtualLibrariesAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportVirtualLibrary> libraries,
        CancellationToken cancellationToken) =>
        WriteBatchAsync(
            libraries,
            """
            INSERT INTO catalogue_virtual_libraries (source_id, name, seen_refresh_id)
            VALUES ($sourceId, $name, $refreshId)
            ON CONFLICT(source_id) DO UPDATE SET
                name = excluded.name,
                seen_refresh_id = excluded.seen_refresh_id;
            """,
            command =>
            {
                Add(command, "$sourceId", null);
                Add(command, "$name", null);
                Add(command, "$refreshId", refreshId);
            },
            (command, library) =>
            {
                Set(command, "$sourceId", library.SourceId);
                Set(command, "$name", library.Name);
            },
            cancellationToken);

    public Task WriteVirtualLibraryTracksAsync(
        string refreshId,
        string librarySourceId,
        IReadOnlyList<string> trackSourceIds,
        CancellationToken cancellationToken) =>
        WriteBatchAsync(
            trackSourceIds,
            """
            INSERT INTO catalogue_virtual_library_tracks (
                library_source_id, track_source_id, seen_refresh_id)
            VALUES ($libraryId, $trackId, $refreshId)
            ON CONFLICT(library_source_id, track_source_id) DO UPDATE SET
                seen_refresh_id = excluded.seen_refresh_id;
            """,
            command =>
            {
                Add(command, "$libraryId", librarySourceId);
                Add(command, "$trackId", null);
                Add(command, "$refreshId", refreshId);
            },
            (command, trackId) => Set(command, "$trackId", trackId),
            cancellationToken);

    public async Task<CatalogueRefreshCompletion> CompleteRefreshAsync(
        string refreshId,
        CatalogueSourceReadResult source,
        DateTimeOffset completedAt,
        int existingWarningCount,
        CancellationToken cancellationToken)
    {
        await ValidateSeenCountAsync(
            "catalogue_artist_lookup",
            refreshId,
            source.ArtistLookupCount,
            cancellationToken);
        await ValidateSeenCountAsync(
            "catalogue_albums",
            refreshId,
            source.AlbumCount,
            cancellationToken);
        await ValidateSeenCountAsync(
            "catalogue_genres",
            refreshId,
            source.GenreCount,
            cancellationToken);
        await ValidateSeenCountAsync(
            "catalogue_tracks",
            refreshId,
            source.TrackCount,
            cancellationToken);
        await ValidateSeenCountAsync(
            "catalogue_virtual_libraries",
            refreshId,
            source.VirtualLibraryCount,
            cancellationToken);
        if (source.VirtualLibraryMemberships.Count != source.VirtualLibraryCount)
        {
            throw new InvalidOperationException(
                "The catalogue refresh did not report membership counts for every virtual library.");
        }

        foreach (var membership in source.VirtualLibraryMemberships)
        {
            await ValidateVirtualLibraryMembershipCountAsync(
                refreshId,
                membership,
                cancellationToken);
        }

        await DeleteNotSeenAsync("catalogue_virtual_library_tracks", refreshId, cancellationToken);
        await DeleteNotSeenAsync("catalogue_virtual_libraries", refreshId, cancellationToken);
        await DeleteNotSeenAsync("catalogue_tracks", refreshId, cancellationToken);
        await DeleteNotSeenAsync("catalogue_albums", refreshId, cancellationToken);
        await DeleteNotSeenAsync("catalogue_genres", refreshId, cancellationToken);
        await DeleteNotSeenAsync("catalogue_artists", refreshId, cancellationToken);
        await DeleteNotSeenAsync("catalogue_artist_lookup", refreshId, cancellationToken);

        var referentialWarnings = await ReadReferentialWarningsAsync(cancellationToken);
        var warningCount = existingWarningCount + referentialWarnings.Count;
        var summary = new CatalogueSummary(
            source.Source.Id,
            source.Source.Provider,
            source.Source.Revision,
            source.Source.Version,
            source.CapturedAt,
            source.SourceLastScanAt,
            completedAt,
            await CountAsync("catalogue_artists", cancellationToken),
            await CountAsync("catalogue_albums", cancellationToken),
            await CountAsync("catalogue_genres", cancellationToken),
            await CountAsync("catalogue_tracks", cancellationToken),
            await CountAsync("catalogue_virtual_libraries", cancellationToken),
            warningCount);

        await StoreCompletedRefreshAsync(
            refreshId,
            source,
            summary,
            completedAt,
            cancellationToken);
        return new CatalogueRefreshCompletion(summary, referentialWarnings);
    }

    private async Task StoreCompletedRefreshAsync(
        string refreshId,
        CatalogueSourceReadResult source,
        CatalogueSummary summary,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = """
                UPDATE catalogue_state
                SET refresh_status = 'succeeded',
                    refresh_completed_at = $completedAt,
                    source_id = $sourceId,
                    source_provider = $provider,
                    source_version = $version,
                    source_revision = $revision,
                    captured_at = $capturedAt,
                    source_last_scan_at = $lastScanAt,
                    refreshed_at = $refreshedAt,
                    artist_count = $artists,
                    album_count = $albums,
                    genre_count = $genres,
                    track_count = $tracks,
                    virtual_library_count = $libraries,
                    warning_count = $warnings
                WHERE id = 1
                  AND refresh_id = $refreshId
                  AND refresh_status = 'running';
                """;
            Add(state, "$refreshId", refreshId);
            Add(state, "$completedAt", FormatDate(completedAt));
            Add(state, "$sourceId", source.Source.Id);
            Add(state, "$provider", source.Source.Provider);
            Add(state, "$version", source.Source.Version);
            Add(state, "$revision", source.Source.Revision);
            Add(state, "$capturedAt", FormatDate(source.CapturedAt));
            Add(state, "$lastScanAt", DbDate(source.SourceLastScanAt));
            Add(state, "$refreshedAt", FormatDate(completedAt));
            Add(state, "$artists", summary.ArtistCount);
            Add(state, "$albums", summary.AlbumCount);
            Add(state, "$genres", summary.GenreCount);
            Add(state, "$tracks", summary.TrackCount);
            Add(state, "$libraries", summary.VirtualLibraryCount);
            Add(state, "$warnings", summary.WarningCount);
            if (await state.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The catalogue refresh state was not running when completion was recorded.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FinishRefreshAsync(
        string refreshId,
        CatalogueStateStatus status,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        if (status is CatalogueStateStatus.Running or CatalogueStateStatus.Succeeded)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A failed refresh must finish with a non-success terminal status.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE catalogue_state
            SET refresh_status = $status,
                refresh_completed_at = $completedAt
            WHERE id = 1
              AND refresh_id = $refreshId
              AND refresh_status = 'running';
            """;
        Add(command, "$status", ToText(status));
        Add(command, "$completedAt", FormatDate(completedAt));
        Add(command, "$refreshId", refreshId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CatalogueRefreshWarning>> ReadReferentialWarningsAsync(
        CancellationToken cancellationToken)
    {
        var warnings = new[]
        {
            new ReferentialWarning(
                "Track album references were not present in the imported album set.",
                """
                SELECT COUNT(*)
                FROM catalogue_tracks track
                LEFT JOIN catalogue_albums album ON album.source_id = track.album_source_id
                WHERE track.album_source_id IS NOT NULL AND album.source_id IS NULL;
                """),
            new ReferentialWarning(
                "Track or album artist references were not present in the imported artist set.",
                """
                SELECT
                    (SELECT COUNT(*)
                     FROM catalogue_albums album
                     LEFT JOIN catalogue_artists artist ON artist.source_id = album.album_artist_source_id
                     WHERE album.album_artist_source_id IS NOT NULL AND artist.source_id IS NULL)
                  + (SELECT COUNT(*)
                     FROM catalogue_track_artists track_artist
                     LEFT JOIN catalogue_artists artist ON artist.source_id = track_artist.artist_source_id
                     WHERE artist.source_id IS NULL);
                """),
            new ReferentialWarning(
                "Track genre references were not present in the imported genre set.",
                """
                SELECT COUNT(*)
                FROM catalogue_track_genres track_genre
                LEFT JOIN catalogue_genres genre ON genre.source_id = track_genre.genre_source_id
                WHERE genre.source_id IS NULL;
                """),
            new ReferentialWarning(
                "Virtual-library memberships referenced tracks outside the imported track set.",
                """
                SELECT COUNT(*)
                FROM catalogue_virtual_library_tracks member
                LEFT JOIN catalogue_tracks track ON track.source_id = member.track_source_id
                WHERE track.source_id IS NULL;
                """)
        };

        var results = new List<CatalogueRefreshWarning>();
        foreach (var warning in warnings)
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = warning.Sql;
            var value = await command.ExecuteScalarAsync(cancellationToken);
            var occurrences = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (occurrences == 0)
            {
                continue;
            }

            results.Add(new CatalogueRefreshWarning(
                CatalogueRefreshLogLevel.Warning,
                warning.Message,
                occurrences,
                null));
        }

        return results;
    }

    private async Task ValidateSeenCountAsync(
        string table,
        string refreshId,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE seen_refresh_id = $refreshId;";
        Add(command, "$refreshId", refreshId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var actualCount = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (actualCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"The catalogue refresh wrote {actualCount} unique rows to {table}, but LMS returned {expectedCount} rows.");
        }
    }

    private async Task ValidateVirtualLibraryMembershipCountAsync(
        string refreshId,
        CatalogueImportVirtualLibraryMembership membership,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM catalogue_virtual_library_tracks
            WHERE library_source_id = $libraryId AND seen_refresh_id = $refreshId;
            """;
        Add(command, "$libraryId", membership.LibrarySourceId);
        Add(command, "$refreshId", refreshId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var actualCount = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (actualCount != membership.TrackCount)
        {
            throw new InvalidOperationException(
                $"The catalogue refresh wrote {actualCount} unique virtual-library memberships for {membership.LibrarySourceId}, but LMS returned {membership.TrackCount} rows.");
        }
    }

    private async Task DeleteNotSeenAsync(
        string table,
        string refreshId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE seen_refresh_id <> $refreshId;";
        Add(command, "$refreshId", refreshId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> CountAsync(string table, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private async Task WriteBatchAsync<T>(
        IReadOnlyList<T> items,
        string sql,
        Action<SqliteCommand> configure,
        Action<SqliteCommand, T> setValues,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        configure(command);
        foreach (var item in items)
        {
            setValues(command, item);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static SqliteCommand CreateTrackCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string refreshId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_tracks (
                source_id, title, subtitle, url, content_type, is_remote,
                external_id, album_source_id, year, disc_number, disc_count, track_number,
                duration_seconds, file_size_bytes, sample_rate, added_at, source_modified_at,
                source_updated_at, release_type, is_compilation, artwork_track_source_id,
                work_source_id, work_title, performance, grouping_name, seen_refresh_id)
            VALUES (
                $sourceId, $title, $subtitle, $url, $contentType, $remote,
                $externalId, $albumId, $year, $discNumber, $discCount, $trackNumber,
                $duration, $fileSize, $sampleRate, $addedAt, $modifiedAt,
                $updatedAt, $releaseType, $compilation, $artworkTrackId,
                $workId, $workTitle, $performance, $grouping, $refreshId)
            ON CONFLICT(source_id) DO UPDATE SET
                title = excluded.title,
                subtitle = excluded.subtitle,
                url = excluded.url,
                content_type = excluded.content_type,
                is_remote = excluded.is_remote,
                external_id = excluded.external_id,
                album_source_id = excluded.album_source_id,
                year = excluded.year,
                disc_number = excluded.disc_number,
                disc_count = excluded.disc_count,
                track_number = excluded.track_number,
                duration_seconds = excluded.duration_seconds,
                file_size_bytes = excluded.file_size_bytes,
                sample_rate = excluded.sample_rate,
                added_at = excluded.added_at,
                source_modified_at = excluded.source_modified_at,
                source_updated_at = excluded.source_updated_at,
                release_type = excluded.release_type,
                is_compilation = excluded.is_compilation,
                artwork_track_source_id = excluded.artwork_track_source_id,
                work_source_id = excluded.work_source_id,
                work_title = excluded.work_title,
                performance = excluded.performance,
                grouping_name = excluded.grouping_name,
                seen_refresh_id = excluded.seen_refresh_id;
            """;
        foreach (var name in new[]
                 {
                     "$sourceId", "$title", "$subtitle", "$url", "$contentType", "$remote",
                     "$externalId", "$albumId", "$year", "$discNumber", "$discCount", "$trackNumber",
                     "$duration", "$fileSize", "$sampleRate", "$addedAt", "$modifiedAt", "$updatedAt",
                     "$releaseType", "$compilation", "$artworkTrackId", "$workId", "$workTitle",
                     "$performance", "$grouping"
                 })
        {
            Add(command, name, null);
        }

        Add(command, "$refreshId", refreshId);
        return command;
    }

    private static void SetTrackParameters(SqliteCommand command, CatalogueImportTrack track)
    {
        Set(command, "$sourceId", track.SourceId);
        Set(command, "$title", track.Title);
        Set(command, "$subtitle", track.Subtitle);
        Set(command, "$url", track.Url);
        Set(command, "$contentType", track.ContentType);
        Set(command, "$remote", track.IsRemote ? 1 : 0);
        Set(command, "$externalId", track.ExternalId);
        Set(command, "$albumId", track.AlbumSourceId);
        Set(command, "$year", track.Year);
        Set(command, "$discNumber", track.DiscNumber);
        Set(command, "$discCount", track.DiscCount);
        Set(command, "$trackNumber", track.TrackNumber);
        Set(command, "$duration", track.DurationSeconds);
        Set(command, "$fileSize", track.FileSizeBytes);
        Set(command, "$sampleRate", track.SampleRate);
        Set(command, "$addedAt", DbDate(track.AddedAt));
        Set(command, "$modifiedAt", DbDate(track.SourceModifiedAt));
        Set(command, "$updatedAt", DbDate(track.SourceUpdatedAt));
        Set(command, "$releaseType", track.ReleaseType);
        Set(command, "$compilation", ToDatabaseBoolean(track.IsCompilation));
        Set(command, "$artworkTrackId", track.ArtworkTrackSourceId);
        Set(command, "$workId", track.WorkSourceId);
        Set(command, "$workTitle", track.WorkTitle);
        Set(command, "$performance", track.Performance);
        Set(command, "$grouping", track.Grouping);
    }

    private static SqliteCommand CreateTrackArtistCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_track_artists (track_source_id, artist_source_id)
            VALUES ($trackId, $artistId);
            """;
        Add(command, "$trackId", null);
        Add(command, "$artistId", null);
        return command;
    }

    private static SqliteCommand CreateTrackGenreCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_track_genres (track_source_id, genre_source_id)
            VALUES ($trackId, $genreId);
            """;
        Add(command, "$trackId", null);
        Add(command, "$genreId", null);
        return command;
    }

    private static SqliteCommand CreateTrackStatisticsCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_track_statistics (
                track_source_id, source, rating, play_count, last_played_at)
            VALUES ($trackId, $source, $rating, $playCount, $lastPlayedAt);
            """;
        Add(command, "$trackId", null);
        Add(command, "$source", null);
        Add(command, "$rating", null);
        Add(command, "$playCount", null);
        Add(command, "$lastPlayedAt", null);
        return command;
    }

    private static SqliteCommand CreateDeleteCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        Add(command, "$trackId", null);
        return command;
    }

    private static async Task DeleteTrackChildrenAsync(
        SqliteCommand command,
        string trackId,
        CancellationToken cancellationToken)
    {
        Set(command, "$trackId", trackId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM catalogue_schema LIMIT 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null
            ? 0
            : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static CatalogueSummary ReadSummary(SqliteDataReader reader, int offset = 0) => new(
        reader.GetString(offset),
        reader.GetString(offset + 1),
        reader.IsDBNull(offset + 2) ? null : reader.GetString(offset + 2),
        reader.IsDBNull(offset + 3) ? null : reader.GetString(offset + 3),
        ParseDate(reader.GetString(offset + 4)),
        reader.IsDBNull(offset + 5) ? null : ParseDate(reader.GetString(offset + 5)),
        ParseDate(reader.GetString(offset + 6)),
        reader.GetInt32(offset + 7),
        reader.GetInt32(offset + 8),
        reader.GetInt32(offset + 9),
        reader.GetInt32(offset + 10),
        reader.GetInt32(offset + 11),
        reader.GetInt32(offset + 12));

    private static CatalogueStateStatus ParseCatalogueStateStatus(string value) => value switch
    {
        "running" => CatalogueStateStatus.Running,
        "succeeded" => CatalogueStateStatus.Succeeded,
        "failed" => CatalogueStateStatus.Failed,
        "cancelled" => CatalogueStateStatus.Cancelled,
        "interrupted" => CatalogueStateStatus.Interrupted,
        _ => throw new InvalidOperationException($"Unsupported catalogue state '{value}'.")
    };

    private static string ToText(CatalogueStateStatus status) => status switch
    {
        CatalogueStateStatus.Running => "running",
        CatalogueStateStatus.Succeeded => "succeeded",
        CatalogueStateStatus.Failed => "failed",
        CatalogueStateStatus.Cancelled => "cancelled",
        CatalogueStateStatus.Interrupted => "interrupted",
        _ => throw new InvalidOperationException("Unknown catalogue state status.")
    };

    private static SqliteParameter Add(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = DbValue(value);
        command.Parameters.Add(parameter);
        return parameter;
    }

    private static void Set(SqliteCommand command, string name, object? value) =>
        command.Parameters[name].Value = DbValue(value);

    private static int? ToDatabaseBoolean(bool? value) => value is null ? null : value.Value ? 1 : 0;
    private static object DbValue(object? value) => value ?? DBNull.Value;
    private static object DbDate(DateTimeOffset? value) => value is null ? DBNull.Value : FormatDate(value.Value);
    private static string FormatDate(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private sealed record ReferentialWarning(string Message, string Sql);

    private const string ResetSchemaSql = """
        DROP TABLE IF EXISTS catalogue_warnings;
        DROP TABLE IF EXISTS catalogue_virtual_library_tracks;
        DROP TABLE IF EXISTS catalogue_virtual_libraries;
        DROP TABLE IF EXISTS catalogue_track_statistics;
        DROP TABLE IF EXISTS catalogue_track_genres;
        DROP TABLE IF EXISTS catalogue_track_artists;
        DROP TABLE IF EXISTS catalogue_tracks;
        DROP TABLE IF EXISTS catalogue_genres;
        DROP TABLE IF EXISTS catalogue_albums;
        DROP TABLE IF EXISTS catalogue_artists;
        DROP TABLE IF EXISTS catalogue_artist_lookup;
        DROP TABLE IF EXISTS catalogue_refresh_logs;
        DROP TABLE IF EXISTS catalogue_refresh_runs;
        DROP TABLE IF EXISTS catalogue_state;
        DROP TABLE IF EXISTS catalogue_generations;
        DROP TABLE IF EXISTS catalogue_schema;
        """;

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS catalogue_schema (version INTEGER NOT NULL);
        INSERT INTO catalogue_schema (version)
            SELECT 6 WHERE NOT EXISTS (SELECT 1 FROM catalogue_schema);

        CREATE TABLE IF NOT EXISTS catalogue_state (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            refresh_id TEXT NOT NULL,
            refresh_status TEXT NOT NULL
                CHECK (refresh_status IN ('running', 'succeeded', 'failed', 'cancelled', 'interrupted')),
            refresh_started_at TEXT NOT NULL,
            refresh_completed_at TEXT NULL,
            source_id TEXT NULL,
            source_provider TEXT NULL,
            source_version TEXT NULL,
            source_revision TEXT NULL,
            captured_at TEXT NULL,
            source_last_scan_at TEXT NULL,
            refreshed_at TEXT NULL,
            artist_count INTEGER NULL,
            album_count INTEGER NULL,
            genre_count INTEGER NULL,
            track_count INTEGER NULL,
            virtual_library_count INTEGER NULL,
            warning_count INTEGER NULL
        );
        CREATE TABLE IF NOT EXISTS catalogue_artists (
            source_id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            external_id TEXT NULL,
            seen_refresh_id TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_catalogue_artists_seen_refresh_id
            ON catalogue_artists(seen_refresh_id);
        CREATE TABLE IF NOT EXISTS catalogue_artist_lookup (
            source_id TEXT PRIMARY KEY,
            seen_refresh_id TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_catalogue_artist_lookup_seen_refresh_id
            ON catalogue_artist_lookup(seen_refresh_id);
        CREATE TABLE IF NOT EXISTS catalogue_albums (
            source_id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            album_artist_source_id TEXT NULL,
            year INTEGER NULL,
            disc_count INTEGER NULL,
            is_compilation INTEGER NULL,
            release_type TEXT NULL,
            artwork_track_source_id TEXT NULL,
            external_id TEXT NULL,
            seen_refresh_id TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_catalogue_albums_seen_refresh_id
            ON catalogue_albums(seen_refresh_id);
        CREATE INDEX IF NOT EXISTS ix_catalogue_albums_album_artist_source_id
            ON catalogue_albums(album_artist_source_id);
        CREATE TABLE IF NOT EXISTS catalogue_genres (
            source_id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            seen_refresh_id TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_catalogue_genres_seen_refresh_id
            ON catalogue_genres(seen_refresh_id);
        CREATE TABLE IF NOT EXISTS catalogue_tracks (
            source_id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            subtitle TEXT NULL,
            url TEXT NOT NULL,
            content_type TEXT NULL,
            is_remote INTEGER NOT NULL,
            external_id TEXT NULL,
            album_source_id TEXT NULL,
            year INTEGER NULL,
            disc_number INTEGER NULL,
            disc_count INTEGER NULL,
            track_number INTEGER NULL,
            duration_seconds REAL NULL,
            file_size_bytes INTEGER NULL,
            sample_rate INTEGER NULL,
            added_at TEXT NULL,
            source_modified_at TEXT NULL,
            source_updated_at TEXT NULL,
            release_type TEXT NULL,
            is_compilation INTEGER NULL,
            artwork_track_source_id TEXT NULL,
            work_source_id TEXT NULL,
            work_title TEXT NULL,
            performance TEXT NULL,
            grouping_name TEXT NULL,
            seen_refresh_id TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_catalogue_tracks_seen_refresh_id
            ON catalogue_tracks(seen_refresh_id);
        CREATE TABLE IF NOT EXISTS catalogue_track_artists (
            track_source_id TEXT NOT NULL,
            artist_source_id TEXT NOT NULL,
            PRIMARY KEY (track_source_id, artist_source_id),
            FOREIGN KEY (track_source_id) REFERENCES catalogue_tracks(source_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_catalogue_track_artists_artist_source_id
            ON catalogue_track_artists(artist_source_id);
        CREATE TABLE IF NOT EXISTS catalogue_track_genres (
            track_source_id TEXT NOT NULL,
            genre_source_id TEXT NOT NULL,
            PRIMARY KEY (track_source_id, genre_source_id),
            FOREIGN KEY (track_source_id) REFERENCES catalogue_tracks(source_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_track_statistics (
            track_source_id TEXT NOT NULL,
            source TEXT NOT NULL,
            rating INTEGER NULL,
            play_count INTEGER NULL,
            last_played_at TEXT NULL,
            PRIMARY KEY (track_source_id, source),
            FOREIGN KEY (track_source_id) REFERENCES catalogue_tracks(source_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_virtual_libraries (
            source_id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            seen_refresh_id TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_catalogue_virtual_libraries_seen_refresh_id
            ON catalogue_virtual_libraries(seen_refresh_id);
        CREATE TABLE IF NOT EXISTS catalogue_virtual_library_tracks (
            library_source_id TEXT NOT NULL,
            track_source_id TEXT NOT NULL,
            seen_refresh_id TEXT NOT NULL,
            PRIMARY KEY (library_source_id, track_source_id),
            FOREIGN KEY (library_source_id) REFERENCES catalogue_virtual_libraries(source_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_catalogue_virtual_library_tracks_seen_refresh_id
            ON catalogue_virtual_library_tracks(seen_refresh_id);
        """;
}
