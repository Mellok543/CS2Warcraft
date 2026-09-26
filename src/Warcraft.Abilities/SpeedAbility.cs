using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>Passive: movement speed multiplier. Config: multiplier (e.g. 1.10).</summary>
internal sealed class SpeedAbility(MovementController movement) : AbilityHandler
{
    private const float MaxMultiplier = 3.0f;

    public override string Id => "speed";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Скорость передвижения x{multiplier}.";
    protected override string DisplayName => "Скорость";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<GameTickEvent>(_ => Refresh()));

    private void Refresh()
    {
        foreach (var player in GamePlayers.AllAliveHumans())
        {
            var ability = GetUsable(player.Controller.SteamID);
            float? multiplier = ability is null
                ? null
                : Math.Min((float)AbilityConfigReader.GetLevelDouble(ability, "multiplier", 1.0), MaxMultiplier);

            movement.SetPassive(player.Controller.Slot, multiplier);
        }
    }
}
