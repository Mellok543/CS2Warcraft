using System.Text.Json;

namespace Warcraft.Core.Conditions.Builtin;

/// <summary><c>"requiresGrounded": true</c> — owner must (or, with false, must not) stand on ground.</summary>
internal sealed class RequiresGroundedConditionFactory : IAbilityConditionFactory
{
    public string Key => "requiresGrounded";

    public ConditionParseResult Parse(JsonElement value)
        => value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? ConditionParseResult.Ok(new Condition(value.GetBoolean()))
            : ConditionParseResult.Invalid("expected true or false.");

    private sealed class Condition(bool grounded) : IAbilityCondition
    {
        public string Description => grounded ? "стоя на земле" : "в воздухе";

        public bool IsSatisfied(in PlayerCombatState state) => state.IsGrounded == grounded;
    }
}
