using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api.Tools;

internal static class SkippedItemMapper
{
    public static IReadOnlyList<SkippedItem> Map(
        IReadOnlyList<SkippedMediaItem> items) =>
        items.Select(item => new SkippedItem(
            item.Index,
            item.Reason.ToStableName(),
            item.Message)).ToArray();
}
