using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Diagnostics;
using Warcraft.Api.Events;
using Warcraft.Api.Modules;
using Warcraft.Api.Persistence;
using Warcraft.Core.Abilities;
using Warcraft.Core.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Diagnostics;

/// <summary>
/// Builds status and race catalog health from Core registries. Remembers the
/// last <see cref="RaceCatalogReloadedEvent"/> (including rejected reloads).
/// </summary>
internal sealed class DiagnosticsService : IDiagnosticsApi, IDisposable
{
    private readonly RaceCatalogService _races;
    private readonly AbilityRegistrationStore _registrations;
    private readonly IModulesApi _modules;
    private readonly IPersistenceApi _persistence;
    private readonly PlayerStateStore _players;
    private readonly TimeProvider _time;
    private readonly IDisposable _subscription;
    private volatile RaceCatalogReloadInfo? _lastReload;

    public DiagnosticsService(
        RaceCatalogService races,
        AbilityRegistrationStore registrations,
        IModulesApi modules,
        IPersistenceApi persistence,
        PlayerStateStore players,
        IWarcraftEventBus events,
        TimeProvider time)
    {
        _races = races;
        _registrations = registrations;
        _modules = modules;
        _persistence = persistence;
        _players = players;
        _time = time;
        _subscription = events.Subscribe<RaceCatalogReloadedEvent>(reload =>
            _lastReload = new RaceCatalogReloadInfo(
                reload.Success,
                reload.Source,
                _time.GetUtcNow(),
                reload.RaceCount,
                reload.Errors));
    }

    public WarcraftStatus GetStatus()
        => new(
            WarcraftVersion.Current,
            _modules.GetAll().OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
            _registrations.GetAll().Count,
            _races.GetAll().Count,
            _persistence.ProviderName,
            _players.GetLoadedPlayers().Count,
            _lastReload);

    public RaceCatalogHealth GetRaceCatalogHealth()
    {
        var races = _races.GetAll().OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var unresolved = new List<RaceAbilityIssue>();
        var passiveUltimates = new List<RaceAbilityIssue>();

        foreach (var race in races)
        {
            foreach (var ability in race.Abilities)
            {
                if (_registrations.Get(ability.Id) is null)
                    unresolved.Add(new RaceAbilityIssue(race.Id, ability.Id, false));
            }

            if (race.Ultimate is not { } ultimate)
                continue;

            var registration = _registrations.Get(ultimate.Id);
            if (registration is null)
                unresolved.Add(new RaceAbilityIssue(race.Id, ultimate.Id, true));
            else if (registration.Kind == AbilityKind.Passive)
                passiveUltimates.Add(new RaceAbilityIssue(race.Id, ultimate.Id, true));
        }

        return new RaceCatalogHealth(
            races.Length,
            races.Where(x => !x.VipOnly && (x.Requirements?.IsEmpty ?? true)).Select(x => x.Id).ToArray(),
            races.Where(x => !(x.Requirements?.IsEmpty ?? true)).Select(x => x.Id).ToArray(),
            races.Where(x => x.VipOnly).Select(x => x.Id).ToArray(),
            unresolved,
            passiveUltimates,
            _lastReload);
    }

    public void Dispose() => _subscription.Dispose();
}
