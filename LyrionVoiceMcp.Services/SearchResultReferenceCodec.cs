using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class SearchResultReferenceCodec : ISearchResultReferenceCodec
{
    private const string Prefix = "result_";
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

        return registry.Issue(Prefix, value);
    }

    public SearchResultReferenceValue? TryDecode(string reference)
    {
        return registry.Resolve<SearchResultReferenceValue>(Prefix, reference);
    }
}
