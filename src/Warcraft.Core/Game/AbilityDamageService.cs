using CounterStrikeSharp.API;
using Warcraft.Api.Combat;

namespace Warcraft.Core.Game;

/// <summary>
/// Applies ability damage and remembers who dealt a lethal hit, so the Core
/// death hooks can credit the kill to the ability owner. Game thread only.
/// </summary>
internal sealed class AbilityDamageService : ICombatApi
{
    private const float CreditLifetimeSeconds = 2f;

    private readonly Dictionary<int, PendingKillCredit> _pending = [];

    public AbilityDamageResult DealAbilityDamage(AbilityDamageRequest request)
    {
        if (request.Amount <= 0)
            return AbilityDamageResult.NotApplied;

        var victim = Utilities.GetPlayerFromSlot(request.VictimSlot);
        var pawn = victim?.PlayerPawn.Value;

        if (victim is not { IsValid: true, PawnIsAlive: true } || pawn is not { IsValid: true, Health: > 0 })
            return AbilityDamageResult.NotApplied;

        var health = pawn.Health;
        if (health > request.Amount)
        {
            pawn.Health = health - request.Amount;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            return new AbilityDamageResult(true, false, request.Amount);
        }

        _pending[request.VictimSlot] = new PendingKillCredit(
            request.AttackerSteamId,
            request.AbilityId,
            Server.CurrentTime + CreditLifetimeSeconds);

        victim.CommitSuicide(false, true);
        return new AbilityDamageResult(true, true, health);
    }

    /// <summary>Returns the ability owner credited with the death of <paramref name="victimSlot"/>.</summary>
    public bool TryTakeCredit(int victimSlot, out PendingKillCredit credit)
    {
        if (!_pending.Remove(victimSlot, out credit!))
            return false;

        return credit.ExpiresAt >= Server.CurrentTime;
    }

    public void Clear() => _pending.Clear();
}

internal sealed record PendingKillCredit(ulong AttackerSteamId, string AbilityId, float ExpiresAt);
