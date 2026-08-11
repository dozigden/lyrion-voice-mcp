namespace LyrionVoiceMcp.Contracts;

public sealed record HealthResponse(string Status);

public sealed record VersionResponse(
    string Version,
    string Channel,
    string Build,
    string Commit);

public sealed record LmsConnectionResponse(
    string Status,
    string? ServerId,
    string? BaseUrl,
    string? ServerVersion,
    string Message);
