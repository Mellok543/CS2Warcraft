using Warcraft.Api.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Races;

/// <summary>Computes requirement progress for a player. Pure; no engine access.</summary>
internal static class RaceRequirementEvaluator
{
    public static IReadOnlyList<RaceRequirementProgress> Evaluate(
        PlayerRuntimeState player,
        RaceRequirements? requirements,
        Func<string, RaceDefinition?> findRace)
    {
        if (requirements is null || requirements.IsEmpty)
            return [];

        var result = new List<RaceRequirementProgress>();

        if (requirements.TotalLevel > 0)
        {
            result.Add(new RaceRequirementProgress(
                "Сумма уровней всех рас",
                player.Races.Values.Sum(x => (long)x.Level),
                requirements.TotalLevel));
        }

        if (requirements.GlobalXp > 0)
            result.Add(new RaceRequirementProgress("Общий опыт", player.GlobalXp, requirements.GlobalXp));

        if (requirements.PlaytimeHours > 0)
        {
            result.Add(new RaceRequirementProgress(
                "Время на сервере (ч)",
                player.Stats.PlaySeconds(DateTimeOffset.UtcNow) / 3600,
                requirements.PlaytimeHours));
        }

        foreach (var (raceId, level) in requirements.Races)
        {
            var name = findRace(raceId)?.Name ?? raceId;
            result.Add(new RaceRequirementProgress(
                $"Уровень расы {name}",
                player.Races.GetValueOrDefault(raceId)?.Level ?? 0,
                level));
        }

        return result;
    }

    public static void Validate(
        RaceDefinition race,
        IReadOnlyDictionary<string, RaceDefinition> catalog,
        List<string> errors)
    {
        var requirements = race.Requirements;
        if (requirements is null)
            return;

        if (requirements.TotalLevel < 0 || requirements.GlobalXp < 0 || requirements.PlaytimeHours < 0)
            errors.Add($"Race '{race.Id}' requirements must not be negative.");

        foreach (var (raceId, level) in requirements.Races)
        {
            if (string.Equals(raceId, race.Id, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Race '{race.Id}' cannot require itself.");
                continue;
            }

            if (!catalog.TryGetValue(raceId, out var required))
            {
                errors.Add($"Race '{race.Id}' requires unknown race '{raceId}'.");
                continue;
            }

            if (level < 1 || level > required.MaxLevel)
                errors.Add($"Race '{race.Id}' requires '{raceId}' level {level}, allowed 1..{required.MaxLevel}.");
        }
    }
}
