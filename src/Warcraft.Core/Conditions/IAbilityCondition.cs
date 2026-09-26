using System.Text.Json;

namespace Warcraft.Core.Conditions;

internal interface IAbilityCondition
{
    bool IsSatisfied(in PlayerCombatState state);
}

/// <summary>
/// Parses one key of a race ability <c>conditions</c> object. Adding a new
/// condition means adding one factory and registering it in
/// <see cref="AbilityConditionRegistry.CreateDefault"/>.
/// </summary>
internal interface IAbilityConditionFactory
{
    string Key { get; }
    ConditionParseResult Parse(JsonElement value);
}

internal sealed record ConditionParseResult(IAbilityCondition? Condition, string? Error)
{
    public static ConditionParseResult Ok(IAbilityCondition condition) => new(condition, null);
    public static ConditionParseResult Invalid(string error) => new(null, error);
}
