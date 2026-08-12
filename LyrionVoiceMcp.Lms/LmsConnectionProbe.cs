using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsConnectionProbe(
    LmsConnectionSettings settings,
    LmsJsonRpcClient jsonRpcClient) : ILmsConnectionProbe
{
    public async Task<LmsConnectionStatus> CheckAsync(CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return new LmsConnectionStatus(
                LmsConnectionState.NotConfigured,
                null,
                null,
                null,
                "Configure an LMS server identity and base URL to test connectivity.");
        }

        try
        {
            var result = await jsonRpcClient.SendAsync(
                ["serverstatus", 0, 0],
                cancellationToken);
            var version = ReadServerVersion(result);

            return new LmsConnectionStatus(
                LmsConnectionState.Online,
                settings.ServerId,
                settings.BaseUrl?.AbsoluteUri.TrimEnd('/'),
                version,
                "The configured LMS JSON-RPC endpoint is responding.");
        }
        catch (LmsRequestException exception)
        {
            return Unavailable(exception.Message);
        }
    }

    private static string? ReadServerVersion(JsonElement result)
    {
        if (!result.TryGetProperty("version", out var version))
        {
            return null;
        }

        return version.ValueKind switch
        {
            JsonValueKind.String => version.GetString(),
            JsonValueKind.Number => version.GetRawText(),
            _ => null
        };
    }

    private LmsConnectionStatus Unavailable(string message) =>
        new(
            LmsConnectionState.Unavailable,
            settings.ServerId,
            settings.BaseUrl?.AbsoluteUri.TrimEnd('/'),
            null,
            message);
}
