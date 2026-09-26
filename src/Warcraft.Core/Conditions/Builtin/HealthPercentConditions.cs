using System.Text.Json;

namespace Warcraft.Core.Conditions.Builtin;

/// <summary><c>"minHealthPercent": 0.5</c> — owner health / max health &gt;= value.</summary>
internal sealed class MinHealthPercentConditionFactory : IAbilityConditionFactory
{
    public string Key => "minHealthPercent";

    public ConditionParseResult Parse(JsonElement value)
        => HealthFraction.TryParse(value, out var threshold, out var error)
            ? ConditionParseResult.Ok(new Condition(threshold))
            : ConditionParseResult.Invalid(error);

    private sealed class Condition(double threshold) : IAbilityCondition
    {
        public bool IsSatisfied(in PlayerCombatState state) => state.HealthFraction >= threshold;
    }
}

/// <summary><c>"maxHealthPercent": 0.2</c> — owner health / max health &lt;= value.</summary>
internal sealed class MaxHealthPercentConditionFactory : IAbilityConditionFactory
{
    public string Key => "maxHealthPercent";

    public ConditionParseResult Parse(JsonElement value)
        => HealthFraction.TryParse(value, out var threshold, out var error)
            ? ConditionParseResult.Ok(new Condition(threshold))
            : ConditionParseResult.Invalid(error);

    private sealed class Condition(double threshold) : IAbilityCondition
    {
        public bool IsSatisfied(in PlayerCombatState state) => state.HealthFraction <= threshold;
    }
}

internal static class HealthFraction
{
    public static bool TryParse(JsonElement value, out double fraction, out string error)
    {
        error = string.Empty;

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out fraction) &&
            fraction is >= 0.0 and <= 1.0)
        {
            return true;
        }

        fraction = 0;
        error = "expected a number between 0.0 and 1.0.";
        return false;
    }
}
