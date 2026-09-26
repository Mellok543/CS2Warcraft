using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Warcraft.Api.Abilities;

namespace Warcraft.Abilities;

/// <summary>
/// Passive: makes the player model translucent while usable (e.g. only with a
/// knife via conditions). Config: alpha (0 = invisible, 255 = normal).
/// Held weapons stay visible.
/// </summary>
internal sealed class InvisibilityAbility : PawnAttributeAbility
{
    private const int Opaque = 255;

    public override string Id => "invisibility";
    protected override string Description =>
        "Делает модель полупрозрачной: видимость {alpha} из 255.";
    protected override string DisplayName => "Невидимость";

    protected override float? ReadValue(PlayerAbilitySnapshot ability)
    {
        var alpha = Math.Clamp(AbilityConfigReader.GetLevelInt(ability, "alpha", Opaque), 0, Opaque);
        return alpha < Opaque ? alpha : null;
    }

    protected override void Apply(CCSPlayerPawn pawn, float value)
        => SetAlpha(pawn, (int)value, RenderMode_t.kRenderTransAlpha);

    protected override void Reset(CCSPlayerPawn pawn)
    {
        if (pawn.IsValid)
            SetAlpha(pawn, Opaque, RenderMode_t.kRenderNormal);
    }

    private static void SetAlpha(CCSPlayerPawn pawn, int alpha, RenderMode_t mode)
    {
        if (pawn.Render.A == alpha && pawn.RenderMode == mode)
            return;

        pawn.RenderMode = mode;
        pawn.Render = Color.FromArgb(alpha, pawn.Render.R, pawn.Render.G, pawn.Render.B);
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
    }
}
