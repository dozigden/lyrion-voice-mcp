using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LyrionVoiceMcp.Contracts;

public sealed record SearchRequest(string Query);

public sealed record SearchArtist(string Name, string BrowseRef);

public sealed record SearchExactArtistMatch(
    string Name,
    [property: Description("The number of distinct canonical catalogue album identities considered for this search's exact-artist album preview. Null when album expansion was not performed. Separate LMS album records count separately when their catalogue identities differ.")]
    int? DiscographyAlbumCount,
    string DiscographyBrowseRef);

public sealed record SearchAlbum(
    string Title,
    string? Artist,
    string BrowseRef,
    string PlayRef);

public sealed record SearchTrack(
    string Title,
    string? Artist,
    string? Album,
    [property: Description("The track's numeric 0 to 5 star rating, including decimals such as 4.5.")]
    decimal Rating,
    string PlayRef);

public sealed record SearchPlaylist(
    string Title,
    string BrowseRef,
    string PlayRef);

public sealed record SearchResponse(
    string Guidance,
    SearchExactArtistMatch? ExactArtistMatch,
    IReadOnlyList<SearchArtist> Artists,
    IReadOnlyList<SearchAlbum> Albums,
    IReadOnlyList<SearchTrack> TopTracks,
    IReadOnlyList<SearchTrack> Tracks,
    IReadOnlyList<SearchPlaylist> Playlists);

[JsonConverter(typeof(JsonStringEnumConverter<BrowseEntityKind>))]
public enum BrowseEntityKind
{
    Category,

    [JsonStringEnumMemberName("album_artist")]
    AlbumArtist,

    Artist,
    Album,
    Genre,
    Playlist,
    Track,
    Year
}

public sealed record BrowseResponse(
    string Guidance,
    IReadOnlyList<BrowseItem> Items,
    string? NextBrowseRef);

public sealed record BrowseItem(
    BrowseEntityKind Kind,
    string Title,
    string? Artist,
    string? Album)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BrowseRef { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlayRef { get; init; }

    [Description("For tracks returned from rating browse, the numeric 0 to 5 rating. Omitted otherwise.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Rating { get; init; }
}

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

public sealed record GetQueueRequest(string Player);

public sealed record GetQueueResponse(
    string Player,
    int? CurrentIndex,
    IReadOnlyList<QueueItem> Items);

public sealed record QueueItem(
    int Index,
    string Title,
    string? Artist,
    string? Album,
    double? DurationSeconds);

[JsonConverter(typeof(ManageQueueActionJsonConverter))]
public enum ManageQueueAction
{
    [JsonStringEnumMemberName("clear")]
    Clear,

    [JsonStringEnumMemberName("append")]
    Append,

    [JsonStringEnumMemberName("insert_next")]
    InsertNext
}

public sealed class ManageQueueActionJsonConverter
    : JsonConverter<ManageQueueAction>
{
    private const ManageQueueAction InvalidAction = (ManageQueueAction)(-1);

    public override bool HandleNull => true;

    public override ManageQueueAction Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() switch
            {
                "clear" => ManageQueueAction.Clear,
                "append" => ManageQueueAction.Append,
                "insert_next" => ManageQueueAction.InsertNext,
                _ => InvalidAction
            };
        }

        reader.Skip();
        return InvalidAction;
    }

    public override void Write(
        Utf8JsonWriter writer,
        ManageQueueAction value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            ManageQueueAction.Clear => "clear",
            ManageQueueAction.Append => "append",
            ManageQueueAction.InsertNext => "insert_next",
            _ => throw new JsonException(
                $"Unsupported queue management action {value}.")
        });
}

public sealed record ManageQueueRequest(
    string Player,
    ManageQueueAction Action,
    IReadOnlyList<string>? Items = null);

public sealed record ManageQueueResponse(
    string Player,
    int? QueueLength,
    int RequestedItemCount,
    int CompletedItemCount,
    IReadOnlyList<SkippedItem> SkippedItems,
    string? StateRefreshError);

public sealed record SkippedItem(
    int Index,
    string Reason,
    string Message);

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

public sealed record PlayRequest(
    string Player,
    IReadOnlyList<string> Items);

public sealed record PlayResponse(
    PlayerStatus? Player,
    int RequestedItemCount,
    int CompletedItemCount,
    IReadOnlyList<SkippedItem> SkippedItems,
    string? StateRefreshError);
