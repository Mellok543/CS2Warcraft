namespace Warcraft.Api.Players;

public sealed record PlayerStateSnapshot(
    ulong SteamId,
    string Name,
    long GlobalXp,
    long AchievementCurrency,
    string? ActiveRaceId,
    IReadOnlyDictionary<string, RaceProgressSnapshot> Races,
    IReadOnlyDictionary<string, DateTimeOffset> Cooldowns,
    PlayerStatsSnapshot Stats,
    IReadOnlyCollection<string> OwnedCosmetics,
    IReadOnlyDictionary<string, string> EquippedCosmetics);

/// <summary>Lifetime statistics maintained by Core. PlaySeconds includes the current session.</summary>
public sealed record PlayerStatsSnapshot(
    long Kills,
    long Deaths,
    long Headshots,
    long RoundsPlayed,
    long RoundsWon,
    long PlaySeconds);

public sealed record RaceProgressSnapshot(
    string RaceId,
    int Level,
    long Xp,
    int SkillPoints,
    IReadOnlyDictionary<string, int> AbilityLevels);
