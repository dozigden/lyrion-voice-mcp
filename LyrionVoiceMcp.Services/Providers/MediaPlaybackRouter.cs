using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;

namespace LyrionVoiceMcp.Services.Providers;

internal sealed class MediaPlaybackRouter(
    ILmsPlaybackClient local,
    IEnumerable<IProviderPlaybackSource> sources) : ILmsPlaybackClient
{
    private readonly IReadOnlyDictionary<string, IProviderPlaybackSource> providers =
        sources.ToDictionary(source => source.ProviderId, StringComparer.Ordinal);

    private IProviderPlaybackSource Resolve(ProviderMediaTarget target) =>
        providers.TryGetValue(target.ProviderId, out var source)
            ? source : throw new LmsRequestException("The media provider is unavailable.");

    public async Task<int> GetPlayableItemCountAsync(PlayableMedia media, CancellationToken cancellationToken)
    {
        if (media.ProviderTarget is not { } target)
            return await local.GetPlayableItemCountAsync(media, cancellationToken);
        try
        {
            return await Resolve(target).GetPlayableItemCountAsync(target, cancellationToken);
        }
        catch (LmsRequestException)
        {
            // Optional-source availability failure rejects this item, not the
            // other items already inspected by the shared batch policy.
            return 0;
        }
    }

    public Task LoadAsync(string playerId, PlayableMedia media, CancellationToken cancellationToken) =>
        media.ProviderTarget is { } target
            ? Resolve(target).SubmitAsync(playerId, target, ProviderPlaybackCommand.Load, cancellationToken)
            : local.LoadAsync(playerId, media, cancellationToken);

    public Task AddAsync(string playerId, PlayableMedia media, CancellationToken cancellationToken) =>
        media.ProviderTarget is { } target
            ? Resolve(target).SubmitAsync(playerId, target, ProviderPlaybackCommand.Add, cancellationToken)
            : local.AddAsync(playerId, media, cancellationToken);

    public Task InsertAsync(string playerId, PlayableMedia media, CancellationToken cancellationToken) =>
        media.ProviderTarget is { } target
            ? Resolve(target).SubmitAsync(playerId, target, ProviderPlaybackCommand.Insert, cancellationToken)
            : local.InsertAsync(playerId, media, cancellationToken);

    public Task PowerOnAsync(string playerId, CancellationToken cancellationToken) => local.PowerOnAsync(playerId, cancellationToken);
    public Task<int> GetQueueCountAsync(string playerId, CancellationToken cancellationToken) => local.GetQueueCountAsync(playerId, cancellationToken);
    public Task ClearAsync(string playerId, CancellationToken cancellationToken) => local.ClearAsync(playerId, cancellationToken);
}
