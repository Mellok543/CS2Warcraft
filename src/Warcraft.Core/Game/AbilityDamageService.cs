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

    private readonly PendingAbilityKillCredits _credits =
        new(() => Server.CurrentTime);

    public AbilityDamageResult DealAbilityDamage(AbilityDamageRequest request)
    {
        if (request.Amount <= 0)
            return AbilityDamageResult.NotApplied;

        var victim = Utilities.GetPlayerFromSlot(request.VictimSlot);
        var pawn = victim?.PlayerPawn.Value;

        if (victim is not { IsValid: true, PawnIsAlive: true } ||
            pawn is not { IsValid: true, Health: > 0 })
        {
            return AbilityDamageResult.NotApplied;
        }

        var health = pawn.Health;
        if (health > request.Amount)
        {
            pawn.Health = health - request.Amount;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            return new AbilityDamageResult(true, false, request.Amount);
        }

        _credits.Store(
            request.VictimSlot,
            request.AttackerSteamId,
            request.AbilityId,
            CreditLifetimeSeconds);

        victim.CommitSuicide(false, true);
        return new AbilityDamageResult(true, true, health);
    }

    public bool TryTakeCredit(int victimSlot, out PendingKillCredit credit)
        => _credits.TryTake(victimSlot, out credit);

    public int PruneExpiredCredits()
        => _credits.PruneExpired();

    public void Clear()
        => _credits.Clear();
}
