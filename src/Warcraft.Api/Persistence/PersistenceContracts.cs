namespace Warcraft.Api.Persistence;

public interface IPersistenceApi
{
    bool RegisterProvider(IWarcraftStorageProvider provider);
    bool UnregisterProvider(string providerName);
    bool HasProvider { get; }

    /// <summary>Name of the registered storage provider, or null.</summary>
    string? ProviderName { get; }

    ValueTask<PlayerPersistenceDto?> LoadPlayerAsync(
        ulong steamId,
        CancellationToken cancellationToken = default);

    ValueTask SavePlayerAsync(
        PlayerPersistenceDto player,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<PlayerLeaderboardEntry>> LoadLevelLeaderboardAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

public interface IWarcraftStorageProvider
{
    string ProviderName { get; }

    ValueTask<PlayerPersistenceDto?> LoadPlayerAsync(
        ulong steamId,
        CancellationToken cancellationToken = default);

    ValueTask SavePlayerAsync(
        PlayerPersistenceDto player,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<PlayerLeaderboardEntry>> LoadLevelLeaderboardAsync(
        int limit,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<PlayerLeaderboardEntry>>([]);
}

public sealed record PlayerLeaderboardEntry(
    ulong SteamId,
    string Name,
    long GlobalXp,
    long TotalRaceLevels);

public sealed record PlayerPersistenceDto
{
    public required ulong SteamId { get; init; }
    public required string Name { get; init; }
    public long GlobalXp { get; init; }
    public long AchievementCurrency { get; init; }
    public string? ActiveRaceId { get; init; }
    public IReadOnlyCollection<RaceProgressPersistenceDto> Races { get; init; } = [];
    public PlayerStatsPersistenceDto Stats { get; init; } = new();
    public IReadOnlyCollection<AchievementProgressPersistenceDto> Achievements { get; init; } = [];
}

/// <summary>Absolute lifetime totals; the storage provider overwrites stored values.</summary>
public sealed record PlayerStatsPersistenceDto
{
    public long Kills { get; init; }
    public long Deaths { get; init; }
    public long Headshots { get; init; }
    public long RoundsPlayed { get; init; }
    public long RoundsWon { get; init; }
    public long PlaySeconds { get; init; }
}

public sealed record RaceProgressPersistenceDto
{
    public required string RaceId { get; init; }
    public int Level { get; init; } = 1;
    public long Xp { get; init; }
    public int SkillPoints { get; init; }
    public IReadOnlyDictionary<string, int> AbilityLevels { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}


public sealed record AchievementProgressPersistenceDto
{
    public required string AchievementId { get; init; }
    public long Progress { get; init; }
    public bool Unlocked { get; init; }
    public DateTimeOffset? UnlockedAt { get; init; }
}
