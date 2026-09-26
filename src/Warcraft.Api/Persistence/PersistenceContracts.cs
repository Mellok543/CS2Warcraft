namespace Warcraft.Api.Persistence;

public interface IPersistenceApi
{
    bool RegisterProvider(IWarcraftStorageProvider provider);
    bool HasProvider { get; }
    ValueTask<PlayerPersistenceDto?> LoadPlayerAsync(
        ulong steamId,
        CancellationToken cancellationToken = default);
    ValueTask SavePlayerAsync(
        PlayerPersistenceDto player,
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
}

public sealed record PlayerPersistenceDto
{
    public required ulong SteamId { get; init; }
    public required string Name { get; init; }
    public long GlobalXp { get; init; }
    public string? ActiveRaceId { get; init; }
    public IReadOnlyCollection<RaceProgressPersistenceDto> Races { get; init; } = [];
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
