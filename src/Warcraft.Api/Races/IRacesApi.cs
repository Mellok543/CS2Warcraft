namespace Warcraft.Api.Races;

public interface IRacesApi
{
    IReadOnlyCollection<RaceDefinition> GetAll();
    RaceDefinition? Get(string raceId);
    RaceCatalogReplaceResult ReplaceCatalog(IReadOnlyCollection<RaceDefinition> races, string source);

    /// <param name="force">Skips VIP and unlock requirements (administrative use).</param>
    RaceSelectionResult SelectRace(ulong steamId, string raceId, string reason, bool force = false);

    /// <summary>Whether the player may select the race and how far they are from each requirement.</summary>
    RaceAvailability GetAvailability(ulong steamId, string raceId);
}

public sealed record RaceAvailability(
    bool IsAvailable,
    bool VipLocked,
    bool AlreadyUnlocked,
    IReadOnlyList<RaceRequirementProgress> Requirements);

public sealed record RaceRequirementProgress(
    string Description,
    long Current,
    long Required)
{
    public bool IsMet => Current >= Required;
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
