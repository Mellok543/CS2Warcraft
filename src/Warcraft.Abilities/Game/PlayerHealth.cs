using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Warcraft.Abilities.Game;

/// <summary>Health manipulation used by ability effects. Game thread only.</summary>
internal static class PlayerHealth
{
    public static int Heal(CCSPlayerPawn pawn, int amount, int? cap = null)
    {
        if (amount <= 0 || pawn.Health <= 0)
            return 0;

        var limit = cap ?? pawn.MaxHealth;
        if (pawn.Health >= limit)
            return 0;

        var before = pawn.Health;
        pawn.Health = Math.Min(limit, pawn.Health + amount);
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        return pawn.Health - before;
    }

    public static void SetMaxAndCurrent(CCSPlayerPawn pawn, int value)
    {
        pawn.MaxHealth = value;
        pawn.Health = value;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
    }
}
