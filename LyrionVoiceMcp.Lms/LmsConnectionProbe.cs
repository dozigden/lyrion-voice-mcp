using System.Net.Http.Json;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsConnectionProbe(
    LmsConnectionSettings settings,
    HttpClient httpClient) : ILmsConnectionProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.JsonRpcUrl)
            {
                Content = JsonContent.Create(new
                {
                    id = 1,
                    method = "slim.request",
                    @params = new object[]
                    {
                        string.Empty,
                        new object[] { "serverstatus", 0, 0 }
                    }
                }, options: JsonOptions)
            };

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Unavailable($"LMS returned HTTP {(int)response.StatusCode}.");
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                content,
                cancellationToken: cancellationToken);
            var version = ReadServerVersion(document.RootElement);

            return new LmsConnectionStatus(
                LmsConnectionState.Online,
                settings.ServerId,
                settings.BaseUrl?.AbsoluteUri.TrimEnd('/'),
                version,
                "The configured LMS JSON-RPC endpoint is responding.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(
                $"LMS did not respond within {settings.RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException)
        {
            return Unavailable("LMS could not be reached at the configured base URL.");
        }
        catch (JsonException)
        {
            return Unavailable("LMS returned an invalid JSON response.");
        }
        catch (InvalidOperationException exception)
        {
            return Unavailable(exception.Message);
        }
    }

    private static string? ReadServerVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "LMS serverstatus response did not include a result object.");
        }

        if (root.TryGetProperty("error", out var error)
            && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("LMS returned a JSON-RPC error.");
        }

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
