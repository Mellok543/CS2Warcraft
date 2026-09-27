using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>Passive: reduces incoming damage. Config: percent (capped at 90%).</summary>
internal sealed class DamageReductionAbility : AbilityHandler
{
    private const double MaxReduction = 0.9;

    public override string Id => "damage_reduction";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Снижает получаемый урон на {percent%}.";
    protected override string DisplayName => "Щит";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePreEvent>(OnDamagePre));

    private void OnDamagePre(DamagePreEvent @event)
    {
        if (@event.Damage <= 0 || @event.AttackerSteamId == @event.VictimSteamId)
            return;

        var ability = GetUsable(@event.VictimSteamId);
        if (ability is null)
            return;

        var percent = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "percent"), 0.0, MaxReduction);
        var before = @event.Damage;
        @event.Damage *= (float)(1.0 - percent);
        var prevented = Math.Max(0, (long)Math.Round(before - @event.Damage));
        if (prevented > 0)
            Api?.Events.Publish(new AbilityTelemetryEvent(@event.VictimSteamId, Id, AbilityTelemetryKind.DamagePrevented, prevented));
    }
}
