using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>Passive: heals the attacker for a share of dealt damage. Config: percent.</summary>
internal sealed class VampirismAbility : AbilityHandler
{
    public override string Id => "vampirism";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Восстанавливает здоровье на {percent%} от нанесённого урона.";
    protected override string DisplayName => "Вампиризм";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePostEvent>(OnDamagePost));

    private void OnDamagePost(DamagePostEvent @event)
    {
        if (@event.AttackerSteamId is not { } attacker ||
            attacker == @event.VictimSteamId ||
            @event.FinalDamage <= 0)
        {
            return;
        }

        var ability = GetUsable(attacker);
        if (ability is null)
            return;

        var percent = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "percent"), 0.0, 1.0);
        if (percent <= 0)
            return;

        if (GamePlayers.FindAlive(attacker) is not { } player)
            return;

        var healed = PlayerHealth.Heal(player.Pawn, Math.Max(1, (int)Math.Round(@event.FinalDamage * percent)));
        if (healed > 0)
            Api?.Events.Publish(new AbilityTelemetryEvent(attacker, Id, AbilityTelemetryKind.Healing, healed));
    }
}
