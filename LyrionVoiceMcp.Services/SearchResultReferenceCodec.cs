using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class SearchResultReferenceCodec : ISearchResultReferenceCodec
{
    private readonly ReferenceHandleRegistry registry;

    internal SearchResultReferenceCodec(ReferenceHandleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this.registry = registry;
    }

    public string Encode(SearchResultReferenceValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!Guid.TryParseExact(value.CorrelationId, "N", out _))
        {
            throw new ArgumentException(
                "The search-result correlation ID must be a compact GUID.",
                nameof(value));
        }

        if (!Enum.IsDefined(value.Identity.Kind)
            || string.IsNullOrWhiteSpace(value.Identity.Id))
        {
            throw new ArgumentException(
                "The search-result media identity is invalid.",
                nameof(value));
        }

        var providerId = value.Identity.ProviderId;
        if ((value.ProviderTarget is not null && providerId != value.ProviderTarget.ProviderId)
            || (value.ProviderMediaTarget is not null && providerId != value.ProviderMediaTarget.ProviderId)
            || (providerId is null && (value.ProviderTarget is not null || value.ProviderMediaTarget is not null))
            || (providerId is not null && value.ProviderTarget is null && value.ProviderMediaTarget is null))
        {
            throw new ArgumentException("The search provider targets do not match their identity.", nameof(value));
        }

        return registry.Issue(
            ReferencePrefixes.ForMedia(value.Identity.Kind),
            value,
            value.DisplayMetadata);
    }

    public SearchResultReferenceValue? TryDecode(string reference)
    {
        return registry.Resolve<SearchResultReferenceValue>(reference);
    }
}
