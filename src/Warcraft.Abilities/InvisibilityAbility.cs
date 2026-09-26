using CounterStrikeSharp.API.Core;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;

namespace Warcraft.Abilities;

/// <summary>
/// Passive: makes the player model translucent while usable (e.g. only with a
/// knife via conditions). Config: alpha (0 = invisible, 255 = normal).
/// Held weapons stay visible.
/// </summary>
internal sealed class InvisibilityAbility : PawnAttributeAbility
{
    public override string Id => "invisibility";
    protected override string Description =>
        "Делает модель полупрозрачной: видимость {alpha} из 255.";
    protected override string DisplayName => "Невидимость";

    protected override float? ReadValue(PlayerAbilitySnapshot ability)
    {
        var alpha = Math.Clamp(AbilityConfigReader.GetLevelInt(ability, "alpha", PlayerRender.Opaque), 0, PlayerRender.Opaque);
        return alpha < PlayerRender.Opaque ? alpha : null;
    }

    protected override void Apply(CCSPlayerPawn pawn, float value) => PlayerRender.SetAlpha(pawn, (int)value);

    protected override void Reset(CCSPlayerPawn pawn)
    {
        if (pawn.IsValid)
            PlayerRender.SetAlpha(pawn, PlayerRender.Opaque);
    }
}
