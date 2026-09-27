using Warcraft.Api.Events;

namespace Warcraft.Abilities.Game;

internal enum BuffKind
{
    /// <summary>Outgoing damage multiplier bonus (0.2 = +20%).</summary>
    DamageBonus,

    /// <summary>Incoming damage reduction (0.2 = −20%, capped at 90%).</summary>
    DamageReduction,

    /// <summary>Share of dealt damage returned as health.</summary>
    Lifesteal
}

/// <summary>
/// Short-lived buffs granted by auras, totems and activatables. The strongest
/// active buff of each kind applies. Keyed by SteamID (Warcraft players). Game thread only.
/// </summary>
internal sealed class TeamBuffs
{
    private const float MaxReduction = 0.9f;

    private readonly Dictionary<(ulong SteamId, BuffKind Kind), Buff> _buffs = [];

    public void Grant(
        ulong steamId,
        BuffKind kind,
        float value,
        double until,
        double now,
        ulong sourceSteamId = 0,
        string? sourceAbilityId = null)
    {
        if (value <= 0 || steamId == 0)
            return;

        var key = (steamId, kind);
        if (_buffs.TryGetValue(key, out var existing) && existing.Until > now)
        {
            // A stronger buff wins; an equal one refreshes the duration; a weaker one is ignored.
            if (existing.Value > value)
                return;

            if (Math.Abs(existing.Value - value) < 0.0001f)
                until = Math.Max(existing.Until, until);
        }

        _buffs[key] = new Buff(value, until, sourceSteamId, sourceAbilityId);
    }

    public float Get(ulong steamId, BuffKind kind, double now)
    {
        if (!_buffs.TryGetValue((steamId, kind), out var buff))
            return 0;

        if (now < buff.Until)
            return buff.Value;

        _buffs.Remove((steamId, kind));
        return 0;
    }

    public void Clear() => _buffs.Clear();

    /// <summary>Applies buffs to weapon damage. Subscribed before ability handlers.</summary>
    public IDisposable[] Attach(IWarcraftEventBus events, Func<double> now)
        =>
        [
            events.Subscribe<DamagePreEvent>(e =>
            {
                if (e.Damage <= 0)
                    return;

                // Damage bonuses amplify weapon damage only; reductions also protect from abilities.
                if (!e.IsAbilityDamage && e.AttackerSteamId is { } attacker && attacker != e.VictimSteamId)
                    e.Damage *= 1f + Get(attacker, BuffKind.DamageBonus, now());

                var reduction = GetBuff(e.VictimSteamId, BuffKind.DamageReduction, now());
                if (reduction is { } reductionBuff)
                {
                    var before = e.Damage;
                    e.Damage *= 1f - Math.Min(MaxReduction, reductionBuff.Value);
                    var prevented = Math.Max(0, (long)Math.Round(before - e.Damage));
                    if (prevented > 0 &&
                        reductionBuff.SourceSteamId != 0 &&
                        !string.IsNullOrWhiteSpace(reductionBuff.SourceAbilityId))
                    {
                        events.Publish(new AbilityTelemetryEvent(
                            reductionBuff.SourceSteamId,
                            reductionBuff.SourceAbilityId!,
                            AbilityTelemetryKind.DamagePrevented,
                            prevented));
                    }
                }
            }),
            events.Subscribe<DamagePostEvent>(e =>
            {
                if (e.AttackerSteamId is not { } attacker || attacker == e.VictimSteamId || e.FinalDamage <= 0)
                    return;

                var lifesteal = GetBuff(attacker, BuffKind.Lifesteal, now());
                if (lifesteal is { Value: > 0 } lifestealBuff &&
                    GamePlayers.FindAlive(attacker) is { } player)
                {
                    var healed = PlayerHealth.Heal(
                        player.Pawn,
                        Math.Max(1, (int)Math.Round(e.FinalDamage * lifestealBuff.Value)));

                    if (healed > 0 &&
                        lifestealBuff.SourceSteamId != 0 &&
                        !string.IsNullOrWhiteSpace(lifestealBuff.SourceAbilityId))
                    {
                        events.Publish(new AbilityTelemetryEvent(
                            lifestealBuff.SourceSteamId,
                            lifestealBuff.SourceAbilityId!,
                            AbilityTelemetryKind.Healing,
                            healed));
                    }
                }
            })
        ];

    private Buff? GetBuff(ulong steamId, BuffKind kind, double now)
    {
        if (!_buffs.TryGetValue((steamId, kind), out var buff))
            return null;

        if (now < buff.Until)
            return buff;

        _buffs.Remove((steamId, kind));
        return null;
    }

    private readonly record struct Buff(
        float Value,
        double Until,
        ulong SourceSteamId,
        string? SourceAbilityId);
}
