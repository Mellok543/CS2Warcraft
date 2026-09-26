using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Warcraft.Abilities.Game;

/// <summary>Player model transparency. Game thread only.</summary>
internal static class PlayerRender
{
    public const int Opaque = 255;

    public static void SetAlpha(CCSPlayerPawn pawn, int alpha)
    {
        alpha = Math.Clamp(alpha, 0, Opaque);
        var mode = alpha < Opaque ? RenderMode_t.kRenderTransAlpha : RenderMode_t.kRenderNormal;

        if (pawn.Render.A == alpha && pawn.RenderMode == mode)
            return;

        pawn.RenderMode = mode;
        pawn.Render = Color.FromArgb(alpha, pawn.Render.R, pawn.Render.G, pawn.Render.B);
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
    }
}
