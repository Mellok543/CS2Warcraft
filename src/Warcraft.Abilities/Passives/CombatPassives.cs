using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Passives;

/// <summary>Passive: flat outgoing damage bonus. Config: percent. With maxHealthPercent it becomes a berserk.</summary>
internal sealed class BonusDamageAbility : AbilityHandler
{
    public override string Id => "bonus_damage";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "Увеличивает наносимый урон на {percent%}.";
    protected override string DisplayName => "Мощь";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePreEvent>(OnDamagePre));

    private void OnDamagePre(DamagePreEvent @event)
    {
        if (@event.IsAbilityDamage ||
            @event.AttackerSteamId is not { } attacker ||
            attacker == @event.VictimSteamId ||
            @event.Damage <= 0)
        {
            return;
        }

        var ability = GetUsable(attacker);
        if (ability is null)
            return;

        var percent = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "percent"), 0.0, 5.0);
        @event.Damage *= (float)(1.0 + percent);
    }
}

/// <summary>Passive: reduces non-player damage of the given kind. Config: percent (1 = immune).</summary>
internal sealed class FallImmunityAbility : AbilityHandler
{
    public override string Id => "fall_immunity";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "Снижает урон от падения на {percent%}.";
    protected override string DisplayName => "Мягкое приземление";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePreEvent>(OnDamagePre));

    private void OnDamagePre(DamagePreEvent @event)
    {
        if ((@event.Kind & DamageKind.Fall) == 0 || @event.Damage <= 0)
            return;

        var ability = GetUsable(@event.VictimSteamId);
        if (ability is null)
            return;

        var percent = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "percent", 1.0), 0.0, 1.0);
        @event.Damage *= (float)(1.0 - percent);
    }
}

/// <summary>
/// Passive: poisons enemies on hit — damage every interval, credited to the attacker.
/// Config: chance (default 1), damage (per tick), ticks, interval (s, default 1).
/// </summary>
internal sealed class PoisonAbility(DamageOverTime dots) : AbilityHandler
{
    public override string Id => "poison";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "С шансом {chance%|1} отравляет врага: {damage} урона каждые {interval|1} с, {ticks|3} раз.";
    protected override string DisplayName => "Яд";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePostEvent>(OnDamagePost));

    private void OnDamagePost(DamagePostEvent @event)
    {
        if (@event.AttackerSteamId is not { } attacker || attacker == @event.VictimSteamId || @event.FinalDamage <= 0)
            return;

        var ability = GetUsable(attacker);
        if (ability is null)
            return;

        if (Random.Shared.NextDouble() >= Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "chance", 1.0), 0, 1))
            return;

        if (GamePlayers.FindAlive(attacker) is not { } source ||
            GamePlayers.FindAlive(@event.VictimSteamId) is not { } victim ||
            !GamePlayers.AreEnemies(source, victim))
        {
            return;
        }

        var ticks = AbilityConfigReader.GetLevelInt(ability, "ticks", 3);
        var interval = AbilityConfigReader.GetLevelDouble(ability, "interval", 1.0);

        dots.Apply(
            attacker,
            victim.Controller.Slot,
            Id,
            AbilityConfigReader.GetLevelInt(ability, "damage"),
            ticks,
            interval,
            Server.CurrentTime);
        Fx?.Status(Id, victim, WarcraftParticles.Body(FxColor.Poison), Math.Max(0.5, ticks * interval));
    }
}
