using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef;
using LyrionVoiceMcp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace LyrionVoiceMcp.Persistence.Tests;

public sealed class EfSearchObservationStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-18T08:00:00Z");

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lvm-ef-observations-{Guid.NewGuid():N}");
    private ServiceProvider serviceProvider = null!;
    private ISearchObservationStore store = null!;

    public async ValueTask InitializeAsync()
    {
        serviceProvider = CreateServiceProvider(
            Now,
            90);
        await serviceProvider.InitialiseLyrionVoiceMcpEfAsync(
            TestContext.Current.CancellationToken);
        store = serviceProvider.GetRequiredService<ISearchObservationStore>();
        await store.InitialiseAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (serviceProvider is not null)
        {
            await serviceProvider.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task StoreShouldRoundTripBrowseSelectionReviewAndPrivacySafeExport()
    {
        var observation = CreateObservation(Now.AddMinutes(-5));
        await store.RecordAsync(observation, TestContext.Current.CancellationToken);

        await store.MarkSelectedAsync(
            ["correlation-2", "correlation-2"],
            Now,
            TestContext.Current.CancellationToken);
        Assert.True(await store.SaveReviewAsync(
            observation.Id,
            new SearchObservationReview(
                SearchReviewClassification.WrongOrder,
                "correlation-1",
                MediaEntityKind.Album,
                "Crazymad, for Me",
                "ZYRAQ",
                null,
                "Private review note",
                true,
                Now.AddMinutes(1)),
            TestContext.Current.CancellationToken));

        var saved = await store.GetAsync(
            observation.Id,
            TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.True(saved.Candidates[0].IsExactArtistMatch);
        Assert.Equal("roman_cardinal_equivalent", saved.Candidates[0].MatchSignal);
        var selected = Assert.Single(saved.Candidates, item => item.Position == 2);
        Assert.Equal(Now, selected.SelectedAt);
        Assert.False(selected.IsExactArtistMatch);
        Assert.Equal(LmsSearchRequestStatus.Failed, saved.Requests[1].Status);
        Assert.Equal("Playlist provider unavailable.", saved.Requests[1].FailureMessage);
        Assert.Equal(SearchReviewClassification.WrongOrder, saved.Review?.Classification);
        Assert.Equal(SearchObservationInterpretation.Named, saved.Interpretation);

        var page = await store.BrowseAsync(
            new SearchObservationQuery(
                "see%mat_",
                SearchObservationReviewFilter.Reviewed,
                SearchObservationResultFilter.Selected,
                0,
                10),
            TestContext.Current.CancellationToken);
        Assert.Empty(page.Items);

        page = await store.BrowseAsync(
            new SearchObservationQuery(
                "zyrack",
                SearchObservationReviewFilter.Reviewed,
                SearchObservationResultFilter.Selected,
                0,
                10),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, page.Total);
        Assert.Equal(2, Assert.Single(page.Items).SelectedPosition);
        Assert.Equal(SearchObservationInterpretation.Named, Assert.Single(page.Items).Interpretation);

        var exportedCase = Assert.Single(await store.ExportAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal("zyrack", exportedCase.Query);
        Assert.True(exportedCase.OriginalCandidates[0].Expected);
        Assert.True(exportedCase.OriginalCandidates[1].Selected);
        var exportedJson = JsonSerializer.Serialize(exportedCase);
        Assert.DoesNotContain(
            "correlation",
            exportedJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private review note", exportedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowseShouldDistinguishCompletedNoResultsFromFailures()
    {
        var noResults = CreateObservation(Now.AddMinutes(-2)) with
        {
            Id = "no-results",
            Candidates = []
        };
        var failed = CreateObservation(Now.AddMinutes(-1)) with
        {
            Id = "failed",
            Status = SearchObservationStatus.Failed,
            FailureMessage = "Synthetic failure.",
            Candidates = []
        };
        await store.RecordAsync(noResults, TestContext.Current.CancellationToken);
        await store.RecordAsync(failed, TestContext.Current.CancellationToken);

        var zeroResultPage = await store.BrowseAsync(
            new SearchObservationQuery(
                null,
                SearchObservationReviewFilter.Unreviewed,
                SearchObservationResultFilter.NoResults,
                0,
                10),
            TestContext.Current.CancellationToken);
        var failurePage = await store.BrowseAsync(
            new SearchObservationQuery(
                null,
                SearchObservationReviewFilter.All,
                SearchObservationResultFilter.Failed,
                0,
                10),
            TestContext.Current.CancellationToken);

        Assert.Equal("no-results", Assert.Single(zeroResultPage.Items).Id);
        Assert.Equal("failed", Assert.Single(failurePage.Items).Id);
    }

    [Fact]
    public async Task StoreShouldRoundTripRatingConstraintsAndCandidateRatings()
    {
        var constraint = new RatingSearchConstraint(4.5m, RatingMatchMode.AtLeast);
        var observation = CreateObservation(Now) with
        {
            Id = "rated-search",
            RatingConstraint = constraint,
            Genre = "Pop",
            RequestedFromYear = 99,
            RequestedToYear = 0,
            EffectiveFromYear = 1999,
            EffectiveToYear = 2000,
            Candidates =
            [
                new SearchObservationCandidate(
                    1,
                    "rated-correlation",
                    new MediaIdentity(MediaEntityKind.Track, "rated-track"),
                    "Rated Signal",
                    "The Imaginaries",
                    "Imaginary Signals",
                    null,
                    4.55m)
            ]
        };

        await store.RecordAsync(observation, TestContext.Current.CancellationToken);
        Assert.True(await store.SaveReviewAsync(
            observation.Id,
            new SearchObservationReview(
                SearchReviewClassification.Good,
                "rated-correlation",
                MediaEntityKind.Track,
                "Rated Signal",
                "The Imaginaries",
                "Imaginary Signals",
                null,
                true,
                Now.AddMinutes(1)),
            TestContext.Current.CancellationToken));

        var saved = await store.GetAsync(
            observation.Id,
            TestContext.Current.CancellationToken);
        var exported = Assert.Single(await store.ExportAsync(
            TestContext.Current.CancellationToken),
            item => item.Query == observation.NormalisedQuery
                && item.RatingConstraint is not null);
        Assert.Equal(constraint, saved?.RatingConstraint);
        Assert.Equal("Pop", saved?.Genre);
        Assert.Equal(99, saved?.RequestedFromYear);
        Assert.Equal(0, saved?.RequestedToYear);
        Assert.Equal(1999, saved?.EffectiveFromYear);
        Assert.Equal(2000, saved?.EffectiveToYear);
        Assert.Equal(4.55m, Assert.Single(saved!.Candidates).Rating);
        Assert.Equal(constraint, exported.RatingConstraint);
        Assert.Equal("Pop", exported.Genre);
        Assert.Equal(1999, exported.EffectiveFromYear);
        Assert.Equal(2000, exported.EffectiveToYear);
        Assert.Equal(4.55m, Assert.Single(exported.OriginalCandidates).Rating);
    }

    [Fact]
    public async Task RecordingShouldDeleteExpiredRowsFromApplicationDatabase()
    {
        var expired = CreateObservation(Now.AddDays(-91)) with { Id = "expired" };
        await store.RecordAsync(expired, TestContext.Current.CancellationToken);
        var current = CreateObservation(Now) with { Id = "current" };
        await store.RecordAsync(current, TestContext.Current.CancellationToken);

        Assert.Null(await store.GetAsync(expired.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await store.GetAsync(current.Id, TestContext.Current.CancellationToken));
    }

    private ServiceProvider CreateServiceProvider(
        DateTimeOffset now,
        int retentionDays)
    {
        var services = new ServiceCollection();
        services.AddLyrionVoiceMcpEf(new ApplicationDatabaseSettings(
            Path.Combine(directory, "application.db")));
        services.AddSingleton(new SearchObservationRetentionPolicy(retentionDays));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddTransient<ISearchObservationStore, EfSearchObservationStore>();
        return services.BuildServiceProvider();
    }

    private static SearchObservation CreateObservation(DateTimeOffset createdAt) => new(
        Guid.NewGuid().ToString("N"),
        createdAt,
        "zyrack",
        "zyrack",
        MediaEntityKind.Artist,
        "lms",
        "whole_library",
        "catalogue-index",
        "2",
        SearchObservationStatus.Completed,
        null,
        24,
        18,
        6,
        [
            new LmsSearchRequestObservation(
                "catalogue-index",
                "search",
                LmsSearchRequestStatus.Completed,
                null,
                12,
                2),
            new LmsSearchRequestObservation(
                "playlists",
                "search",
                LmsSearchRequestStatus.Failed,
                "Playlist provider unavailable.",
                6,
                0)
        ],
        [
            new SearchObservationCandidate(
                1,
                "correlation-1",
                new MediaIdentity(MediaEntityKind.Artist, "7"),
                "ZYRAQ",
                null,
                null,
                null,
                IsExactArtistMatch: true,
                MatchSignal: "roman_cardinal_equivalent"),
            new SearchObservationCandidate(
                2,
                "correlation-2",
                new MediaIdentity(MediaEntityKind.Album, "8"),
                "Crazymad, for Me",
                "ZYRAQ",
                null,
                null)
        ],
        null,
        Interpretation: SearchObservationInterpretation.Named);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
