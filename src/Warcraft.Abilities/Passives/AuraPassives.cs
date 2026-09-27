using System.Drawing;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Combat;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Passives;

/// <summary>Shared pulse timing for auras (one pulse per owner every <c>interval</c> seconds).</summary>
internal abstract class AuraAbility : AbilityHandler
{
    private readonly Dictionary<ulong, double> _nextPulseAt = [];
    private AuraVisuals? _visuals;

    protected override AbilityKind Kind => AbilityKind.Passive;

    /// <summary>Tint of the ring shown under players with this aura.</summary>
    protected abstract Color AuraColor { get; }

    /// <summary>Colour of the aura's particles.</summary>
    protected abstract FxColor FxColor { get; }

    /// <summary>Particle loop attached to players with this aura.</summary>
    protected virtual string Ambient => WarcraftParticles.Motes(FxColor);

    /// <summary>Pulse period when the race config has no <c>interval</c>.</summary>
    protected virtual double DefaultInterval => 1.0;

    protected override void Subscribe(IWarcraftEventBus events)
    {
        Track(events.Subscribe<GameTickEvent>(OnGameTick));
        Track(events.Subscribe<RoundStartEvent>(_ =>
        {
            _nextPulseAt.Clear();
            _visuals?.Clear();
        }));

        if (Fx is not null)
            _visuals = new AuraVisuals(Fx, AuraColor, Ambient);
    }

    protected override void OnDisposed()
    {
        _nextPulseAt.Clear();
        _visuals?.Clear();
    }

    protected abstract void Pulse(LivePlayer owner, PlayerAbilitySnapshot ability);

    private void OnGameTick(GameTickEvent tick)
    {
        var owners = new List<LivePlayer>();

        foreach (var owner in GamePlayers.AllAliveHumans())
        {
            var steamId = owner.Controller.SteamID;
            if (GetUsable(steamId) is not { } ability)
            {
                _nextPulseAt.Remove(steamId);
                continue;
            }

            owners.Add(owner);

            var interval = Math.Max(0.2, AbilityConfigReader.GetLevelDouble(ability, "interval", DefaultInterval));
            if (!_nextPulseAt.TryGetValue(steamId, out var nextAt) || nextAt - tick.ServerTime > interval)
            {
                _nextPulseAt[steamId] = tick.ServerTime + interval;
                continue;
            }

            if (tick.ServerTime < nextAt)
                continue;

            _nextPulseAt[steamId] = tick.ServerTime + interval;
            Pulse(owner, ability);
        }

        _visuals?.Sync(owners);
    }
}

/// <summary>Passive aura: heals teammates (and the owner) nearby. Config: amount, radius, interval.</summary>
internal sealed class HealAuraAbility : AuraAbility
{
    public override string Id => "heal_aura";
    protected override Color AuraColor => Color.FromArgb(200, 60, 230, 100);
    protected override FxColor FxColor => FxColor.Heal;
    protected override string Description =>
        "Аура: союзники в радиусе {radius} восстанавливают {amount} HP каждые {interval|1} с.";
    protected override string DisplayName => "Аура исцеления";

    protected override void Pulse(LivePlayer owner, PlayerAbilitySnapshot ability)
    {
        var amount = AbilityConfigReader.GetLevelInt(ability, "amount", 2);
        var radius = (float)AbilityConfigReader.GetLevelDouble(ability, "radius", 300);

        foreach (var ally in GamePlayers.AlliesAround(owner, owner.Position, radius))
            PlayerHealth.Heal(ally.Pawn, amount);
    }
}

/// <summary>Passive aura: burns enemies nearby. Config: damage, radius, interval.</summary>
internal sealed class ImmolationAbility : AuraAbility
{
    public override string Id => "immolation";
    protected override Color AuraColor => Color.FromArgb(220, 255, 110, 20);
    protected override FxColor FxColor => FxColor.Fire;
    protected override string Ambient => WarcraftParticles.FlamesRing;
    protected override string Description =>
        "Аура: враги в радиусе {radius} получают {damage} урона каждые {interval|1} с.";
    protected override string DisplayName => "Жертвенный огонь";

    protected override void Pulse(LivePlayer owner, PlayerAbilitySnapshot ability)
    {
        var damage = AbilityConfigReader.GetLevelInt(ability, "damage", 2);
        var radius = (float)AbilityConfigReader.GetLevelDouble(ability, "radius", 200);

        foreach (var enemy in GamePlayers.EnemiesAround(owner, owner.Position, radius).ToArray())
        {
            var result = Api?.Combat.DealAbilityDamage(
                new AbilityDamageRequest(owner.Controller.SteamID, enemy.Controller.Slot, damage, Id));

            if (result is { Applied: true, HealthRemoved: > 0 })
                Fx?.SparksOn(enemy, FxColor.Fire);
        }
    }
}
