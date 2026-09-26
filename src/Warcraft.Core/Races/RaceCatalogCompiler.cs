using Warcraft.Api.Races;
using Warcraft.Core.Abilities;
using Warcraft.Core.Conditions;

namespace Warcraft.Core.Races;

/// <summary>
/// Validates a complete set of race definitions and builds a new catalog.
/// Any error rejects the whole set, so hot reload never applies partially.
/// </summary>
internal sealed class RaceCatalogCompiler(AbilityConditionRegistry conditions)
{
    public CompiledRaceCatalog? Compile(
        IReadOnlyCollection<RaceDefinition> races,
        List<string> errors)
    {
        var compiled = new Dictionary<string, CompiledRace>(StringComparer.OrdinalIgnoreCase);

        foreach (var race in races)
        {
            if (string.IsNullOrWhiteSpace(race.Id))
            {
                errors.Add("Race id is required.");
                continue;
            }

            if (compiled.ContainsKey(race.Id))
            {
                errors.Add($"Duplicate race id '{race.Id}'.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(race.Name))
                errors.Add($"Race '{race.Id}' has no name.");

            if (race.MaxLevel < 1)
                errors.Add($"Race '{race.Id}' maxLevel must be >= 1.");

            var abilities = new List<CompiledAbility>();
            var abilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ability in race.Abilities)
            {
                var result = CompileAbility(race, ability, false, abilityIds, errors);
                if (result is not null)
                    abilities.Add(result);
            }

            if (race.Ultimate is not null)
            {
                var result = CompileAbility(race, race.Ultimate, true, abilityIds, errors);
                if (result is not null)
                    abilities.Add(result);
            }

            compiled[race.Id] = new CompiledRace(race, abilities);
        }

        return errors.Count == 0 ? new CompiledRaceCatalog(compiled) : null;
    }

    private CompiledAbility? CompileAbility(
        RaceDefinition race,
        RaceAbilityDefinition ability,
        bool isUltimate,
        HashSet<string> abilityIds,
        List<string> errors)
    {
        var kind = isUltimate ? "Ultimate" : "Ability";

        if (string.IsNullOrWhiteSpace(ability.Id))
        {
            errors.Add($"Race '{race.Id}' contains an {kind.ToLowerInvariant()} without id.");
            return null;
        }

        var owner = $"{race.Id}/{ability.Id}";

        if (!abilityIds.Add(ability.Id))
        {
            errors.Add(isUltimate
                ? $"Race '{race.Id}' reuses ability '{ability.Id}' as ultimate."
                : $"Race '{race.Id}' contains duplicate ability '{ability.Id}'.");
            return null;
        }

        if (ability.MaxLevel < 1)
            errors.Add($"{kind} '{owner}' maxLevel must be >= 1.");

        if (ability.UnlockLevel < 1 || ability.UnlockLevel > race.MaxLevel)
            errors.Add($"{kind} '{owner}' has invalid unlockLevel.");

        var cooldownError = AbilityConfigValues.ValidateNonNegativeNumber(
            ability.Config,
            AbilityConfigValues.CooldownKey);

        if (cooldownError is not null)
            errors.Add($"{kind} '{owner}': {cooldownError}");

        if (ability.Description is { } description)
        {
            foreach (var key in AbilityDescriptionFormatter.MissingKeys(description, ability.Config).Distinct())
                errors.Add($"{kind} '{owner}' description uses '{{{key}}}' which is missing from config.");
        }

        var compiledConditions = conditions.Compile(ability.Conditions, owner, errors);

        return new CompiledAbility(ability, isUltimate, compiledConditions);
    }
}
