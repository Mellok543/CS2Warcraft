using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Actives;

/// <summary>
/// Active: heals the caster and teammates within radius.
/// Config: amount, radius (0 = self only), cooldown.
/// </summary>
internal sealed class HealBurstAbility : ActiveAbilityHandler
{
    public override string Id => "heal_burst";
    protected override string Description =>
        "Исцеляет вас и союзников в радиусе {radius|0} на {amount} HP. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Святой свет";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var amount = AbilityConfigReader.GetLevelInt(activation.Ability, "amount", 30);
        var radius = (float)AbilityConfigReader.GetLevelDouble(activation.Ability, "radius");

        var targets = radius > 0
            ? GamePlayers.AlliesAround(caster, caster.Position, radius).ToArray()
            : [caster];

        var healed = 0;
        var restored = 0;
        foreach (var target in targets.Where(x => x.Pawn.Health < x.Pawn.MaxHealth))
        {
            var actual = PlayerHealth.Heal(target.Pawn, amount);
            if (actual <= 0)
                continue;

            healed++;
            restored += actual;
        }

        if (restored > 0)
            Api?.Events.Publish(new AbilityTelemetryEvent(activation.SteamId, Id, AbilityTelemetryKind.Healing, restored));

        if (healed == 0)
            activation.Fail("Все уже здоровы.");
        else
            activation.Succeed($"Исцелено: {healed}.");
    }
}

/// <summary>
/// Active: temporary damage reduction (1 = invulnerable).
/// Config: duration (s), percent (default 1), cooldown.
/// </summary>
internal sealed class DivineShieldAbility : ActiveAbilityHandler
{
    private readonly Dictionary<ulong, Shield> _shields = [];

    public override string Id => "divine_shield";
    protected override string Description =>
        "На {duration} с снижает получаемый урон на {percent%|1}. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Божественный щит";

    protected override void SubscribeExtra(IWarcraftEventBus events)
    {
        Track(events.Subscribe<DamagePreEvent>(OnDamagePre));
        Track(events.Subscribe<RoundStartEvent>(_ => _shields.Clear()));
    }

    protected override void OnDisposed() => _shields.Clear();

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var duration = Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "duration", 3), 0.1, 30);
        var percent = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "percent", 1.0), 0, 1);

        _shields[activation.SteamId] = new Shield(Server.CurrentTime + duration, percent);
        activation.Succeed($"Щит активен {duration:0.#} с.");
    }

    private void OnDamagePre(DamagePreEvent @event)
    {
        if (!_shields.TryGetValue(@event.VictimSteamId, out var shield))
            return;

        if (Server.CurrentTime >= shield.Until)
        {
            _shields.Remove(@event.VictimSteamId);
            return;
        }

        var before = @event.Damage;
        @event.Damage *= 1f - shield.Percent;
        var prevented = Math.Max(0, (long)Math.Round(before - @event.Damage));
        if (prevented > 0)
            Api?.Events.Publish(new AbilityTelemetryEvent(@event.VictimSteamId, Id, AbilityTelemetryKind.DamagePrevented, prevented));
    }

    private readonly record struct Shield(double Until, float Percent);
}

/// <summary>Active: temporary speed boost. Config: multiplier, duration (s), cooldown.</summary>
internal sealed class SprintAbility(MovementController movement) : ActiveAbilityHandler
{
    public override string Id => "sprint";
    protected override string Description =>
        "На {duration} с увеличивает скорость в x{multiplier}. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Спринт";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var multiplier = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "multiplier", 1.4), 1.0, 3.0);
        var duration = Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "duration", 4), 0.1, 30);

        movement.Boost(caster.Controller.Slot, multiplier, Server.CurrentTime + duration);
        activation.Succeed();
    }
}
