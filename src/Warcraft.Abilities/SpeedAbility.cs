using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Warcraft.Api.Abilities;

namespace Warcraft.Abilities;

/// <summary>Passive: movement speed multiplier. Config: multiplier (e.g. 1.10).</summary>
internal sealed class SpeedAbility : PawnAttributeAbility
{
    private const float Epsilon = 0.001f;
    private const float DefaultModifier = 1.0f;

    public override string Id => "speed";
    protected override string Description =>
        "Скорость передвижения x{multiplier}.";
    protected override string DisplayName => "Скорость";

    protected override float? ReadValue(PlayerAbilitySnapshot ability)
    {
        var multiplier = (float)AbilityConfigReader.GetLevelDouble(ability, "multiplier", DefaultModifier);
        return multiplier > DefaultModifier ? Math.Min(multiplier, 3.0f) : null;
    }

    protected override void Apply(CCSPlayerPawn pawn, float value)
    {
        var current = pawn.VelocityModifier;

        // Below 1.0 the engine is applying damage slowdown; let it recover first.
        if (current < DefaultModifier - Epsilon || Math.Abs(current - value) < Epsilon)
            return;

        SetModifier(pawn, value);
    }

    protected override void Reset(CCSPlayerPawn pawn)
    {
        if (pawn.IsValid && pawn.VelocityModifier > DefaultModifier + Epsilon)
            SetModifier(pawn, DefaultModifier);
    }

    private static void SetModifier(CCSPlayerPawn pawn, float value)
    {
        pawn.VelocityModifier = value;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
    }
}
