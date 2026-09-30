using CounterStrikeSharp.API.Core;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;

namespace Warcraft.Abilities;

/// <summary>
/// Passive: makes the player model translucent while usable (e.g. only with a
/// knife via conditions). Config: alpha (0 = invisible, 255 = normal).
/// Player model and owned weapons use the same transparency.
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

    protected override void Apply(CCSPlayerPawn pawn, float value)
    {
        SetPlayerAndWeaponsAlpha(pawn, (int)value);
    }

    protected override void Reset(CCSPlayerPawn pawn)
    {
        if (pawn.IsValid)
            SetPlayerAndWeaponsAlpha(pawn, PlayerRender.Opaque);
    }

    private static void SetPlayerAndWeaponsAlpha(CCSPlayerPawn pawn, int alpha)
    {
        PlayerRender.SetAlpha(pawn, alpha);

        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons is null)
            return;

        foreach (var handle in weapons)
        {
            var weapon = handle.Value;
            if (weapon is { IsValid: true })
                PlayerRender.SetAlpha(weapon, alpha);
        }
    }
}
