using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Warcraft.Api.Abilities;

namespace Warcraft.Abilities;

/// <summary>Passive: reduced gravity. Config: gravity (scale, e.g. 0.75).</summary>
internal sealed class LowGravityAbility : PawnAttributeAbility
{
    private const float Epsilon = 0.001f;
    private const float DefaultGravity = 1.0f;

    public override string Id => "low_gravity";
    protected override string Description => "Reduces gravity for higher jumps.";
    protected override string DisplayName => "Низкая гравитация";

    protected override float? ReadValue(PlayerAbilitySnapshot ability)
    {
        var gravity = (float)AbilityConfigReader.GetLevelDouble(ability, "gravity", DefaultGravity);
        return gravity is > 0.05f and < DefaultGravity ? gravity : null;
    }

    protected override void Apply(CCSPlayerPawn pawn, float value)
    {
        if (Math.Abs(pawn.GravityScale - value) >= Epsilon)
            SetGravity(pawn, value);
    }

    protected override void Reset(CCSPlayerPawn pawn)
    {
        if (pawn.IsValid && Math.Abs(pawn.GravityScale - DefaultGravity) >= Epsilon)
            SetGravity(pawn, DefaultGravity);
    }

    private static void SetGravity(CCSPlayerPawn pawn, float value)
    {
        pawn.GravityScale = value;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_flGravityScale");
    }
}
