using System.Text.Json;

namespace Warcraft.Core.Conditions.Builtin;

/// <summary><c>"weaponTypes": ["rifle", "smg"]</c> — owner's active weapon category.</summary>
internal sealed class WeaponTypesConditionFactory : IAbilityConditionFactory
{
    public string Key => "weaponTypes";

    public ConditionParseResult Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return ConditionParseResult.Invalid("expected an array of weapon categories.");

        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in value.EnumerateArray())
        {
            var category = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (category is null || !WeaponCategories.All.Contains(category))
            {
                return ConditionParseResult.Invalid(
                    $"unknown weapon category '{item}'. " +
                    $"Known: {string.Join(", ", WeaponCategories.All.Order())}.");
            }

            categories.Add(category);
        }

        return categories.Count == 0
            ? ConditionParseResult.Invalid("at least one weapon category is required.")
            : ConditionParseResult.Ok(new WeaponTypesCondition(categories));
    }

    private sealed class WeaponTypesCondition(IReadOnlySet<string> categories) : IAbilityCondition
    {
        public bool IsSatisfied(in PlayerCombatState state)
            => state.WeaponCategory is not null && categories.Contains(state.WeaponCategory);
    }
}
