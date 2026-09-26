using Warcraft.Api;
using Warcraft.Api.Combat;

namespace Warcraft.Abilities.Game;

/// <summary>
/// Periodic ability damage (poison, entangling roots). Damage goes through
/// <see cref="ICombatApi"/>, so lethal ticks are credited to the owner.
/// One effect per (victim, ability): re-applying refreshes it. Game thread only.
/// </summary>
internal sealed class DamageOverTime(IWarcraftApi api)
{
    private readonly Dictionary<(int Slot, string AbilityId), Effect> _effects = [];

    public void Apply(ulong attackerSteamId, int victimSlot, string abilityId, int damagePerTick, int ticks, double interval, double now)
    {
        if (damagePerTick <= 0 || ticks <= 0)
            return;

        interval = Math.Max(0.1, interval);
        _effects[(victimSlot, abilityId)] = new Effect(attackerSteamId, damagePerTick, ticks, interval, now + interval);
    }

    public void Update(double now)
    {
        foreach (var (key, effect) in _effects.ToArray())
        {
            if (now < effect.NextAt)
                continue;

            var result = api.Combat.DealAbilityDamage(
                new AbilityDamageRequest(effect.AttackerSteamId, key.Slot, effect.DamagePerTick, key.AbilityId));

            var remaining = effect.RemainingTicks - 1;
            if (!result.Applied || result.Killed || remaining <= 0)
                _effects.Remove(key);
            else
                _effects[key] = effect with { RemainingTicks = remaining, NextAt = effect.NextAt + effect.Interval };
        }
    }

    public void Clear() => _effects.Clear();

    private sealed record Effect(ulong AttackerSteamId, int DamagePerTick, int RemainingTicks, double Interval, double NextAt);
}
