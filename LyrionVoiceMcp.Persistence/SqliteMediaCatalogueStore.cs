using System.Globalization;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Persistence;

public sealed class SqliteMediaCatalogueStore(
    CatalogueSettings settings,
    TimeProvider timeProvider) : IMediaCatalogueStore
{
    private const int SchemaVersion = 2;
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
        if (storedVersion == 0)
        {
            await ExecuteAsync(connection, SchemaSql, cancellationToken);
        }
        else if (storedVersion == 1)
        {
            await ExecuteAsync(connection, Migration1To2Sql, cancellationToken);
        }
        else if (storedVersion == SchemaVersion)
        {
            await ExecuteAsync(connection, SchemaSql, cancellationToken);
        }

        storedVersion = await ReadSchemaVersionAsync(connection, cancellationToken);
        if (storedVersion != SchemaVersion)
        {
            throw new InvalidOperationException("The catalogue database schema is not supported.");
        }

        await using var interrupted = connection.CreateCommand();
        interrupted.CommandText = """
            UPDATE catalogue_refresh_runs
            SET status = 'interrupted',
                completed_at = $completedAt,
                duration_ms = MAX(0, CAST((julianday($completedAt) - julianday(started_at)) * 86400000 AS INTEGER)),
                failure_message = 'Catalogue refresh was interrupted before completion.'
            WHERE status = 'running';
            """;
        Add(interrupted, "$completedAt", FormatDate(timeProvider.GetUtcNow()));
        await interrupted.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PublishedCatalogueGeneration?> GetPublishedGenerationAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.id, g.source_id, g.source_revision, g.source_version,
                   g.captured_at, g.source_last_scan_at, g.published_at,
                   g.artist_count, g.album_count, g.genre_count, g.track_count,
                   g.virtual_library_count, g.warning_count
            FROM catalogue_state s
            JOIN catalogue_generations g ON g.id = s.published_generation_id
            WHERE s.id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadGeneration(reader)
            : null;
    }

    public async Task<CatalogueRefreshRun?> GetLatestRefreshRunAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, status, started_at, completed_at, duration_ms,
                   published_generation_id, failure_message
            FROM catalogue_refresh_runs
            ORDER BY started_at DESC, id DESC
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRefreshRun(reader)
            : null;
    }

    public async Task BeginRefreshAsync(
        string refreshId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO catalogue_refresh_runs (id, status, started_at)
            VALUES ($id, 'running', $startedAt);
            """;
        Add(command, "$id", refreshId);
        Add(command, "$startedAt", FormatDate(startedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PublishedCatalogueGeneration> PublishAsync(
        CatalogueImportSnapshot snapshot,
        string refreshId,
        DateTimeOffset completedAt,
        long durationMilliseconds,
        CancellationToken cancellationToken)
    {
        var generation = new PublishedCatalogueGeneration(
            Guid.NewGuid().ToString("N"),
            snapshot.Source.Id,
            snapshot.Source.Revision,
            snapshot.Source.Version,
            snapshot.CapturedAt,
            snapshot.SourceLastScanAt,
            completedAt,
            snapshot.Artists.Count,
            snapshot.Albums.Count,
            snapshot.Genres.Count,
            snapshot.Tracks.Count,
            snapshot.VirtualLibraries.Count,
            snapshot.Warnings.Count);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertGenerationAsync(connection, transaction, generation, snapshot, cancellationToken);
        await InsertArtistsAsync(connection, transaction, generation.Id, snapshot.Artists, cancellationToken);
        await InsertAlbumsAsync(connection, transaction, generation.Id, snapshot.Albums, cancellationToken);
        await InsertGenresAsync(connection, transaction, generation.Id, snapshot.Genres, cancellationToken);
        await InsertTracksAsync(connection, transaction, generation.Id, snapshot.Tracks, cancellationToken);
        await InsertVirtualLibrariesAsync(
            connection,
            transaction,
            generation.Id,
            snapshot.VirtualLibraries,
            cancellationToken);
        await InsertWarningsAsync(connection, transaction, generation.Id, snapshot.Warnings, cancellationToken);
        await PublishGenerationAsync(
            connection,
            transaction,
            generation,
            refreshId,
            completedAt,
            durationMilliseconds,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return generation;
    }

    public async Task CompleteFailedRefreshAsync(
        string refreshId,
        CatalogueRefreshRunStatus status,
        DateTimeOffset completedAt,
        long durationMilliseconds,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        if (status is not (CatalogueRefreshRunStatus.Failed or CatalogueRefreshRunStatus.Cancelled))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A failed refresh can only be recorded as failed or cancelled.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE catalogue_refresh_runs
            SET status = $status,
                completed_at = $completedAt,
                duration_ms = $duration,
                failure_message = $failureMessage
            WHERE id = $id AND status = 'running';
            """;
        Add(command, "$status", ToText(status));
        Add(command, "$completedAt", FormatDate(completedAt));
        Add(command, "$duration", durationMilliseconds);
        Add(command, "$failureMessage", failureMessage);
        Add(command, "$id", refreshId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The catalogue refresh run was not active.");
        }
    }

    private static async Task InsertGenerationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PublishedCatalogueGeneration generation,
        CatalogueImportSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_generations (
                id, source_id, source_provider, source_version, source_revision,
                captured_at, source_last_scan_at, published_at,
                artist_count, album_count, genre_count, track_count,
                virtual_library_count, warning_count)
            VALUES (
                $id, $sourceId, $provider, $version, $revision,
                $capturedAt, $lastScanAt, $publishedAt,
                $artists, $albums, $genres, $tracks, $libraries, $warnings);
            """;
        Add(command, "$id", generation.Id);
        Add(command, "$sourceId", snapshot.Source.Id);
        Add(command, "$provider", snapshot.Source.Provider);
        Add(command, "$version", snapshot.Source.Version);
        Add(command, "$revision", snapshot.Source.Revision);
        Add(command, "$capturedAt", FormatDate(snapshot.CapturedAt));
        Add(command, "$lastScanAt", snapshot.SourceLastScanAt is null
            ? null
            : FormatDate(snapshot.SourceLastScanAt.Value));
        Add(command, "$publishedAt", FormatDate(generation.PublishedAt));
        Add(command, "$artists", generation.ArtistCount);
        Add(command, "$albums", generation.AlbumCount);
        Add(command, "$genres", generation.GenreCount);
        Add(command, "$tracks", generation.TrackCount);
        Add(command, "$libraries", generation.VirtualLibraryCount);
        Add(command, "$warnings", generation.WarningCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertArtistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId,
        IReadOnlyList<CatalogueImportArtist> artists,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_artists (generation_id, source_id, name, external_id)
            VALUES ($generationId, $sourceId, $name, $externalId);
            """;
        Add(command, "$generationId", generationId);
        var sourceId = Add(command, "$sourceId", null);
        var name = Add(command, "$name", null);
        var externalId = Add(command, "$externalId", null);
        foreach (var artist in artists)
        {
            sourceId.Value = artist.SourceId;
            name.Value = artist.Name;
            externalId.Value = DbValue(artist.ExternalId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertAlbumsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId,
        IReadOnlyList<CatalogueImportAlbum> albums,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_albums (
                generation_id, source_id, title, album_artist_source_id, year,
                disc_count, is_compilation, release_type, artwork_track_source_id, external_id)
            VALUES (
                $generationId, $sourceId, $title, $artistId, $year,
                $discCount, $compilation, $releaseType, $artworkTrackId, $externalId);
            """;
        Add(command, "$generationId", generationId);
        var sourceId = Add(command, "$sourceId", null);
        var title = Add(command, "$title", null);
        var artistId = Add(command, "$artistId", null);
        var year = Add(command, "$year", null);
        var discCount = Add(command, "$discCount", null);
        var compilation = Add(command, "$compilation", null);
        var releaseType = Add(command, "$releaseType", null);
        var artworkTrackId = Add(command, "$artworkTrackId", null);
        var externalId = Add(command, "$externalId", null);
        foreach (var album in albums)
        {
            sourceId.Value = album.SourceId;
            title.Value = album.Title;
            artistId.Value = DbValue(album.AlbumArtistSourceId);
            year.Value = DbValue(album.Year);
            discCount.Value = DbValue(album.DiscCount);
            compilation.Value = DbValue(album.IsCompilation is null ? null : album.IsCompilation.Value ? 1 : 0);
            releaseType.Value = DbValue(album.ReleaseType);
            artworkTrackId.Value = DbValue(album.ArtworkTrackSourceId);
            externalId.Value = DbValue(album.ExternalId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertGenresAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId,
        IReadOnlyList<CatalogueImportGenre> genres,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_genres (generation_id, source_id, name)
            VALUES ($generationId, $sourceId, $name);
            """;
        Add(command, "$generationId", generationId);
        var sourceId = Add(command, "$sourceId", null);
        var name = Add(command, "$name", null);
        foreach (var genre in genres)
        {
            sourceId.Value = genre.SourceId;
            name.Value = genre.Name;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertTracksAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId,
        IReadOnlyList<CatalogueImportTrack> tracks,
        CancellationToken cancellationToken)
    {
        await using var trackCommand = CreateTrackCommand(connection, transaction, generationId);
        await using var artistCommand = CreateTrackArtistCommand(connection, transaction, generationId);
        await using var genreCommand = CreateTrackGenreCommand(connection, transaction, generationId);
        await using var statisticsCommand = CreateTrackStatisticsCommand(connection, transaction, generationId);
        foreach (var track in tracks)
        {
            SetTrackParameters(trackCommand, track);
            await trackCommand.ExecuteNonQueryAsync(cancellationToken);

            foreach (var artistId in track.ArtistSourceIds)
            {
                artistCommand.Parameters["$trackId"].Value = track.SourceId;
                artistCommand.Parameters["$artistId"].Value = artistId;
                await artistCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var genreId in track.GenreSourceIds)
            {
                genreCommand.Parameters["$trackId"].Value = track.SourceId;
                genreCommand.Parameters["$genreId"].Value = genreId;
                await genreCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var statistics in track.Statistics)
            {
                statisticsCommand.Parameters["$trackId"].Value = track.SourceId;
                statisticsCommand.Parameters["$source"].Value = statistics.Source;
                statisticsCommand.Parameters["$rating"].Value = DbValue(statistics.Rating);
                statisticsCommand.Parameters["$playCount"].Value = DbValue(statistics.PlayCount);
                statisticsCommand.Parameters["$lastPlayedAt"].Value = statistics.LastPlayedAt is null
                    ? DBNull.Value
                    : FormatDate(statistics.LastPlayedAt.Value);
                await statisticsCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static SqliteCommand CreateTrackCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_tracks (
                generation_id, source_id, title, subtitle, url, content_type, is_remote,
                external_id, album_source_id, year, disc_number, disc_count, track_number,
                duration_seconds, file_size_bytes, sample_rate, added_at, source_modified_at,
                source_updated_at, release_type, is_compilation, artwork_track_source_id,
                work_source_id, work_title, performance, grouping_name)
            VALUES (
                $generationId, $sourceId, $title, $subtitle, $url, $contentType, $remote,
                $externalId, $albumId, $year, $discNumber, $discCount, $trackNumber,
                $duration, $fileSize, $sampleRate, $addedAt, $modifiedAt,
                $updatedAt, $releaseType, $compilation, $artworkTrackId,
                $workId, $workTitle, $performance, $grouping);
            """;
        Add(command, "$generationId", generationId);
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

        return command;
    }

    private static void SetTrackParameters(SqliteCommand command, CatalogueImportTrack track)
    {
        command.Parameters["$sourceId"].Value = track.SourceId;
        command.Parameters["$title"].Value = track.Title;
        command.Parameters["$subtitle"].Value = DbValue(track.Subtitle);
        command.Parameters["$url"].Value = track.Url;
        command.Parameters["$contentType"].Value = DbValue(track.ContentType);
        command.Parameters["$remote"].Value = track.IsRemote ? 1 : 0;
        command.Parameters["$externalId"].Value = DbValue(track.ExternalId);
        command.Parameters["$albumId"].Value = DbValue(track.AlbumSourceId);
        command.Parameters["$year"].Value = DbValue(track.Year);
        command.Parameters["$discNumber"].Value = DbValue(track.DiscNumber);
        command.Parameters["$discCount"].Value = DbValue(track.DiscCount);
        command.Parameters["$trackNumber"].Value = DbValue(track.TrackNumber);
        command.Parameters["$duration"].Value = DbValue(track.DurationSeconds);
        command.Parameters["$fileSize"].Value = DbValue(track.FileSizeBytes);
        command.Parameters["$sampleRate"].Value = DbValue(track.SampleRate);
        command.Parameters["$addedAt"].Value = DbDate(track.AddedAt);
        command.Parameters["$modifiedAt"].Value = DbDate(track.SourceModifiedAt);
        command.Parameters["$updatedAt"].Value = DbDate(track.SourceUpdatedAt);
        command.Parameters["$releaseType"].Value = DbValue(track.ReleaseType);
        command.Parameters["$compilation"].Value = DbValue(
            track.IsCompilation is null ? null : track.IsCompilation.Value ? 1 : 0);
        command.Parameters["$artworkTrackId"].Value = DbValue(track.ArtworkTrackSourceId);
        command.Parameters["$workId"].Value = DbValue(track.WorkSourceId);
        command.Parameters["$workTitle"].Value = DbValue(track.WorkTitle);
        command.Parameters["$performance"].Value = DbValue(track.Performance);
        command.Parameters["$grouping"].Value = DbValue(track.Grouping);
    }

    private static SqliteCommand CreateTrackArtistCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_track_artists (
                generation_id, track_source_id, artist_source_id)
            VALUES ($generationId, $trackId, $artistId);
            """;
        Add(command, "$generationId", generationId);
        Add(command, "$trackId", null);
        Add(command, "$artistId", null);
        return command;
    }

    private static SqliteCommand CreateTrackGenreCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_track_genres (generation_id, track_source_id, genre_source_id)
            VALUES ($generationId, $trackId, $genreId);
            """;
        Add(command, "$generationId", generationId);
        Add(command, "$trackId", null);
        Add(command, "$genreId", null);
        return command;
    }

    private static SqliteCommand CreateTrackStatisticsCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_track_statistics (
                generation_id, track_source_id, source, rating, play_count, last_played_at)
            VALUES ($generationId, $trackId, $source, $rating, $playCount, $lastPlayedAt);
            """;
        Add(command, "$generationId", generationId);
        Add(command, "$trackId", null);
        Add(command, "$source", null);
        Add(command, "$rating", null);
        Add(command, "$playCount", null);
        Add(command, "$lastPlayedAt", null);
        return command;
    }

    private static async Task InsertVirtualLibrariesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId,
        IReadOnlyList<CatalogueImportVirtualLibrary> libraries,
        CancellationToken cancellationToken)
    {
        await using var libraryCommand = connection.CreateCommand();
        libraryCommand.Transaction = transaction;
        libraryCommand.CommandText = """
            INSERT INTO catalogue_virtual_libraries (generation_id, source_id, name)
            VALUES ($generationId, $sourceId, $name);
            """;
        Add(libraryCommand, "$generationId", generationId);
        Add(libraryCommand, "$sourceId", null);
        Add(libraryCommand, "$name", null);

        await using var memberCommand = connection.CreateCommand();
        memberCommand.Transaction = transaction;
        memberCommand.CommandText = """
            INSERT INTO catalogue_virtual_library_tracks (
                generation_id, library_source_id, track_source_id)
            VALUES ($generationId, $libraryId, $trackId);
            """;
        Add(memberCommand, "$generationId", generationId);
        Add(memberCommand, "$libraryId", null);
        Add(memberCommand, "$trackId", null);

        foreach (var library in libraries)
        {
            libraryCommand.Parameters["$sourceId"].Value = library.SourceId;
            libraryCommand.Parameters["$name"].Value = library.Name;
            await libraryCommand.ExecuteNonQueryAsync(cancellationToken);
            foreach (var trackId in library.TrackSourceIds)
            {
                memberCommand.Parameters["$libraryId"].Value = library.SourceId;
                memberCommand.Parameters["$trackId"].Value = trackId;
                await memberCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task InsertWarningsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string generationId,
        IReadOnlyList<CatalogueImportWarning> warnings,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalogue_warnings (generation_id, code, message, occurrences)
            VALUES ($generationId, $code, $message, $occurrences);
            """;
        Add(command, "$generationId", generationId);
        Add(command, "$code", null);
        Add(command, "$message", null);
        Add(command, "$occurrences", null);
        foreach (var warning in warnings)
        {
            command.Parameters["$code"].Value = warning.Code;
            command.Parameters["$message"].Value = warning.Message;
            command.Parameters["$occurrences"].Value = warning.Occurrences;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task PublishGenerationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PublishedCatalogueGeneration generation,
        string refreshId,
        DateTimeOffset completedAt,
        long durationMilliseconds,
        CancellationToken cancellationToken)
    {
        await using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = """
                INSERT INTO catalogue_state (id, published_generation_id)
                VALUES (1, $generationId)
                ON CONFLICT(id) DO UPDATE SET published_generation_id = excluded.published_generation_id;
                """;
            Add(state, "$generationId", generation.Id);
            await state.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var refresh = connection.CreateCommand())
        {
            refresh.Transaction = transaction;
            refresh.CommandText = """
                UPDATE catalogue_refresh_runs
                SET status = 'succeeded',
                    completed_at = $completedAt,
                    duration_ms = $duration,
                    published_generation_id = $generationId,
                    failure_message = NULL
                WHERE id = $refreshId AND status = 'running';
                """;
            Add(refresh, "$completedAt", FormatDate(completedAt));
            Add(refresh, "$duration", durationMilliseconds);
            Add(refresh, "$generationId", generation.Id);
            Add(refresh, "$refreshId", refreshId);
            if (await refresh.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The catalogue refresh run was not active.");
            }
        }

        await using var cleanup = connection.CreateCommand();
        cleanup.Transaction = transaction;
        cleanup.CommandText = "DELETE FROM catalogue_generations WHERE id <> $generationId;";
        Add(cleanup, "$generationId", generation.Id);
        await cleanup.ExecuteNonQueryAsync(cancellationToken);
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
        if (value is null)
        {
            return 0;
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static PublishedCatalogueGeneration ReadGeneration(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        ParseDate(reader.GetString(4)),
        reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)),
        ParseDate(reader.GetString(6)),
        reader.GetInt32(7),
        reader.GetInt32(8),
        reader.GetInt32(9),
        reader.GetInt32(10),
        reader.GetInt32(11),
        reader.GetInt32(12));

    private static CatalogueRefreshRun ReadRefreshRun(SqliteDataReader reader) => new(
        reader.GetString(0),
        ParseStatus(reader.GetString(1)),
        ParseDate(reader.GetString(2)),
        reader.IsDBNull(3) ? null : ParseDate(reader.GetString(3)),
        reader.IsDBNull(4) ? null : reader.GetInt64(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6));

    private static CatalogueRefreshRunStatus ParseStatus(string value) => value switch
    {
        "running" => CatalogueRefreshRunStatus.Running,
        "succeeded" => CatalogueRefreshRunStatus.Succeeded,
        "failed" => CatalogueRefreshRunStatus.Failed,
        "cancelled" => CatalogueRefreshRunStatus.Cancelled,
        "interrupted" => CatalogueRefreshRunStatus.Interrupted,
        _ => throw new InvalidOperationException("Unknown stored catalogue refresh status.")
    };

    private static string ToText(CatalogueRefreshRunStatus value) => value switch
    {
        CatalogueRefreshRunStatus.Running => "running",
        CatalogueRefreshRunStatus.Succeeded => "succeeded",
        CatalogueRefreshRunStatus.Failed => "failed",
        CatalogueRefreshRunStatus.Cancelled => "cancelled",
        CatalogueRefreshRunStatus.Interrupted => "interrupted",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown catalogue refresh status.")
    };

    private static SqliteParameter Add(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = DbValue(value);
        command.Parameters.Add(parameter);
        return parameter;
    }

    private static object DbValue(object? value) => value ?? DBNull.Value;
    private static object DbDate(DateTimeOffset? value) => value is null ? DBNull.Value : FormatDate(value.Value);
    private static string FormatDate(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS catalogue_schema (version INTEGER NOT NULL);
        INSERT INTO catalogue_schema (version)
            SELECT 2 WHERE NOT EXISTS (SELECT 1 FROM catalogue_schema);

        CREATE TABLE IF NOT EXISTS catalogue_generations (
            id TEXT PRIMARY KEY,
            source_id TEXT NOT NULL,
            source_provider TEXT NOT NULL,
            source_version TEXT NULL,
            source_revision TEXT NULL,
            captured_at TEXT NOT NULL,
            source_last_scan_at TEXT NULL,
            published_at TEXT NOT NULL,
            artist_count INTEGER NOT NULL,
            album_count INTEGER NOT NULL,
            genre_count INTEGER NOT NULL,
            track_count INTEGER NOT NULL,
            virtual_library_count INTEGER NOT NULL,
            warning_count INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS catalogue_state (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            published_generation_id TEXT NOT NULL,
            FOREIGN KEY (published_generation_id) REFERENCES catalogue_generations(id)
        );
        CREATE TABLE IF NOT EXISTS catalogue_refresh_runs (
            id TEXT PRIMARY KEY,
            status TEXT NOT NULL CHECK (status IN ('running', 'succeeded', 'failed', 'cancelled', 'interrupted')),
            started_at TEXT NOT NULL,
            completed_at TEXT NULL,
            duration_ms INTEGER NULL,
            published_generation_id TEXT NULL,
            failure_message TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_catalogue_refresh_runs_started_at
            ON catalogue_refresh_runs(started_at DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_catalogue_refresh_runs_running
            ON catalogue_refresh_runs(status) WHERE status = 'running';

        CREATE TABLE IF NOT EXISTS catalogue_artists (
            generation_id TEXT NOT NULL,
            source_id TEXT NOT NULL,
            name TEXT NOT NULL,
            external_id TEXT NULL,
            PRIMARY KEY (generation_id, source_id),
            FOREIGN KEY (generation_id) REFERENCES catalogue_generations(id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_albums (
            generation_id TEXT NOT NULL,
            source_id TEXT NOT NULL,
            title TEXT NOT NULL,
            album_artist_source_id TEXT NULL,
            year INTEGER NULL,
            disc_count INTEGER NULL,
            is_compilation INTEGER NULL,
            release_type TEXT NULL,
            artwork_track_source_id TEXT NULL,
            external_id TEXT NULL,
            PRIMARY KEY (generation_id, source_id),
            FOREIGN KEY (generation_id) REFERENCES catalogue_generations(id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_genres (
            generation_id TEXT NOT NULL,
            source_id TEXT NOT NULL,
            name TEXT NOT NULL,
            PRIMARY KEY (generation_id, source_id),
            FOREIGN KEY (generation_id) REFERENCES catalogue_generations(id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_tracks (
            generation_id TEXT NOT NULL,
            source_id TEXT NOT NULL,
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
            PRIMARY KEY (generation_id, source_id),
            FOREIGN KEY (generation_id) REFERENCES catalogue_generations(id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_track_artists (
            generation_id TEXT NOT NULL,
            track_source_id TEXT NOT NULL,
            artist_source_id TEXT NOT NULL,
            PRIMARY KEY (generation_id, track_source_id, artist_source_id),
            FOREIGN KEY (generation_id, track_source_id)
                REFERENCES catalogue_tracks(generation_id, source_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_track_genres (
            generation_id TEXT NOT NULL,
            track_source_id TEXT NOT NULL,
            genre_source_id TEXT NOT NULL,
            PRIMARY KEY (generation_id, track_source_id, genre_source_id),
            FOREIGN KEY (generation_id, track_source_id)
                REFERENCES catalogue_tracks(generation_id, source_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_track_statistics (
            generation_id TEXT NOT NULL,
            track_source_id TEXT NOT NULL,
            source TEXT NOT NULL,
            rating INTEGER NULL,
            play_count INTEGER NULL,
            last_played_at TEXT NULL,
            PRIMARY KEY (generation_id, track_source_id, source),
            FOREIGN KEY (generation_id, track_source_id)
                REFERENCES catalogue_tracks(generation_id, source_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_virtual_libraries (
            generation_id TEXT NOT NULL,
            source_id TEXT NOT NULL,
            name TEXT NOT NULL,
            PRIMARY KEY (generation_id, source_id),
            FOREIGN KEY (generation_id) REFERENCES catalogue_generations(id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_virtual_library_tracks (
            generation_id TEXT NOT NULL,
            library_source_id TEXT NOT NULL,
            track_source_id TEXT NOT NULL,
            PRIMARY KEY (generation_id, library_source_id, track_source_id),
            FOREIGN KEY (generation_id, library_source_id)
                REFERENCES catalogue_virtual_libraries(generation_id, source_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS catalogue_warnings (
            generation_id TEXT NOT NULL,
            code TEXT NOT NULL,
            message TEXT NOT NULL,
            occurrences INTEGER NOT NULL,
            PRIMARY KEY (generation_id, code),
            FOREIGN KEY (generation_id) REFERENCES catalogue_generations(id) ON DELETE CASCADE
        );
        """;

    private const string Migration1To2Sql = """
        BEGIN IMMEDIATE;
        ALTER TABLE catalogue_generations RENAME COLUMN contributor_count TO artist_count;
        CREATE TABLE catalogue_artists (
            generation_id TEXT NOT NULL,
            source_id TEXT NOT NULL,
            name TEXT NOT NULL,
            external_id TEXT NULL,
            PRIMARY KEY (generation_id, source_id),
            FOREIGN KEY (generation_id) REFERENCES catalogue_generations(id) ON DELETE CASCADE
        );
        INSERT INTO catalogue_artists (generation_id, source_id, name, external_id)
            SELECT contributor.generation_id, contributor.source_id,
                   contributor.name, contributor.external_id
            FROM catalogue_contributors contributor
            WHERE EXISTS (
                SELECT 1
                FROM catalogue_track_contributors track_artist
                WHERE track_artist.generation_id = contributor.generation_id
                  AND track_artist.contributor_source_id = contributor.source_id
                  AND track_artist.role = 'ARTIST'
            ) OR EXISTS (
                SELECT 1
                FROM catalogue_albums album
                WHERE album.generation_id = contributor.generation_id
                  AND album.album_artist_source_id = contributor.source_id
            );
        CREATE TABLE catalogue_track_artists (
            generation_id TEXT NOT NULL,
            track_source_id TEXT NOT NULL,
            artist_source_id TEXT NOT NULL,
            PRIMARY KEY (generation_id, track_source_id, artist_source_id),
            FOREIGN KEY (generation_id, track_source_id)
                REFERENCES catalogue_tracks(generation_id, source_id) ON DELETE CASCADE
        );
        INSERT INTO catalogue_track_artists (generation_id, track_source_id, artist_source_id)
            SELECT generation_id, track_source_id, contributor_source_id
            FROM catalogue_track_contributors
            WHERE role = 'ARTIST';
        DELETE FROM catalogue_warnings WHERE code = 'missing-contributor';
        INSERT INTO catalogue_warnings (generation_id, code, message, occurrences)
            SELECT reference.generation_id,
                   'missing-artist',
                   'Track or album artist references were not present in the imported artist set.',
                   COUNT(*)
            FROM (
                SELECT generation_id, album_artist_source_id AS artist_source_id
                FROM catalogue_albums
                WHERE album_artist_source_id IS NOT NULL
                UNION ALL
                SELECT generation_id, contributor_source_id AS artist_source_id
                FROM catalogue_track_contributors
                WHERE role = 'ARTIST'
            ) reference
            LEFT JOIN catalogue_contributors contributor
              ON contributor.generation_id = reference.generation_id
             AND contributor.source_id = reference.artist_source_id
            WHERE contributor.source_id IS NULL
            GROUP BY reference.generation_id;
        UPDATE catalogue_generations
        SET artist_count = (
                SELECT COUNT(*)
                FROM catalogue_artists artist
                WHERE artist.generation_id = catalogue_generations.id
            ),
            warning_count = (
                SELECT COUNT(*)
                FROM catalogue_warnings warning
                WHERE warning.generation_id = catalogue_generations.id
            );
        DROP TABLE catalogue_track_contributors;
        DROP TABLE catalogue_contributors;
        UPDATE catalogue_schema SET version = 2;
        COMMIT;
        """;
}
