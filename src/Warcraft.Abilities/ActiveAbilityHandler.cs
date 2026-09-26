using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Base for activatable mechanics. Handles both <see cref="AbilityPressedEvent"/>
/// and <see cref="UltimatePressedEvent"/>, so a race may put the same mechanic in
/// an ability slot or in its ultimate. Core has already checked level, unlock,
/// conditions and cooldown; the handler only reports success or failure.
/// </summary>
internal abstract class ActiveAbilityHandler : AbilityHandler
{
    protected override AbilityKind Kind => AbilityKind.Active;

    protected sealed override void Subscribe(IWarcraftEventBus events)
    {
        Track(events.Subscribe<AbilityPressedEvent>(Handle));
        Track(events.Subscribe<UltimatePressedEvent>(Handle));
        SubscribeExtra(events);
    }

    protected virtual void SubscribeExtra(IWarcraftEventBus events)
    {
    }

    protected abstract void Activate(AbilityActivationEvent activation, LivePlayer caster);

    private void Handle(AbilityActivationEvent activation)
    {
        if (!activation.IsFor(Id))
            return;

        if (GamePlayers.FindAlive(activation.SteamId) is not { } caster)
        {
            activation.Fail("Вы должны быть живы.");
            return;
        }

        Activate(activation, caster);
    }
}
