namespace LyrionVoiceMcp.Lms;

public sealed record LmsConnectionSettings(
    string? ServerId,
    Uri? BaseUrl,
    TimeSpan RequestTimeout)
{
    public const int DefaultRequestTimeoutSeconds = 5;
    public const int MaximumRequestTimeoutSeconds = 30;

    public bool IsConfigured => ServerId is not null && BaseUrl is not null;

    public Uri? JsonRpcUrl => BaseUrl is null
        ? null
        : new Uri(BaseUrl.GetLeftPart(UriPartial.Authority) + "/jsonrpc.js");

    public static LmsConnectionSettings FromValues(
        string? serverId,
        string? baseUrl,
        string? requestTimeoutSeconds)
    {
        var normalisedServerId = NormaliseOptional(serverId);
        var normalisedBaseUrl = NormaliseOptional(baseUrl);

        if (normalisedServerId is null && normalisedBaseUrl is null)
        {
            return new LmsConnectionSettings(
                null,
                null,
                ParseRequestTimeout(requestTimeoutSeconds));
        }

        if (normalisedServerId is null)
        {
            throw new InvalidOperationException(
                "LyrionVoiceMcpLms:ServerId is required when an LMS base URL is configured.");
        }

        if (normalisedBaseUrl is null)
        {
            throw new InvalidOperationException(
                "LyrionVoiceMcpLms:BaseUrl is required when an LMS server identity is configured.");
        }

        if (!Uri.TryCreate(normalisedBaseUrl.TrimEnd('/'), UriKind.Absolute, out var parsedBaseUrl)
            || !IsHttp(parsedBaseUrl))
        {
            throw new InvalidOperationException(
                "LyrionVoiceMcpLms:BaseUrl must be an absolute HTTP or HTTPS origin.");
        }

        if (!IsOriginOnly(parsedBaseUrl))
        {
            throw new InvalidOperationException(
                "LyrionVoiceMcpLms:BaseUrl must not include credentials, a path, query, or fragment.");
        }

        return new LmsConnectionSettings(
            normalisedServerId,
            parsedBaseUrl,
            ParseRequestTimeout(requestTimeoutSeconds));
    }

    private static TimeSpan ParseRequestTimeout(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.FromSeconds(DefaultRequestTimeoutSeconds);
        }

        if (!int.TryParse(value, out var seconds)
            || seconds < 1
            || seconds > MaximumRequestTimeoutSeconds)
        {
            throw new InvalidOperationException(
                $"LyrionVoiceMcpLms:RequestTimeoutSeconds must be between 1 and {MaximumRequestTimeoutSeconds}.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static string? NormaliseOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsHttp(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool IsOriginOnly(Uri uri) =>
        string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo);
}
