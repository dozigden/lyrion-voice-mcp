using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

internal sealed class PassthroughCatalogueSearchAvailabilityService
    : ICatalogueSearchAvailabilityService
{
    public static PassthroughCatalogueSearchAvailabilityService Instance { get; } = new();

    public Task<string> DescribeUnavailableAsync(
        string fallbackMessage,
        CancellationToken cancellationToken) => Task.FromResult(fallbackMessage);
}

internal sealed class FixedCatalogueSearchAvailabilityService(string message)
    : ICatalogueSearchAvailabilityService
{
    public Task<string> DescribeUnavailableAsync(
        string fallbackMessage,
        CancellationToken cancellationToken) => Task.FromResult(message);
}
