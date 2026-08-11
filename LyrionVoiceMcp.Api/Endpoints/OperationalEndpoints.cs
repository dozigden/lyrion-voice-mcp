using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Api.Configuration;
using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api.Endpoints;

public static class OperationalEndpoints
{
    public static IEndpointRouteBuilder MapOperationalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health", (IOperationalStatusService service) =>
        {
            var status = service.GetStatus();
            return Results.Ok(new HealthResponse(status.Status));
        });

        endpoints.MapGet("/api/version", (LyrionVoiceMcpBuildInfo buildInfo) =>
            Results.Ok(new VersionResponse(
                buildInfo.Version,
                buildInfo.Channel,
                buildInfo.Build,
                buildInfo.Commit)));

        endpoints.MapGet("/api/lms", async (
            ILmsConnectionStatusService service,
            CancellationToken cancellationToken) =>
        {
            var status = await service.GetStatusAsync(cancellationToken);
            return Results.Ok(new LmsConnectionResponse(
                ToContractStatus(status.State),
                status.ServerId,
                status.BaseUrl,
                status.ServerVersion,
                status.Message));
        });

        return endpoints;
    }

    private static string ToContractStatus(LmsConnectionState state) => state switch
    {
        LmsConnectionState.NotConfigured => "not_configured",
        LmsConnectionState.Online => "online",
        LmsConnectionState.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown LMS connection state.")
    };
}
