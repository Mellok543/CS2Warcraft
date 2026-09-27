using Warcraft.Api.Achievements;
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

    public PlayerStatsRuntime Stats { get; init; } = new();

    /// <summary>
    /// True once stored progress was restored (or the storage confirmed a new
    /// player). Until then the state is a placeholder and must never be saved,
    /// otherwise it would overwrite and prune the player's stored progress.
    /// </summary>
    public bool IsLoaded { get; set; }

    public Dictionary<string, AchievementProgressRuntime> Achievements { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public PlayerStateSnapshot ToSnapshot(DateTimeOffset now)
        => new(
            SteamId,
            Name,
            GlobalXp,
            ActiveRaceId,
            Races.ToDictionary(x => x.Key, x => x.Value.ToSnapshot(), StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, DateTimeOffset>(Cooldowns, StringComparer.OrdinalIgnoreCase),
            Stats.ToSnapshot(now));

    public PlayerPersistenceDto ToPersistence(DateTimeOffset now)
        => new()
        {
            SteamId = SteamId,
            Name = Name,
            GlobalXp = GlobalXp,
            ActiveRaceId = ActiveRaceId,
            Races = Races.Values.Select(x => x.ToPersistence()).ToArray(),
            Stats = Stats.ToPersistence(now),
            Achievements = Achievements.Select(x => x.Value.ToPersistence(x.Key)).ToArray()
        };
}

internal sealed class PlayerStatsRuntime
{
    public long Kills { get; set; }
    public long Deaths { get; set; }
    public long Headshots { get; set; }
    public long RoundsPlayed { get; set; }
    public long RoundsWon { get; set; }

    /// <summary>Play time stored before the current session started.</summary>
    public long StoredPlaySeconds { get; set; }

    public DateTimeOffset SessionStartedAt { get; set; }

    public long PlaySeconds(DateTimeOffset now)
        => StoredPlaySeconds + Math.Max(0L, (long)(now - SessionStartedAt).TotalSeconds);

    public PlayerStatsSnapshot ToSnapshot(DateTimeOffset now)
        => new(Kills, Deaths, Headshots, RoundsPlayed, RoundsWon, PlaySeconds(now));

    public PlayerStatsPersistenceDto ToPersistence(DateTimeOffset now)
        => new()
        {
            Kills = Kills,
            Deaths = Deaths,
            Headshots = Headshots,
            RoundsPlayed = RoundsPlayed,
            RoundsWon = RoundsWon,
            PlaySeconds = PlaySeconds(now)
        };

    public static PlayerStatsRuntime FromPersistence(PlayerStatsPersistenceDto stored, DateTimeOffset sessionStartedAt)
        => new()
        {
            Kills = stored.Kills,
            Deaths = stored.Deaths,
            Headshots = stored.Headshots,
            RoundsPlayed = stored.RoundsPlayed,
            RoundsWon = stored.RoundsWon,
            StoredPlaySeconds = stored.PlaySeconds,
            SessionStartedAt = sessionStartedAt
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

internal sealed class AchievementProgressRuntime
{
    public long Progress { get; set; }
    public bool Unlocked { get; set; }
    public DateTimeOffset? UnlockedAt { get; set; }

    public AchievementProgressSnapshot ToSnapshot()
        => new(Progress, Unlocked, UnlockedAt);

    public AchievementProgressPersistenceDto ToPersistence(string achievementId)
        => new()
        {
            AchievementId = achievementId,
            Progress = Progress,
            Unlocked = Unlocked,
            UnlockedAt = UnlockedAt
        };
}
