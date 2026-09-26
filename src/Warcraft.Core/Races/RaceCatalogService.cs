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

    public RaceSelectionResult SelectRace(
        ulong steamId,
        string raceId,
        string reason)
    {
        var race = Get(raceId);

        if (race is null)
            return new(false, $"Race '{raceId}' is not registered.", null, null);

        if (race.VipOnly &&
            !modifiers.GetCombined(steamId).CanAccessVipRaces)
        {
            return new(
                false,
                $"Race '{race.Name}' requires VIP access.",
                players.Get(steamId)?.ActiveRaceId,
                null);
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
