using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests.Providers;

public sealed class ProviderRoutingTests
{
    [Fact]
    public async Task RouterShouldDispatchByProviderWithoutChangingPlayerState()
    {
        var local = new LocalPlayback();
        var provider = new FictionalPlayback();
        var router = new MediaPlaybackRouter(local, [provider]);
        var remote = new PlayableMedia(new MediaIdentity(MediaEntityKind.Episode, "episode", "fiction"), new FictionalTarget());

        Assert.Equal(1, await router.GetPlayableItemCountAsync(remote, TestContext.Current.CancellationToken));
        await router.AddAsync("room", remote, TestContext.Current.CancellationToken);
        await router.InsertAsync("room", new PlayableMedia(new MediaIdentity(MediaEntityKind.Track, "local")), TestContext.Current.CancellationToken);

        Assert.Equal(new[] { ProviderPlaybackCommand.Add }, provider.Commands);
        Assert.Equal(new[] { "insert" }, local.Commands);
    }

    [Fact]
    public void ProviderSearchFailureShouldReturnSourceEvidenceWithoutThrowing()
    {
        var result = ProviderSearchWork.Read(new FailingSearch(), new SearchCriteria("fiction"),
            NullLogger.Instance, TestContext.Current.CancellationToken);
        Assert.Empty(result.Candidates);
        Assert.Equal(LmsSearchRequestStatus.Failed, result.Observation.Status);
        Assert.Equal("fiction", result.Observation.Source);
    }

    [Fact]
    public void ProgrammeReferencesShouldBeNavigationOnlyAndKeepProviderTargetsOpaque()
    {
        var references = new ReferenceCodecTestContext();
        var target = new FictionalBrowseTarget();
        var reference = references.Search.Encode(new SearchResultReferenceValue(Guid.NewGuid().ToString("N"),
            new MediaIdentity(MediaEntityKind.Programme, "programme", "fiction"), ProviderTarget: target));
        Assert.Same(target, references.Search.TryDecode(reference)!.ProviderTarget);
        Assert.Null(references.Resolver.Resolve(reference));
        Assert.DoesNotContain("fiction", reference, StringComparison.Ordinal);
    }

    private sealed record FictionalTarget() : ProviderMediaTarget("fiction");
    private sealed record FictionalBrowseTarget() : ProviderBrowseTarget("fiction");
    private sealed class FailingSearch : IProviderSearchSource
    {
        public string ProviderId => "fiction";
        public ProviderSearchResult Search(SearchCriteria criteria, CancellationToken token) => throw new InvalidOperationException("Fictional failure");
    }
    private sealed class FictionalPlayback : IProviderPlaybackSource
    {
        public string ProviderId => "fiction";
        public List<ProviderPlaybackCommand> Commands { get; } = [];
        public Task<int> GetPlayableItemCountAsync(ProviderMediaTarget target, CancellationToken token) => Task.FromResult(1);
        public Task SubmitAsync(string player, ProviderMediaTarget target, ProviderPlaybackCommand command, CancellationToken token)
        { Commands.Add(command); return Task.CompletedTask; }
    }
    private sealed class LocalPlayback : ILmsPlaybackClient
    {
        public List<string> Commands { get; } = [];
        public Task<int> GetPlayableItemCountAsync(PlayableMedia media, CancellationToken token) => Task.FromResult(1);
        public Task<int> GetQueueCountAsync(string player, CancellationToken token) => Task.FromResult(0);
        public Task PowerOnAsync(string player, CancellationToken token) => throw new InvalidOperationException("Unexpected power change");
        public Task LoadAsync(string player, PlayableMedia media, CancellationToken token) { Commands.Add("load"); return Task.CompletedTask; }
        public Task AddAsync(string player, PlayableMedia media, CancellationToken token) { Commands.Add("add"); return Task.CompletedTask; }
        public Task InsertAsync(string player, PlayableMedia media, CancellationToken token) { Commands.Add("insert"); return Task.CompletedTask; }
        public Task ClearAsync(string player, CancellationToken token) => throw new InvalidOperationException("Unexpected clear");
    }
}
