using Warcraft.Api.Modules;

namespace Warcraft.Api.Diagnostics;

/// <summary>Read-only operational information for admin tooling and logs.</summary>
public interface IDiagnosticsApi
{
    WarcraftStatus GetStatus();
    RaceCatalogHealth GetRaceCatalogHealth();
}

public sealed record WarcraftStatus(
    string CoreVersion,
    IReadOnlyList<ModuleRegistration> Modules,
    int RegisteredAbilities,
    int LoadedRaces,
    string? PersistenceProvider,
    int LoadedPlayers,
    RaceCatalogReloadInfo? LastRaceReload);

public sealed record RaceCatalogReloadInfo(
    bool Success,
    string Source,
    DateTimeOffset At,
    int RaceCount,
    IReadOnlyList<string> Errors);

/// <param name="UnresolvedHandlers">Race abilities whose mechanic id has no registered handler.</param>
/// <param name="PassiveUltimates">Ultimates using a passive mechanic, which can never be activated.</param>
public sealed record RaceCatalogHealth(
    int TotalRaces,
    IReadOnlyList<string> StarterRaces,
    IReadOnlyList<string> LockedRaces,
    IReadOnlyList<string> VipRaces,
    IReadOnlyList<RaceAbilityIssue> UnresolvedHandlers,
    IReadOnlyList<RaceAbilityIssue> PassiveUltimates,
    RaceCatalogReloadInfo? LastReload)
{
    public bool IsHealthy =>
        UnresolvedHandlers.Count == 0 &&
        PassiveUltimates.Count == 0 &&
        (LastReload?.Success ?? true);
}

public sealed record RaceAbilityIssue(string RaceId, string AbilityId, bool IsUltimate);
