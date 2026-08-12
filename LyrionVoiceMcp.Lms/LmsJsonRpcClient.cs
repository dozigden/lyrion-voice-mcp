using System.Net.Http.Json;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsJsonRpcClient(
    LmsConnectionSettings settings,
    HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JsonElement> SendAsync(
        object[] command,
        CancellationToken cancellationToken) =>
        await SendAsync(string.Empty, command, cancellationToken);

    public async Task<JsonElement> SendAsync(
        string playerId,
        object[] command,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured || settings.JsonRpcUrl is null)
        {
            throw new LmsRequestException("LMS is not configured.");
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
                        playerId,
                        command
                    }
                }, options: JsonOptions)
            };

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new LmsRequestException(
                    $"LMS returned HTTP {(int)response.StatusCode}.");
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                content,
                cancellationToken: cancellationToken);
            return ReadResult(document.RootElement).Clone();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LmsRequestException(
                $"LMS did not respond within {settings.RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException exception)
        {
            throw new LmsRequestException(
                "LMS could not be reached at the configured base URL.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new LmsRequestException("LMS returned an invalid JSON response.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new LmsRequestException(exception.Message, exception);
        }
    }

    private static JsonElement ReadResult(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("LMS JSON-RPC response was not an object.");
        }

        if (root.TryGetProperty("error", out var error)
            && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("LMS returned a JSON-RPC error.");
        }

        if (!root.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "LMS JSON-RPC response did not include a result object.");
        }

        return result;
    }
}
