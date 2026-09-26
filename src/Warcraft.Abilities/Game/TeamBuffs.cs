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

    public void Grant(ulong steamId, BuffKind kind, float value, double until, double now)
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

        _buffs[key] = new Buff(value, until);
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

                if (e.AttackerSteamId is { } attacker && attacker != e.VictimSteamId)
                    e.Damage *= 1f + Get(attacker, BuffKind.DamageBonus, now());

                e.Damage *= 1f - Math.Min(MaxReduction, Get(e.VictimSteamId, BuffKind.DamageReduction, now()));
            }),
            events.Subscribe<DamagePostEvent>(e =>
            {
                if (e.AttackerSteamId is not { } attacker || attacker == e.VictimSteamId || e.FinalDamage <= 0)
                    return;

                var lifesteal = Get(attacker, BuffKind.Lifesteal, now());
                if (lifesteal > 0 && GamePlayers.FindAlive(attacker) is { } player)
                    PlayerHealth.Heal(player.Pawn, Math.Max(1, (int)Math.Round(e.FinalDamage * lifesteal)));
            })
        ];

    private readonly record struct Buff(float Value, double Until);
}
