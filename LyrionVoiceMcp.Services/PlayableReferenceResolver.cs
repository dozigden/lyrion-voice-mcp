using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class PlayableReferenceResolver(
    ISearchResultReferenceCodec searchReferenceCodec,
    IBrowseReferenceCodec browseReferenceCodec) : IPlayableReferenceResolver
{
    public PlayableReferenceValue? Resolve(string reference)
    {
        var searchReference = searchReferenceCodec.TryDecode(reference);
        if (searchReference is not null)
        {
            return new PlayableReferenceValue(
                new PlayableMedia(searchReference.Identity),
                searchReference.CorrelationId);
        }

        var browseReference = browseReferenceCodec.TryDecode(reference);
        return browseReference?.Media is { } media
            ? new PlayableReferenceValue(
                media,
                browseReference.SearchCorrelationId)
            : null;
    }
}
