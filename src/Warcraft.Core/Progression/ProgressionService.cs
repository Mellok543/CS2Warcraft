using Warcraft.Api.Progression;
using Warcraft.Core.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Progression;

internal sealed class ProgressionService(
    PlayerStateStore players,
    RaceCatalogService races) : IProgressApi
{
    public ProgressMutationResult AddXp(ulong steamId, long amount, string reason)
    {
        if (amount == 0)
            return new(false, "XP amount must not be zero.");

        var player = players.GetRequired(steamId);
        var previousXp = player.GlobalXp;
        player.GlobalXp = Math.Max(0, player.GlobalXp + amount);

        if (player.ActiveRaceId is null)
            return new(true, reason, PreviousXp: previousXp, CurrentXp: player.GlobalXp);

        var race = races.Get(player.ActiveRaceId);
        if (race is null)
            return new(false, $"Active race '{player.ActiveRaceId}' is not registered.");

        var progress = GetOrCreateRace(player, race.Id);
        var previousLevel = progress.Level;
        progress.Xp = Math.Max(0, progress.Xp + amount);

        while (progress.Level < race.MaxLevel)
        {
            var required = RequiredXp(progress.Level);
            if (progress.Xp < required)
                break;

            progress.Xp -= required;
            progress.Level++;
            progress.SkillPoints++;
        }

        return new(true, reason, previousLevel, progress.Level, previousXp, player.GlobalXp);
    }

    public ProgressMutationResult SetRaceLevel(
        ulong steamId, string raceId, int level, string reason)
    {
        var race = races.Get(raceId);
        if (race is null)
            return new(false, $"Race '{raceId}' is not registered.");
        if (level < 1 || level > race.MaxLevel)
            return new(false, $"Level must be between 1 and {race.MaxLevel}.");

        var progress = GetOrCreateRace(players.GetRequired(steamId), race.Id);
        var previous = progress.Level;
        progress.Level = level;
        return new(true, reason, previous, level);
    }

    public ProgressMutationResult GiveSkillPoints(
        ulong steamId, string raceId, int amount, string reason)
    {
        var race = races.Get(raceId);
        if (race is null)
            return new(false, $"Race '{raceId}' is not registered.");

        var progress = GetOrCreateRace(players.GetRequired(steamId), race.Id);
        progress.SkillPoints = Math.Max(0, progress.SkillPoints + amount);
        return new(true, reason);
    }

    public ProgressMutationResult SetAbilityLevel(
        ulong steamId, string abilityId, int level, string reason)
    {
        var player = players.GetRequired(steamId);
        if (player.ActiveRaceId is null)
            return new(false, "Player has no active race.");

        var race = races.Get(player.ActiveRaceId);
        var definitions = race is null
            ? []
            : race.Abilities.Concat(race.Ultimate is null ? [] : [race.Ultimate]);

        var definition = definitions.FirstOrDefault(x =>
            string.Equals(x.Id, abilityId, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
            return new(false, $"Ability '{abilityId}' is not part of the active race.");
        if (level < 0 || level > definition.MaxLevel)
            return new(false, $"Ability level must be between 0 and {definition.MaxLevel}.");

        var progress = GetOrCreateRace(player, player.ActiveRaceId);
        progress.AbilityLevels[abilityId] = level;
        return new(true, reason);
    }

    public ProgressMutationResult ResetPlayer(ulong steamId, string reason)
    {
        var player = players.GetRequired(steamId);
        var previousXp = player.GlobalXp;

        player.GlobalXp = 0;
        player.ActiveRaceId = null;
        player.Races.Clear();
        player.Cooldowns.Clear();

        return new(true, reason, PreviousXp: previousXp, CurrentXp: 0);
    }

    private static RaceProgressRuntime GetOrCreateRace(PlayerRuntimeState player, string raceId)
    {
        if (player.Races.TryGetValue(raceId, out var progress))
            return progress;

        progress = new RaceProgressRuntime { RaceId = raceId };
        player.Races[raceId] = progress;
        return progress;
    }

    private static long RequiredXp(int currentLevel)
        => checked(100L * currentLevel * currentLevel);
}
