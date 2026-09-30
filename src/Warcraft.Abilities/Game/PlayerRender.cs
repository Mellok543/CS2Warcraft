using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Warcraft.Abilities.Game;

/// <summary>Player model transparency. Game thread only.</summary>
internal static class PlayerRender
{
    public const int Opaque = 255;

    public static void SetAlpha(CBaseModelEntity entity, int alpha)
    {
        alpha = Math.Clamp(alpha, 0, Opaque);
        var mode = alpha < Opaque ? RenderMode_t.kRenderTransAlpha : RenderMode_t.kRenderNormal;

        if (entity.Render.A == alpha && entity.RenderMode == mode)
            return;

        entity.RenderMode = mode;
        entity.Render = Color.FromArgb(alpha, entity.Render.R, entity.Render.G, entity.Render.B);
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
    }
}
