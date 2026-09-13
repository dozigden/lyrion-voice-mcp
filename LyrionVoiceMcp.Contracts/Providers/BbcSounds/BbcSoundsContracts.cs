namespace LyrionVoiceMcp.Contracts;

public sealed record SearchSubscribedProgramme(string Title, string BrowseRef);

public sealed record SearchBbcSoundsStation(string Name, string BrowseRef, string PlayRef);
