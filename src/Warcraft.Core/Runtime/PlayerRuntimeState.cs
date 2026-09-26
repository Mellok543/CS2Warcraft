using Warcraft.Api.Persistence;
using Warcraft.Api.Players;

namespace Warcraft.Core.Runtime;

internal sealed class PlayerRuntimeState
{
    public required ulong SteamId { get; init; }
    public required string Name { get; set; }
    public long GlobalXp { get; set; }
    public string? ActiveRaceId { get; set; }

    public Dictionary<string, RaceProgressRuntime> Races { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, DateTimeOffset> Cooldowns { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public PlayerStateSnapshot ToSnapshot()
        => new(
            SteamId,
            Name,
            GlobalXp,
            ActiveRaceId,
            Races.ToDictionary(x => x.Key, x => x.Value.ToSnapshot(), StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, DateTimeOffset>(Cooldowns, StringComparer.OrdinalIgnoreCase));

    public PlayerPersistenceDto ToPersistence()
        => new()
        {
            SteamId = SteamId,
            Name = Name,
            GlobalXp = GlobalXp,
            ActiveRaceId = ActiveRaceId,
            Races = Races.Values.Select(x => x.ToPersistence()).ToArray()
        };
}

internal sealed class RaceProgressRuntime
{
    public required string RaceId { get; init; }
    public int Level { get; set; } = 1;
    public long Xp { get; set; }
    public int SkillPoints { get; set; }

    public Dictionary<string, int> AbilityLevels { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public RaceProgressSnapshot ToSnapshot()
        => new(
            RaceId,
            Level,
            Xp,
            SkillPoints,
            new Dictionary<string, int>(AbilityLevels, StringComparer.OrdinalIgnoreCase));

    public RaceProgressPersistenceDto ToPersistence()
        => new()
        {
            RaceId = RaceId,
            Level = Level,
            Xp = Xp,
            SkillPoints = SkillPoints,
            AbilityLevels = new Dictionary<string, int>(AbilityLevels, StringComparer.OrdinalIgnoreCase)
        };
}
