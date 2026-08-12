using System.Text.Json;
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

[JsonConverter(typeof(PlayQueueModeJsonConverter))]
public enum PlayQueueMode
{
    [JsonStringEnumMemberName("replace")]
    Replace,

    [JsonStringEnumMemberName("append")]
    Append
}

public sealed class PlayQueueModeJsonConverter : JsonConverter<PlayQueueMode>
{
    private const PlayQueueMode InvalidMode = (PlayQueueMode)(-1);

    public override bool HandleNull => true;

    public override PlayQueueMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() switch
            {
                "replace" => PlayQueueMode.Replace,
                "append" => PlayQueueMode.Append,
                _ => InvalidMode
            };
        }

        reader.Skip();
        return InvalidMode;
    }

    public override void Write(
        Utf8JsonWriter writer,
        PlayQueueMode value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            PlayQueueMode.Replace => "replace",
            PlayQueueMode.Append => "append",
            _ => throw new JsonException(
                $"Unsupported playback queue mode {value}.")
        });
}

public sealed record PlayRequest(
    string Player,
    IReadOnlyList<string> Items,
    PlayQueueMode Mode = PlayQueueMode.Replace);

public sealed record PlayResponse(PlayerStatus Player);
