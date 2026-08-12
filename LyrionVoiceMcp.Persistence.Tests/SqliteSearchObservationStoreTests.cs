using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Persistence;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Persistence.Tests;

public sealed class SqliteSearchObservationStoreTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"lvm-observations-{Guid.NewGuid():N}");
    private SqliteSearchObservationStore store = null!;

    public async ValueTask InitializeAsync()
    {
        store = new SqliteSearchObservationStore(
            new SearchObservationSettings(Path.Combine(directory, "observations.db"), 90),
            TimeProvider.System);
        await store.InitialiseAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(directory))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task StoreShouldRoundTripSelectionReviewAndAnonymisedExport()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var observation = CreateObservation(now);
        await store.RecordAsync(observation, TestContext.Current.CancellationToken);

        // Act
        await store.MarkSelectedAsync(["correlation-1"], now.AddMinutes(1), TestContext.Current.CancellationToken);
        await store.SaveReviewAsync(
            observation.Id,
            new SearchObservationReview(
                SearchReviewClassification.WrongOrder,
                "correlation-1",
                null,
                null,
                null,
                null,
                "Useful private note",
                true,
                now.AddMinutes(2)),
            TestContext.Current.CancellationToken);
        var saved = await store.GetAsync(observation.Id, TestContext.Current.CancellationToken);
        var exported = await store.ExportAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(saved);
        Assert.NotNull(saved.Candidates[0].SelectedAt);
        Assert.Equal(SearchReviewClassification.WrongOrder, saved.Review?.Classification);
        Assert.Equal(LmsSearchRequestStatus.Completed, Assert.Single(saved.Requests).Status);
        var exportedCase = Assert.Single(exported);
        Assert.Equal("zyrack", exportedCase.Query);
        Assert.True(Assert.Single(exportedCase.OriginalCandidates).Expected);
        Assert.DoesNotContain("correlation", exportedCase.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Useful private note", exportedCase.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowseShouldFindZeroResultAndUnreviewedSearches()
    {
        // Arrange
        var observation = CreateObservation(DateTimeOffset.UtcNow) with { Candidates = [] };
        await store.RecordAsync(observation, TestContext.Current.CancellationToken);

        // Act
        var page = await store.BrowseAsync(
            new SearchObservationQuery(
                "seem",
                SearchObservationReviewFilter.Unreviewed,
                SearchObservationResultFilter.NoResults,
                0,
                10),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, page.Total);
        Assert.Equal(0, Assert.Single(page.Items).ResultCount);
    }

    [Fact]
    public async Task RecordingShouldApplyRetentionWithoutWaitingForRestart()
    {
        // Arrange
        var first = CreateObservation(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await store.RecordAsync(first, TestContext.Current.CancellationToken);
        var laterStore = new SqliteSearchObservationStore(
            new SearchObservationSettings(Path.Combine(directory, "observations.db"), 90),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-01T00:00:00Z")));

        // Act
        await laterStore.RecordAsync(
            CreateObservation(DateTimeOffset.Parse("2026-05-01T00:00:00Z")) with
            {
                Id = Guid.NewGuid().ToString("N"),
                Candidates = []
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(await laterStore.GetAsync(first.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExportShouldExcludeFailedSearchesEvenWhenMarkedForEvaluation()
    {
        // Arrange
        var failed = CreateObservation(DateTimeOffset.UtcNow) with
        {
            Status = SearchObservationStatus.Failed,
            FailureMessage = "Synthetic LMS failure."
        };
        await store.RecordAsync(failed, TestContext.Current.CancellationToken);
        await store.SaveReviewAsync(
            failed.Id,
            new SearchObservationReview(
                SearchReviewClassification.Other,
                null,
                null,
                null,
                null,
                null,
                null,
                true,
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        // Act
        var exported = await store.ExportAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(exported);
    }

    [Fact]
    public async Task FailedSearchShouldNotAppearAsACompletedZeroResultSearch()
    {
        // Arrange
        var failed = CreateObservation(DateTimeOffset.UtcNow) with
        {
            Status = SearchObservationStatus.Failed,
            FailureMessage = "Synthetic LMS failure.",
            Candidates = []
        };
        await store.RecordAsync(failed, TestContext.Current.CancellationToken);

        // Act
        var zeroResults = await store.BrowseAsync(
            new SearchObservationQuery(null, SearchObservationReviewFilter.All, SearchObservationResultFilter.NoResults, 0, 10),
            TestContext.Current.CancellationToken);
        var failures = await store.BrowseAsync(
            new SearchObservationQuery(null, SearchObservationReviewFilter.All, SearchObservationResultFilter.Failed, 0, 10),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(zeroResults.Items);
        Assert.Equal(failed.Id, Assert.Single(failures.Items).Id);
    }

    private static SearchObservation CreateObservation(DateTimeOffset now) => new(
        Guid.NewGuid().ToString("N"), now, "zyrack", "zyrack", null, "lms", "whole_library",
        "lms-pass-through", "1", SearchObservationStatus.Completed, null, 14, 12, 2,
        [new LmsSearchRequestObservation(
            "library", "[\"search\"]", LmsSearchRequestStatus.Completed, null, 12, 1)],
        [new SearchObservationCandidate(
            1, "correlation-1", new MediaIdentity(MediaEntityKind.Artist, "7"), "ZYRAQ", null, null, null)],
        null);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
