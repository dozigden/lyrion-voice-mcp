using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class ReferenceHandleRegistryTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-16T17:00:00Z");

    [Fact]
    public void ResolveShouldRejectAnAlteredHandleWithoutGuessingItsValue()
    {
        // Arrange
        var registry = new ReferenceHandleRegistry(
            new MutableTimeProvider(Now),
            TimeSpan.FromHours(24),
            10);
        var codec = new SearchResultReferenceCodec(registry);
        var reference = codec.Encode(SearchValue("31"));
        var replacement = reference[^1] == '0' ? '1' : '0';
        var altered = reference[..^1] + replacement;

        // Act
        var resolved = codec.TryDecode(altered);

        // Assert
        Assert.Null(resolved);
        Assert.NotNull(codec.TryDecode(reference));
    }

    [Fact]
    public void ResolveShouldUseAnAbsoluteLifetimeRatherThanSlidingExpiry()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(Now);
        var registry = new ReferenceHandleRegistry(
            timeProvider,
            TimeSpan.FromHours(24),
            10);
        var codec = new SearchResultReferenceCodec(registry);
        var reference = codec.Encode(SearchValue("32"));
        timeProvider.Advance(TimeSpan.FromHours(23));

        // Act
        var beforeExpiry = codec.TryDecode(reference);
        timeProvider.Advance(TimeSpan.FromHours(1));
        var atExpiry = codec.TryDecode(reference);

        // Assert
        Assert.NotNull(beforeExpiry);
        Assert.Null(atExpiry);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void CapacityShouldBeSharedAndEvictTheOldestIssuedHandle()
    {
        // Arrange
        var registry = new ReferenceHandleRegistry(
            new MutableTimeProvider(Now),
            TimeSpan.FromHours(24),
            2);
        var searchCodec = new SearchResultReferenceCodec(registry);
        var browseCodec = new BrowseReferenceCodec(registry);
        var oldest = searchCodec.Encode(SearchValue("33"));
        var second = browseCodec.Encode(BrowseValue("34"));

        // Act
        var newest = browseCodec.Encode(BrowseValue("35"));

        // Assert
        Assert.Null(searchCodec.TryDecode(oldest));
        Assert.NotNull(browseCodec.TryDecode(second));
        Assert.NotNull(browseCodec.TryDecode(newest));
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void ANewRegistryShouldNotResolveAHandleIssuedBeforeRestart()
    {
        // Arrange
        var firstRegistry = new ReferenceHandleRegistry(
            new MutableTimeProvider(Now),
            TimeSpan.FromHours(24),
            10);
        var reference = new SearchResultReferenceCodec(firstRegistry)
            .Encode(SearchValue("36"));
        var restartedCodec = new SearchResultReferenceCodec(
            new ReferenceHandleRegistry(
                new MutableTimeProvider(Now),
                TimeSpan.FromHours(24),
                10));

        // Act
        var resolved = restartedCodec.TryDecode(reference);

        // Assert
        Assert.Null(resolved);
    }

    [Fact]
    public void PrefixAndValueTypeShouldPreventCrossCodecResolution()
    {
        // Arrange
        var registry = new ReferenceHandleRegistry(
            new MutableTimeProvider(Now),
            TimeSpan.FromHours(24),
            10);
        var searchCodec = new SearchResultReferenceCodec(registry);
        var browseCodec = new BrowseReferenceCodec(registry);
        var searchReference = searchCodec.Encode(SearchValue("37"));
        var browseReference = browseCodec.Encode(BrowseValue("38"));

        // Act
        var searchAsBrowse = browseCodec.TryDecode(searchReference);
        var browseAsSearch = searchCodec.TryDecode(browseReference);

        // Assert
        Assert.Null(searchAsBrowse);
        Assert.Null(browseAsSearch);
    }

    [Fact]
    public void ServiceRegistrationShouldShareHandlesAcrossIndependentScopes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLyrionVoiceMcpServices();
        using var provider = services.BuildServiceProvider();
        string searchReference;
        string browseReference;
        using (var issuingScope = provider.CreateScope())
        {
            searchReference = issuingScope.ServiceProvider
                .GetRequiredService<ISearchResultReferenceCodec>()
                .Encode(SearchValue("39"));
            browseReference = issuingScope.ServiceProvider
                .GetRequiredService<IBrowseReferenceCodec>()
                .Encode(BrowseValue("40"));
        }

        // Act
        PlayableReferenceValue? resolvedSearch;
        PlayableReferenceValue? resolvedBrowse;
        using (var consumingScope = provider.CreateScope())
        {
            var resolver = consumingScope.ServiceProvider
                .GetRequiredService<IPlayableReferenceResolver>();
            resolvedSearch = resolver.Resolve(searchReference);
            resolvedBrowse = resolver.Resolve(browseReference);
        }

        // Assert
        Assert.Equal("39", resolvedSearch?.Media.Identity.Id);
        Assert.Equal("40", resolvedBrowse?.Media.Identity.Id);
    }

    private static SearchResultReferenceValue SearchValue(string id) => new(
        "123456781234123412341234567890ab",
        new MediaIdentity(MediaEntityKind.Track, id));

    private static BrowseReferenceValue BrowseValue(string id) => new(
        new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, id, 0),
        new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, id)),
        "123456781234123412341234567890ab");

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
