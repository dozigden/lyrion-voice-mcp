using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

internal static class ReferenceDisplayMetadataPolicy
{
    private const int MaximumFieldLength = 500;

    public static ReferenceDisplayMetadata? Bound(ReferenceDisplayMetadata? metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Title))
        {
            return null;
        }

        return metadata with
        {
            Title = Bound(metadata.Title),
            Artist = BoundOptional(metadata.Artist),
            Album = BoundOptional(metadata.Album)
        };
    }

    private static string Bound(string value)
    {
        if (value.Length <= MaximumFieldLength)
        {
            return value;
        }

        var length = MaximumFieldLength;
        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }
        return value[..length];
    }

    private static string? BoundOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value);
}

internal sealed class ReferenceDisplayMetadataResolver(
    ReferenceHandleRegistry registry) : IReferenceDisplayMetadataResolver
{
    public ReferenceDisplayMetadata? Resolve(string reference) =>
        registry.ResolveDisplayMetadata(reference);
}
