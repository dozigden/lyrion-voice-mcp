using System.Text.Json;
using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsSearchClient(LmsJsonRpcClient jsonRpcClient) :
    ILmsSearchClient,
    ILmsPlaylistSearchClient
{
    private const int ItemsPerCategory = 20;

    public async Task<LmsSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        object[] libraryCommand = ["search", 0, ItemsPerCategory, $"term:{query}"];
        object[] playlistCommand = ["playlists", 0, ItemsPerCategory, $"search:{query}"];
        var retrievalStopwatch = Stopwatch.StartNew();
        var librarySearch = SendObservedAsync(libraryCommand, cancellationToken);
        var playlistSearch = SendObservedAsync(playlistCommand, cancellationToken);

        await Task.WhenAll(librarySearch, playlistSearch);
        retrievalStopwatch.Stop();

        var candidates = new List<LmsSearchCandidate>();
        var library = await librarySearch;
        var playlist = await playlistSearch;
        var libraryObservation = AppendObservedCandidates(
            candidates,
            "library",
            libraryCommand,
            library,
            AppendLibraryCandidates);
        var playlistObservation = AppendObservedCandidates(
            candidates,
            "playlists",
            playlistCommand,
            playlist,
            AppendPlaylistCandidates);

        var response = new LmsSearchResponse(
            candidates,
            [libraryObservation.Observation, playlistObservation.Observation],
            retrievalStopwatch.ElapsedMilliseconds);

        var failure = libraryObservation.Failure ?? playlistObservation.Failure;
        if (failure is not null)
        {
            var failedSources = response.Requests
                .Where(request => request.Status == LmsSearchRequestStatus.Failed)
                .Select(request => request.Source);
            throw new LmsSearchFailedException(
                $"LMS search failed for {string.Join(" and ", failedSources)}.",
                response,
                failure);
        }

        return response;
    }

    public async Task<LmsSearchResponse> SearchPlaylistsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        object[] command = ["playlists", 0, ItemsPerCategory, $"search:{query}"];
        var stopwatch = Stopwatch.StartNew();
        var observed = await SendObservedAsync(command, cancellationToken);
        stopwatch.Stop();
        var candidates = new List<LmsSearchCandidate>();
        var appended = AppendObservedCandidates(
            candidates,
            "playlists",
            command,
            observed,
            AppendPlaylistCandidates);
        var response = new LmsSearchResponse(
            candidates,
            [appended.Observation],
            stopwatch.ElapsedMilliseconds);
        if (appended.Failure is not null)
        {
            throw new LmsSearchFailedException(
                "LMS search failed for playlists.",
                response,
                appended.Failure);
        }

        return response;
    }

    private async Task<ObservedResponse> SendObservedAsync(
        object[] command,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await jsonRpcClient.SendAsync(command, cancellationToken);
            stopwatch.Stop();
            return new ObservedResponse(result, null, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ObservedResponse(null, exception, stopwatch.ElapsedMilliseconds);
        }
    }

    private static ObservedCandidateAppend AppendObservedCandidates(
        List<LmsSearchCandidate> candidates,
        string source,
        object[] command,
        ObservedResponse response,
        Action<List<LmsSearchCandidate>, JsonElement> append)
    {
        var start = candidates.Count;
        var failure = response.Failure;
        if (failure is null && response.Result is { } result)
        {
            try
            {
                append(candidates, result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failure = exception;
            }
        }

        return new ObservedCandidateAppend(
            new LmsSearchRequestObservation(
                source,
                JsonSerializer.Serialize(command),
                failure is null ? LmsSearchRequestStatus.Completed : LmsSearchRequestStatus.Failed,
                failure?.Message,
                response.DurationMilliseconds,
                candidates.Count - start),
            failure);
    }

    private static void AppendLibraryCandidates(
        List<LmsSearchCandidate> candidates,
        JsonElement result)
    {
        AppendCandidates(
            candidates,
            result,
            "contributors_loop",
            MediaEntityKind.Artist,
            "contributor_id",
            "contributor");
        AppendCandidates(
            candidates,
            result,
            "albums_loop",
            MediaEntityKind.Album,
            "album_id",
            "album");
        AppendCandidates(
            candidates,
            result,
            "tracks_loop",
            MediaEntityKind.Track,
            "track_id",
            "track");
    }

    private static void AppendPlaylistCandidates(
        List<LmsSearchCandidate> candidates,
        JsonElement result) =>
        AppendCandidates(
            candidates,
            result,
            "playlists_loop",
            MediaEntityKind.Playlist,
            "id",
            "playlist");

    private static void AppendCandidates(
        List<LmsSearchCandidate> candidates,
        JsonElement result,
        string loopName,
        MediaEntityKind kind,
        string idName,
        string titleName)
    {
        if (!result.TryGetProperty(loopName, out var loop))
        {
            return;
        }

        if (loop.ValueKind != JsonValueKind.Array)
        {
            throw new LmsRequestException(
                $"LMS search response did not include a valid {loopName} array.");
        }

        foreach (var item in loop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new LmsRequestException(
                    $"LMS search response contained an invalid {loopName} item.");
            }

            var id = LmsJson.ReadRequiredString(item, idName, "search");
            var title = LmsJson.ReadRequiredString(item, titleName, "search");
            var artist = kind is MediaEntityKind.Album or MediaEntityKind.Track
                ? LmsJson.ReadString(item, "artist")
                : null;
            var album = kind == MediaEntityKind.Track
                ? LmsJson.ReadString(item, "album")
                : null;
            candidates.Add(new LmsSearchCandidate(
                new MediaIdentity(kind, id),
                title,
                artist,
                album));
        }
    }

    private sealed record ObservedResponse(
        JsonElement? Result,
        Exception? Failure,
        long DurationMilliseconds);

    private sealed record ObservedCandidateAppend(
        LmsSearchRequestObservation Observation,
        Exception? Failure);
}
