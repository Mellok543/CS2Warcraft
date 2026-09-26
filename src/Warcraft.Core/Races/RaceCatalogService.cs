using Warcraft.Api.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Races;

internal sealed class RaceCatalogService(PlayerStateStore players) : IRacesApi
{
    private IReadOnlyDictionary<string, RaceDefinition> _catalog =
        new Dictionary<string, RaceDefinition>(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public IReadOnlyCollection<RaceDefinition> GetAll()
    {
        lock (_sync)
            return _catalog.Values.ToArray();
    }

    public RaceDefinition? Get(string raceId)
    {
        lock (_sync)
            return _catalog.GetValueOrDefault(raceId);
    }

    public RaceCatalogReplaceResult ReplaceCatalog(
        IReadOnlyCollection<RaceDefinition> races,
        string source)
    {
        var errors = Validate(races);
        if (errors.Count > 0)
            return new(false, _catalog.Count, errors);

        var next = races.ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

        lock (_sync)
            _catalog = next;

        return new(true, next.Count, []);
    }

    public RaceSelectionResult SelectRace(ulong steamId, string raceId, string reason)
    {
        RaceDefinition? race;
        lock (_sync)
            race = _catalog.GetValueOrDefault(raceId);

        if (race is null)
            return new(false, $"Race '{raceId}' is not registered.", null, null);

        var player = players.GetRequired(steamId);
        var previous = player.ActiveRaceId;
        player.ActiveRaceId = race.Id;

        if (!player.Races.ContainsKey(race.Id))
            player.Races[race.Id] = new RaceProgressRuntime { RaceId = race.Id };

        return new(true, reason, previous, race.Id);
    }

    private static List<string> Validate(IReadOnlyCollection<RaceDefinition> races)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var race in races)
        {
            if (string.IsNullOrWhiteSpace(race.Id))
                errors.Add("Race id is required.");
            else if (!ids.Add(race.Id))
                errors.Add($"Duplicate race id '{race.Id}'.");

            if (string.IsNullOrWhiteSpace(race.Name))
                errors.Add($"Race '{race.Id}' has no name.");

            if (race.MaxLevel < 1)
                errors.Add($"Race '{race.Id}' maxLevel must be >= 1.");

            var abilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ability in race.Abilities)
            {
                if (!abilityIds.Add(ability.Id))
                    errors.Add($"Race '{race.Id}' contains duplicate ability '{ability.Id}'.");
                if (ability.MaxLevel < 1)
                    errors.Add($"Ability '{race.Id}/{ability.Id}' maxLevel must be >= 1.");
                if (ability.UnlockLevel < 1 || ability.UnlockLevel > race.MaxLevel)
                    errors.Add($"Ability '{race.Id}/{ability.Id}' has invalid unlockLevel.");
            }

            if (race.Ultimate is not null && !abilityIds.Add(race.Ultimate.Id))
                errors.Add($"Race '{race.Id}' reuses ability '{race.Ultimate.Id}' as ultimate.");
        }

        return errors;
    }
}
