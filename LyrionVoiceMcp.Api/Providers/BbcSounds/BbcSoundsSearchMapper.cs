using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api.Providers.BbcSounds;

internal static class BbcSoundsSearchMapper
{
    public static IReadOnlyList<SearchSubscribedProgramme> Map(IReadOnlyList<SearchCandidateResult> candidates) =>
        candidates.Where(candidate => candidate.ProviderId == BbcSoundsProvider.Id
                && candidate.Kind == MediaEntityKind.Programme)
            .Select(candidate => new SearchSubscribedProgramme(candidate.Title, candidate.Reference)).ToArray();
}
