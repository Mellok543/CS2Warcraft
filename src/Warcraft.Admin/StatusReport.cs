using Warcraft.Api.Diagnostics;

namespace Warcraft.Admin;

/// <summary>Formats diagnostics for !wc_status (chat or server console).</summary>
internal static class StatusReport
{
    public static IEnumerable<string> Build(WarcraftStatus status, RaceCatalogHealth health)
    {
        yield return $"[Warcraft] Core {status.CoreVersion} | players loaded: {status.LoadedPlayers}";
        yield return $"[Warcraft] Persistence: {status.PersistenceProvider ?? "НЕТ ПРОВАЙДЕРА (прогресс не сохраняется)"}";
        yield return "[Warcraft] Modules: " + string.Join(", ", status.Modules.Select(x => $"{x.Id} {x.Version}"));
        yield return $"[Warcraft] Abilities registered: {status.RegisteredAbilities} | races loaded: {status.LoadedRaces}";
        yield return $"[Warcraft] Races: starter {health.StarterRaces.Count} [{string.Join(", ", health.StarterRaces)}], " +
                     $"locked {health.LockedRaces.Count}, VIP {health.VipRaces.Count} [{string.Join(", ", health.VipRaces)}]";

        if (status.LastRaceReload is { } reload)
        {
            yield return $"[Warcraft] Last race reload: {(reload.Success ? "OK" : "REJECTED")} " +
                         $"from {reload.Source} at {reload.At:HH:mm:ss} UTC, errors: {reload.Errors.Count}";

            foreach (var error in reload.Errors.Take(5))
                yield return $"[Warcraft]   ! {error}";
        }

        foreach (var issue in health.UnresolvedHandlers)
        {
            yield return $"[Warcraft]   ! {issue.RaceId}: {(issue.IsUltimate ? "ultimate" : "ability")} " +
                         $"'{issue.AbilityId}' has no handler";
        }

        foreach (var issue in health.PassiveUltimates)
            yield return $"[Warcraft]   ! {issue.RaceId}: ultimate '{issue.AbilityId}' is a passive mechanic";

        yield return health.IsHealthy ? "[Warcraft] Health: OK" : "[Warcraft] Health: ISSUES FOUND";
    }
}
