namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class EvaluationConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void FromBaseUrl_requires_the_evaluation_environment_value(string? value)
    {
        var outcome = EvaluationConfiguration.FromBaseUrl(value);

        var rejected = Assert.IsType<EvaluationConfigurationRejected>(outcome);
        Assert.Contains(
            EvaluationConfiguration.BaseUrlEnvironmentVariable,
            rejected.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FromBaseUrl_builds_isolated_live_evaluation_settings()
    {
        var outcome = EvaluationConfiguration.FromBaseUrl("https://music.example.test:9000/");

        var loaded = Assert.IsType<EvaluationConfigurationLoaded>(outcome);
        Assert.Equal("live-evaluation", loaded.Settings.ServerId);
        Assert.Equal("https://music.example.test:9000/", loaded.Settings.BaseUrl?.AbsoluteUri);
    }

    [Fact]
    public void FromBaseUrl_rejects_a_non_origin_url()
    {
        var outcome = EvaluationConfiguration.FromBaseUrl(
            "https://music.example.test:9000/jsonrpc.js");

        var rejected = Assert.IsType<EvaluationConfigurationRejected>(outcome);
        Assert.Contains(
            EvaluationConfiguration.BaseUrlEnvironmentVariable,
            rejected.Error,
            StringComparison.Ordinal);
    }
}
