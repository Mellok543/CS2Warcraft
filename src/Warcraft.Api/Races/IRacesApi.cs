namespace Warcraft.Api.Races;

public interface IRacesApi
{
    IReadOnlyCollection<RaceDefinition> GetAll();
    RaceDefinition? Get(string raceId);
    RaceCatalogReplaceResult ReplaceCatalog(IReadOnlyCollection<RaceDefinition> races, string source);
    RaceSelectionResult SelectRace(ulong steamId, string raceId, string reason);
}

public sealed record RaceCatalogReplaceResult(
    bool Success,
    int RaceCount,
    IReadOnlyList<string> Errors);

public sealed record RaceSelectionResult(
    bool Success,
    string Message,
    string? PreviousRaceId,
    string? CurrentRaceId);
