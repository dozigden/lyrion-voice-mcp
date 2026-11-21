using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.Qobuz;
using LyrionVoiceMcp.Services.Providers.Qobuz;

namespace LyrionVoiceMcp.Services.Tests.Providers;

public sealed class QobuzBrowseSourceTests
{
    [Theory]
    [InlineData(QobuzConfigurationStatus.NotInstalled)]
    [InlineData(QobuzConfigurationStatus.NotConfigured)]
    [InlineData(null)]
    public async Task UnavailableStreamingShouldContributeNoRoots(QobuzConfigurationStatus? status)
    {
        var source = new QobuzBrowseSource(new Client(status), new ReferenceCodecTestContext().Browse);
        Assert.Empty(await source.GetRootsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AlbumContinuationPreservesOrderAndIssuesPlayableTrackReferences()
    {
        var refs = new ReferenceCodecTestContext();
        var source = new QobuzBrowseSource(new Client(QobuzConfigurationStatus.Configured), refs.Browse);
        var token = TestContext.Current.CancellationToken;
        async Task<BrowseSucceeded> Open(string handle) => Assert.IsType<BrowseSucceeded>(
            await source.BrowseAsync(refs.Browse.TryDecode(handle)!.ProviderTarget!, null, token));

        var root = Assert.Single(await source.GetRootsAsync(token));
        var releases = Assert.Single((await Open(root.Reference)).Items);
        var firstPage = await Open(releases.Reference);
        var lastAlbum = Assert.Single((await Open(firstPage.Continuation!)).Items);
        var track = Assert.Single((await Open(lastAlbum.Reference)).Items);

        Assert.Equal(50, firstPage.Items.Count);
        Assert.Equal("Moon 51", lastAlbum.Title);
        Assert.Equal("Paper Ensemble", lastAlbum.Artist);
        Assert.False(lastAlbum.HasPlayReference);
        Assert.True(track.HasPlayReference);
        Assert.False(track.HasBrowseReference);
        var resolved = Assert.IsType<QobuzTrackTarget>(refs.Resolver.Resolve(track.Reference)!.Media.ProviderTarget);
        Assert.Equal("501", resolved.TrackId);
        Assert.Equal(lastAlbum.Title, resolved.Album.Title);
    }

    private sealed class Client(QobuzConfigurationStatus? status) : IQobuzClient
    {
        public Task<QobuzConfigurationStatus> GetConfigurationStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(status ?? throw new LmsRequestException("Offline."));

        public Task<IReadOnlyList<QobuzAlbum>> ReadReleasesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QobuzAlbum>>(Enumerable.Range(1, 51)
                .Select(i => new QobuzAlbum($"Moon {i}\nPaper Ensemble (2001)", null)).ToArray());

        public Task<IReadOnlyList<QobuzTrack>> ReadTracksAsync(QobuzAlbum album, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QobuzTrack>>([new("501", "qobuz://501.flac", "Wax", "Paper Ensemble")]);

        public Task SubmitAsync(string playerId, QobuzTrackTarget track, ProviderPlaybackCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
