using System.Net;
using System.Text;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsConnectionProbeTests
{
    [Fact]
    public async Task CheckShouldReportNotConfiguredWithoutSendingARequest()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("No request was expected."));
        using var client = new HttpClient(handler);
        var probe = new LmsConnectionProbe(
            LmsConnectionSettings.FromValues(null, null, null),
            client);

        // Act
        var result = await probe.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(LmsConnectionState.NotConfigured, result.State);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CheckShouldSendServerStatusAndReportVersion()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("http://music.test:9000/jsonrpc.js", request.RequestUri?.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("\"slim.request\"", body, StringComparison.Ordinal);
            Assert.Contains("\"serverstatus\"", body, StringComparison.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":1,"result":{"version":"9.0.1"}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var client = new HttpClient(handler);
        var probe = new LmsConnectionProbe(
            LmsConnectionSettings.FromValues(
                "development",
                "http://music.test:9000",
                null),
            client);

        // Act
        var result = await probe.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(LmsConnectionState.Online, result.State);
        Assert.Equal("development", result.ServerId);
        Assert.Equal("http://music.test:9000", result.BaseUrl);
        Assert.Equal("9.0.1", result.ServerVersion);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CheckShouldReportAnInvalidResponseAsUnavailable()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        using var client = new HttpClient(handler);
        var probe = new LmsConnectionProbe(
            LmsConnectionSettings.FromValues(
                "development",
                "http://music.test:9000",
                null),
            client);

        // Act
        var result = await probe.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(LmsConnectionState.Unavailable, result.State);
        Assert.Equal("LMS serverstatus response did not include a result object.", result.Message);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return Task.FromResult(respond(request));
        }
    }
}
