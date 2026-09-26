namespace Warcraft.Core.Game;

/// <summary>
/// Tracks short-lived ability kill attribution by victim slot.
/// Game-thread only. Extracted from the CSS bridge so attribution semantics are deterministic and testable.
/// </summary>
internal sealed class PendingAbilityKillCredits(Func<float> currentTime)
{
    private readonly Dictionary<int, PendingKillCredit> _pending = [];

    public void Store(int victimSlot, ulong attackerSteamId, string abilityId, float lifetimeSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(abilityId);

        var expiresAt = currentTime() + Math.Max(0.0f, lifetimeSeconds);
        _pending[victimSlot] = new PendingKillCredit(attackerSteamId, abilityId, expiresAt);
    }

    public bool TryTake(int victimSlot, out PendingKillCredit credit)
    {
        if (!_pending.Remove(victimSlot, out credit!))
            return false;

        return credit.ExpiresAt >= currentTime();
    }

    public int PruneExpired()
    {
        var now = currentTime();
        var expired = _pending
            .Where(x => x.Value.ExpiresAt < now)
            .Select(x => x.Key)
            .ToArray();

        foreach (var victimSlot in expired)
            _pending.Remove(victimSlot);

        return expired.Length;
    }

    public void Clear() => _pending.Clear();
}

internal sealed record PendingKillCredit(
    ulong AttackerSteamId,
    string AbilityId,
    float ExpiresAt);
