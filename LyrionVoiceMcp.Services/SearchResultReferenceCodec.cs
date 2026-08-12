using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class SearchResultReferenceCodec : ISearchResultReferenceCodec
{
    private const string Prefix = "result_";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Encode(SearchResultReferenceValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!Guid.TryParseExact(value.CorrelationId, "N", out _))
        {
            throw new ArgumentException(
                "The search-result correlation ID must be a compact GUID.",
                nameof(value));
        }

        if (string.IsNullOrWhiteSpace(value.Identity.Id))
        {
            throw new ArgumentException(
                "The search-result media identity must not be empty.",
                nameof(value));
        }

        var payload = new ReferencePayload(
            value.CorrelationId,
            value.Identity.Kind,
            value.Identity.Id);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return Prefix + EncodeBase64Url(bytes);
    }

    public SearchResultReferenceValue Decode(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)
            || !reference.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw InvalidReference();
        }

        try
        {
            var bytes = DecodeBase64Url(reference[Prefix.Length..]);
            var payload = JsonSerializer.Deserialize<ReferencePayload>(bytes, JsonOptions);
            if (payload is null
                || !Guid.TryParseExact(payload.CorrelationId, "N", out _)
                || !Enum.IsDefined(payload.Kind)
                || string.IsNullOrWhiteSpace(payload.MediaId))
            {
                throw InvalidReference();
            }

            return new SearchResultReferenceValue(
                payload.CorrelationId,
                new MediaIdentity(payload.Kind, payload.MediaId));
        }
        catch (JsonException)
        {
            throw InvalidReference();
        }
        catch (FormatException)
        {
            throw InvalidReference();
        }
    }

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value
            .Replace('-', '+')
            .Replace('_', '/');
        var padding = base64.Length % 4;
        if (padding != 0)
        {
            base64 = base64.PadRight(base64.Length + (4 - padding), '=');
        }

        return Convert.FromBase64String(base64);
    }

    private static FormatException InvalidReference() =>
        new("The search-result reference is invalid.");

    private sealed record ReferencePayload(
        string CorrelationId,
        MediaEntityKind Kind,
        string MediaId);
}
