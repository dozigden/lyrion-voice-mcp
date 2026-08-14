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

[JsonConverter(typeof(PlayerControlActionJsonConverter))]
public enum PlayerControlAction
{
    [JsonStringEnumMemberName("resume")]
    Resume,

    [JsonStringEnumMemberName("pause")]
    Pause,

    [JsonStringEnumMemberName("stop")]
    Stop,

    [JsonStringEnumMemberName("next")]
    Next,

    [JsonStringEnumMemberName("previous")]
    Previous,

    [JsonStringEnumMemberName("power_on")]
    PowerOn,

    [JsonStringEnumMemberName("power_off")]
    PowerOff
}

public sealed class PlayerControlActionJsonConverter
    : JsonConverter<PlayerControlAction>
{
    private const PlayerControlAction InvalidAction = (PlayerControlAction)(-1);

    public override bool HandleNull => true;

    public override PlayerControlAction Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() switch
            {
                "resume" => PlayerControlAction.Resume,
                "pause" => PlayerControlAction.Pause,
                "stop" => PlayerControlAction.Stop,
                "next" => PlayerControlAction.Next,
                "previous" => PlayerControlAction.Previous,
                "power_on" => PlayerControlAction.PowerOn,
                "power_off" => PlayerControlAction.PowerOff,
                _ => InvalidAction
            };
        }

        reader.Skip();
        return InvalidAction;
    }

    public override void Write(
        Utf8JsonWriter writer,
        PlayerControlAction value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            PlayerControlAction.Resume => "resume",
            PlayerControlAction.Pause => "pause",
            PlayerControlAction.Stop => "stop",
            PlayerControlAction.Next => "next",
            PlayerControlAction.Previous => "previous",
            PlayerControlAction.PowerOn => "power_on",
            PlayerControlAction.PowerOff => "power_off",
            _ => throw new JsonException(
                $"Unsupported player control action {value}.")
        });
}

public sealed record ControlPlayerRequest(
    string Player,
    PlayerControlAction Action);

public sealed record ControlPlayerResponse(PlayerStatus Player);

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
    PlayerPlaybackMode Mode,
    int? Volume,
    bool? Muted,
    NowPlaying? NowPlaying);

public sealed record NowPlaying(
    string Title,
    string? Artist,
    string? Album,
    double? DurationSeconds,
    double? ElapsedSeconds);

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
