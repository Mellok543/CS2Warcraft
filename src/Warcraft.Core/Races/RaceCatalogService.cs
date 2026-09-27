using Warcraft.Api.Events;
using Warcraft.Api.Modifiers;
using Warcraft.Api.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Races;

internal sealed class RaceCatalogService(
    PlayerStateStore players,
    IWarcraftEventBus events,
    IModifiersApi modifiers,
    RaceCatalogCompiler compiler) : IRacesApi
{
    private volatile CompiledRaceCatalog _catalog = CompiledRaceCatalog.Empty;

    public IReadOnlyCollection<RaceDefinition> GetAll()
        => _catalog.Races.Values.Select(x => x.Definition).ToArray();

    public RaceDefinition? Get(string raceId)
        => GetCompiled(raceId)?.Definition;

    internal CompiledRace? GetCompiled(string raceId)
        => _catalog.Races.GetValueOrDefault(raceId);

    public RaceCatalogReplaceResult ReplaceCatalog(
        IReadOnlyCollection<RaceDefinition> races,
        string source)
    {
        var errors = new List<string>();
        var next = compiler.Compile(races, errors);

        if (next is null)
            return new(false, _catalog.Races.Count, errors);

        _catalog = next;
        return new(true, next.Races.Count, []);
    }

    public RaceAvailability GetAvailability(ulong steamId, string raceId)
    {
        var race = Get(raceId);
        var player = players.TryGetRuntime(steamId);
        if (race is null || player is null)
            return new RaceAvailability(false, false, false, []);

        var vipLocked = race.VipOnly && !modifiers.GetCombined(steamId).CanAccessVipRaces;
        var unlocked = player.Races.ContainsKey(race.Id);
        var requirements = RaceRequirementEvaluator.Evaluate(player, race.Requirements, Get);

        return new RaceAvailability(
            !vipLocked && (unlocked || requirements.All(x => x.IsMet)),
            vipLocked,
            unlocked,
            requirements);
    }

    public RaceSelectionResult SelectRace(
        ulong steamId,
        string raceId,
        string reason,
        bool force = false)
    {
        var race = Get(raceId);

        if (race is null)
            return new(false, $"Race '{raceId}' is not registered.", null, null);

        if (!force)
        {
            var availability = GetAvailability(steamId, race.Id);

            if (availability.VipLocked)
            {
                return new(
                    false,
                    $"Раса «{race.Name}» доступна только VIP.",
                    players.Get(steamId)?.ActiveRaceId,
                    null);
            }

            if (!availability.IsAvailable)
            {
                var missing = availability.Requirements
                    .Where(x => !x.IsMet)
                    .Select(x => $"{x.Description}: {x.Current}/{x.Required}");

                return new(
                    false,
                    $"Раса «{race.Name}» ещё закрыта. Нужно: {string.Join("; ", missing)}.",
                    players.Get(steamId)?.ActiveRaceId,
                    null);
            }
        }

        var player = players.GetRequired(steamId);
        var previous = player.ActiveRaceId;
        player.ActiveRaceId = race.Id;

        if (!player.Races.ContainsKey(race.Id))
            player.Races[race.Id] = new RaceProgressRuntime { RaceId = race.Id };

        events.Publish(new PlayerStateChangedEvent(steamId, reason));

        return new(true, reason, previous, race.Id);
    }
}
