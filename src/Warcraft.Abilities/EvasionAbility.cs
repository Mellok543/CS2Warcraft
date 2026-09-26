using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>Passive: chance to completely avoid damage from players. Config: chance.</summary>
internal sealed class EvasionAbility : AbilityHandler
{
    public override string Id => "evasion";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "Chance to avoid incoming player damage.";
    protected override string DisplayName => "Уклонение";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePreEvent>(OnDamagePre));

    private void OnDamagePre(DamagePreEvent @event)
    {
        if (!@event.AttackerIsPlayer ||
            @event.AttackerSteamId == @event.VictimSteamId ||
            @event.Damage <= 0)
        {
            return;
        }

        var ability = GetUsable(@event.VictimSteamId);
        if (ability is null)
            return;

        var chance = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "chance"), 0.0, 1.0);
        if (Random.Shared.NextDouble() < chance)
            @event.Damage = 0;
    }
}
