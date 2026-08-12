using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class SearchObservationReviewServiceTests
{
    [Fact]
    public async Task FailedSearchShouldRejectEvaluationInclusion()
    {
        // Arrange
        var store = new RecordingSearchObservationStore();
        await store.RecordAsync(
            new SearchObservation(
                "observation", DateTimeOffset.UtcNow, "zyrack", "zyrack", null,
                "lms", "whole_library", "lms-pass-through", "1",
                SearchObservationStatus.Failed, "Synthetic failure.", 10, 10, 0, [], [], null),
            TestContext.Current.CancellationToken);
        var service = new SearchObservationReviewService(store);

        // Act
        var outcome = await service.SaveReviewAsync(
            "observation",
            new SearchObservationReview(
                SearchReviewClassification.Other, null, null, null, null, null, null, true,
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<SaveSearchReviewRejected>(outcome);
        Assert.Equal(SaveSearchReviewRejectionReason.InvalidReview, rejection.Reason);
        Assert.Contains("Failed searches", rejection.Message, StringComparison.Ordinal);
    }
}
