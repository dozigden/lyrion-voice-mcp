using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

internal sealed class ReferenceCodecTestContext
{
    public ReferenceCodecTestContext()
    {
        var registry = new ReferenceHandleRegistry(TimeProvider.System);
        Search = new SearchResultReferenceCodec(registry);
        Browse = new BrowseReferenceCodec(registry);
        Resolver = new PlayableReferenceResolver(Search, Browse);
        DisplayMetadata = new ReferenceDisplayMetadataResolver(registry);
    }

    public SearchResultReferenceCodec Search { get; }

    public BrowseReferenceCodec Browse { get; }

    public PlayableReferenceResolver Resolver { get; }

    public IReferenceDisplayMetadataResolver DisplayMetadata { get; }
}
