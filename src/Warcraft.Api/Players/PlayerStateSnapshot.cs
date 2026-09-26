namespace Warcraft.Api.Players;

public sealed record PlayerStateSnapshot(
    ulong SteamId,
    string Name,
    long GlobalXp,
    string? ActiveRaceId,
    IReadOnlyDictionary<string, RaceProgressSnapshot> Races,
    IReadOnlyDictionary<string, DateTimeOffset> Cooldowns);

public sealed record RaceProgressSnapshot(
    string RaceId,
    int Level,
    long Xp,
    int SkillPoints,
    IReadOnlyDictionary<string, int> AbilityLevels);
