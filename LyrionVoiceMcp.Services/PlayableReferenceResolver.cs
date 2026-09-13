using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class PlayableReferenceResolver(
    ISearchResultReferenceCodec searchReferenceCodec,
    IBrowseReferenceCodec browseReferenceCodec) : IPlayableReferenceResolver
{
    public PlayableReferenceValue? Resolve(string reference)
    {
        var searchReference = searchReferenceCodec.TryDecode(reference);
        if (searchReference is not null
            && searchReference.Identity.Kind is not (MediaEntityKind.Artist or MediaEntityKind.Programme)
            && (searchReference.Identity.ProviderId is null || searchReference.ProviderMediaTarget is not null))
        {
            return new PlayableReferenceValue(
                new PlayableMedia(searchReference.Identity, searchReference.ProviderMediaTarget),
                searchReference.CorrelationId);
        }

        var browseReference = browseReferenceCodec.TryDecode(reference);
        return browseReference?.Media is { Identity.Kind: not (MediaEntityKind.Artist or MediaEntityKind.Programme) } media
            ? new PlayableReferenceValue(
                media,
                browseReference.SearchCorrelationId)
            : null;
    }
}
