using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>Passive: chance to multiply outgoing damage. Config: chance, damageMultiplier.</summary>
internal sealed class CriticalStrikeAbility : AbilityHandler
{
    public override string Id => "critical_strike";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "С шансом {chance%} наносит x{damageMultiplier} урона.";
    protected override string DisplayName => "Критический удар";

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

        var chance = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "chance"), 0.0, 1.0);
        if (Random.Shared.NextDouble() >= chance)
            return;

        var multiplier = Math.Max(1.0, AbilityConfigReader.GetLevelDouble(ability, "damageMultiplier", 1.0));
        @event.Damage *= (float)multiplier;

        if (Fx is { } fx && fx.Throttle(Id, attacker, 0.15) && GamePlayers.FindAlive(@event.VictimSteamId) is { } victim)
            fx.SparksOn(victim, FxColor.War);
    }
}
