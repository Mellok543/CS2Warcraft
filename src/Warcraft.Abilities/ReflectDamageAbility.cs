using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Combat;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Passive: returns a share of received damage to the attacking player.
/// Config: percent, optional maxDamage (cap per hit).
/// </summary>
internal sealed class ReflectDamageAbility : AbilityHandler
{
    public override string Id => "reflect_damage";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Возвращает атакующему {percent%} полученного урона.";
    protected override string DisplayName => "Отражение урона";

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

        var ability = GetUsable(@event.VictimSteamId);
        if (ability is null)
            return;

        var percent = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "percent"), 0.0, 1.0);
        var reflected = (int)Math.Round(@event.FinalDamage * percent);

        var cap = AbilityConfigReader.GetLevelInt(ability, "maxDamage");
        if (cap > 0)
            reflected = Math.Min(reflected, cap);

        if (reflected <= 0)
            return;

        if (GamePlayers.FindAlive(attacker) is { } target)
        {
            var result = Api?.Combat.DealAbilityDamage(new AbilityDamageRequest(
                @event.VictimSteamId,
                target.Controller.Slot,
                reflected,
                Id));

            if (result is { Applied: true, HealthRemoved: > 0 })
            {
                Api?.Events.Publish(new AbilityTelemetryEvent(@event.VictimSteamId, Id, AbilityTelemetryKind.DamageDealt, result.HealthRemoved));
                if (Fx is { } fx && fx.Throttle(Id, @event.VictimSteamId, 0.25))
                    fx.SparksOn(target, FxColor.Holy);
            }

            if (result is { Killed: true })
                Api?.Events.Publish(new AbilityTelemetryEvent(@event.VictimSteamId, Id, AbilityTelemetryKind.Kill));
        }
    }
}
