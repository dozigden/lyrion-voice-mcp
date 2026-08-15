using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Evaluation;

public abstract record EvaluationConfigurationOutcome;

public sealed record EvaluationConfigurationLoaded(
    LmsConnectionSettings Settings) : EvaluationConfigurationOutcome;

public sealed record EvaluationConfigurationRejected(
    string Error) : EvaluationConfigurationOutcome;

public static class EvaluationConfiguration
{
    public const string BaseUrlEnvironmentVariable = "LVM_EVALUATION_LMS_BASE_URL";
    private const string EvaluationServerId = "live-evaluation";

    public static EvaluationConfigurationOutcome LoadFromEnvironment() =>
        FromBaseUrl(Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable));

    public static EvaluationConfigurationOutcome FromBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new EvaluationConfigurationRejected(
                $"Set {BaseUrlEnvironmentVariable} to the live LMS HTTP or HTTPS origin before running evaluation.");
        }

        try
        {
            var settings = LmsConnectionSettings.FromValues(
                EvaluationServerId,
                baseUrl,
                requestTimeoutSeconds: null);

            return new EvaluationConfigurationLoaded(settings);
        }
        catch (InvalidOperationException exception)
        {
            return new EvaluationConfigurationRejected(
                $"{BaseUrlEnvironmentVariable} is invalid: {exception.Message}");
        }
    }
}
