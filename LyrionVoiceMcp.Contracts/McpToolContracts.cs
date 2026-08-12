using System.Text.Json.Serialization;

namespace LyrionVoiceMcp.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<SearchEntityKind>))]
public enum SearchEntityKind
{
    Artist,
    Album,
    Track,
    Playlist
}

public sealed record SearchRequest(string Query);

public sealed record SearchCandidate(
    string Reference,
    SearchEntityKind Kind,
    string Title,
    string? Artist,
    string? Album);

public sealed record SearchResponse(IReadOnlyList<SearchCandidate> Results);

public sealed record GetPlayerStatusResponse(IReadOnlyList<PlayerStatus> Players);

[JsonConverter(typeof(JsonStringEnumConverter<PlayerPlaybackMode>))]
public enum PlayerPlaybackMode
{
    Playing,
    Paused,
    Stopped,
    Unknown
}

public sealed record PlayerStatus(
    string Id,
    string Name,
    bool PoweredOn,
    PlayerPlaybackMode Mode);

public enum PlayQueueMode
{
    Replace,
    Append
}

public sealed record PlayRequest(
    string Player,
    IReadOnlyList<string> Items,
    PlayQueueMode Mode = PlayQueueMode.Replace);

public sealed record PlayResponse(PlayerStatus Player);
