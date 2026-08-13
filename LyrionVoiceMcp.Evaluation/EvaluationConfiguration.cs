using System.Text.Json;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Evaluation;

public abstract record EvaluationConfigurationOutcome;

public sealed record EvaluationConfigurationLoaded(
    LmsConnectionSettings Settings) : EvaluationConfigurationOutcome;

public sealed record EvaluationConfigurationRejected(
    string Error) : EvaluationConfigurationOutcome;

public static class EvaluationConfiguration
{
    public static async Task<EvaluationConfigurationOutcome> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new EvaluationConfigurationRejected(
                $"LMS settings file was not found: {path}");
        }

        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("LyrionVoiceMcpLms", out var section)
                || section.ValueKind != JsonValueKind.Object)
            {
                return new EvaluationConfigurationRejected(
                    "LMS settings file does not contain a LyrionVoiceMcpLms object.");
            }

            var serverId = Environment.GetEnvironmentVariable("LyrionVoiceMcpLms__ServerId")
                ?? ReadValue(section, "ServerId");
            var baseUrl = Environment.GetEnvironmentVariable("LyrionVoiceMcpLms__BaseUrl")
                ?? ReadValue(section, "BaseUrl");
            var requestTimeout = Environment.GetEnvironmentVariable(
                    "LyrionVoiceMcpLms__RequestTimeoutSeconds")
                ?? ReadValue(section, "RequestTimeoutSeconds");
            var settings = LmsConnectionSettings.FromValues(
                serverId,
                baseUrl,
                requestTimeout);
            if (!settings.IsConfigured)
            {
                return new EvaluationConfigurationRejected(
                    "LMS is not configured in the evaluation settings file.");
            }

            return new EvaluationConfigurationLoaded(settings);
        }
        catch (JsonException exception)
        {
            return new EvaluationConfigurationRejected(
                $"LMS settings JSON is invalid: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return new EvaluationConfigurationRejected(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new EvaluationConfigurationRejected(
                $"LMS settings file could not be read: {exception.Message}");
        }
    }

    private static string? ReadValue(JsonElement section, string propertyName)
    {
        if (!section.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }
}
