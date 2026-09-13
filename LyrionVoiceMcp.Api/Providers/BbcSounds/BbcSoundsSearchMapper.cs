using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api.Providers.BbcSounds;

internal static class BbcSoundsSearchMapper
{
    public static IReadOnlyList<SearchSubscribedProgramme> MapSubscriptions(IReadOnlyList<SearchCandidateResult> candidates) =>
        candidates.Where(candidate => candidate.ProviderId == BbcSoundsProvider.Id
                && candidate.Kind == MediaEntityKind.Programme)
            .Select(candidate => new SearchSubscribedProgramme(candidate.Title, candidate.Reference)).ToArray();

    public static IReadOnlyList<SearchBbcSoundsStation> MapStations(IReadOnlyList<SearchCandidateResult> candidates) =>
        candidates.Where(candidate => candidate.ProviderId == BbcSoundsProvider.Id
                && candidate.Kind == MediaEntityKind.Station)
            .Select(candidate => new SearchBbcSoundsStation(
                candidate.Title, candidate.Reference, candidate.Reference)).ToArray();
}
