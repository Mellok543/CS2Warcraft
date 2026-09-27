using System.Drawing;
using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Combat;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Actives;

/// <summary>
/// Base for totems: placed at the caster's feet, pulse every <c>interval</c>
/// seconds for <c>duration</c> seconds within <c>radius</c>. One totem of each
/// type per player; re-casting moves it. Common config: radius, duration, interval, cooldown.
/// </summary>
internal abstract class TotemAbility(TotemSystem totems) : ActiveAbilityHandler
{
    protected abstract Color Color { get; }

    /// <summary>Model placed for this totem (see <see cref="WarcraftModels"/>).</summary>
    protected abstract string Model { get; }

    protected abstract void Pulse(Totem totem, PlayerAbilitySnapshot ability, double now);

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var ability = activation.Ability;
        var now = Server.CurrentTime;
        var interval = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "interval", 1.0), 0.25, 10);

        totems.Place(
            new Totem
            {
                OwnerSteamId = activation.SteamId,
                Team = caster.Team,
                AbilityId = Id,
                Position = caster.Position,
                Radius = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "radius", 300), 50, 2000),
                Until = now + Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "duration", 10), 1, 60),
                Interval = interval,
                NextPulse = now,
                Pulse = (totem, time) => Pulse(totem, ability, time)
            },
            Color,
            Model,
            caster.Pawn.EyeAngles.Y);

        Api?.Events.Publish(new AbilityTelemetryEvent(
            activation.SteamId,
            Id,
            AbilityTelemetryKind.TotemPlaced));
        activation.Succeed();
    }
}

/// <summary>Totem: heals teammates in range. Config: amount (+ common).</summary>
internal sealed class HealingTotemAbility(TotemSystem totems) : TotemAbility(totems)
{
    public override string Id => "healing_totem";
    protected override string Model => WarcraftModels.TotemHealing;
    protected override string Description =>
        "Тотем на {duration} с: союзники в радиусе {radius} восстанавливают {amount} HP каждые {interval|1} с. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Тотем исцеления";
    protected override Color Color => Color.FromArgb(255, 60, 220, 90);

    protected override void Pulse(Totem totem, PlayerAbilitySnapshot ability, double now)
    {
        var amount = AbilityConfigReader.GetLevelInt(ability, "amount", 5);
        var restored = 0;
        foreach (var ally in totem.Allies())
            restored += PlayerHealth.Heal(ally.Pawn, amount);

        if (restored > 0)
            Api?.Events.Publish(new AbilityTelemetryEvent(
                totem.OwnerSteamId,
                Id,
                AbilityTelemetryKind.Healing,
                restored));
    }
}

/// <summary>Totem: burns enemies in range (credited to the owner). Config: damage (+ common).</summary>
internal sealed class FlameTotemAbility(TotemSystem totems) : TotemAbility(totems)
{
    public override string Id => "flame_totem";
    protected override string Model => WarcraftModels.TotemFlame;
    protected override string Description =>
        "Тотем на {duration} с: враги в радиусе {radius} получают {damage} урона каждые {interval|1} с. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Тотем пламени";
    protected override Color Color => Color.FromArgb(255, 255, 110, 20);

    protected override void Pulse(Totem totem, PlayerAbilitySnapshot ability, double now)
    {
        var damage = AbilityConfigReader.GetLevelInt(ability, "damage", 5);
        foreach (var enemy in totem.Enemies().ToArray())
        {
            var result = Api?.Combat.DealAbilityDamage(
                new AbilityDamageRequest(totem.OwnerSteamId, enemy.Controller.Slot, damage, Id));

            if (result is { Applied: true, HealthRemoved: > 0 })
                Api?.Events.Publish(new AbilityTelemetryEvent(
                    totem.OwnerSteamId,
                    Id,
                    AbilityTelemetryKind.DamageDealt,
                    result.HealthRemoved));

            if (result is { Killed: true })
                Api?.Events.Publish(new AbilityTelemetryEvent(
                    totem.OwnerSteamId,
                    Id,
                    AbilityTelemetryKind.Kill));
        }
    }
}

/// <summary>Totem: slows enemies in range. Config: slow (movement multiplier) (+ common).</summary>
internal sealed class FrostTotemAbility(TotemSystem totems, MovementController movement) : TotemAbility(totems)
{
    public override string Id => "frost_totem";
    protected override string Model => WarcraftModels.TotemFrost;
    protected override string Description =>
        "Тотем на {duration} с: враги в радиусе {radius} замедлены до x{slow}. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Тотем холода";
    protected override Color Color => Color.FromArgb(255, 110, 200, 255);

    protected override void Pulse(Totem totem, PlayerAbilitySnapshot ability, double now)
    {
        var slow = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "slow", 0.6), 0, 1);
        foreach (var enemy in totem.Enemies())
            movement.Stun(enemy, slow, now + totem.Interval + 0.2);
    }
}

/// <summary>Base for totems granting a team buff to teammates in range. Config: percent (+ common).</summary>
internal abstract class BuffTotemAbility(TotemSystem totems, TeamBuffs buffs, BuffKind kind) : TotemAbility(totems)
{
    protected override void Pulse(Totem totem, PlayerAbilitySnapshot ability, double now)
    {
        var value = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(ability, "percent"));
        foreach (var ally in totem.Allies().Where(x => !x.Controller.IsBot))
            buffs.Grant(
                ally.Controller.SteamID,
                kind,
                value,
                now + totem.Interval + 0.2,
                now,
                totem.OwnerSteamId,
                Id);
    }
}

/// <summary>Totem: teammates in range deal more damage.</summary>
internal sealed class WarTotemAbility(TotemSystem totems, TeamBuffs buffs)
    : BuffTotemAbility(totems, buffs, BuffKind.DamageBonus)
{
    public override string Id => "war_totem";
    protected override string Model => WarcraftModels.TotemWar;
    protected override string Description =>
        "Тотем на {duration} с: союзники в радиусе {radius} наносят на {percent%} больше урона. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Тотем войны";
    protected override Color Color => Color.FromArgb(255, 220, 40, 40);
}

/// <summary>Totem: teammates in range take less damage.</summary>
internal sealed class ShieldTotemAbility(TotemSystem totems, TeamBuffs buffs)
    : BuffTotemAbility(totems, buffs, BuffKind.DamageReduction)
{
    public override string Id => "shield_totem";
    protected override string Model => WarcraftModels.TotemShield;
    protected override string Description =>
        "Тотем на {duration} с: союзники в радиусе {radius} получают на {percent%} меньше урона. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Тотем защиты";
    protected override Color Color => Color.FromArgb(255, 240, 210, 80);
}
