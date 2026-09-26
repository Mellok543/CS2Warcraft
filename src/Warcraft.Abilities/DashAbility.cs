using System.Numerics;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Active: pushes the player horizontally in the look direction.
/// Config: force (units/s), upForce (minimum vertical speed), cooldown.
/// Uses velocity instead of position teleport, so it can never place a player inside a wall.
/// </summary>
internal sealed class DashAbility : AbilityHandler
{
    private const double DefaultForce = 600;
    private const double DefaultUpForce = 200;

    public override string Id => "dash";
    protected override AbilityKind Kind => AbilityKind.Active;
    protected override string Description =>
        "Рывок в сторону взгляда со скоростью {force}. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Рывок";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<AbilityPressedEvent>(OnPressed));

    private void OnPressed(AbilityPressedEvent @event)
    {
        if (!@event.IsFor(Id))
            return;

        if (GamePlayers.FindAlive(@event.SteamId) is not { } player)
        {
            @event.Fail("Вы должны быть живы.");
            return;
        }

        var force = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(@event.Ability, "force", DefaultForce), 0, 3000);
        var upForce = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(@event.Ability, "upForce", DefaultUpForce), 0, 1500);

        var yaw = player.Pawn.EyeAngles.Y * MathF.PI / 180f;
        var current = player.Pawn.AbsVelocity;
        var velocity = new Vector3(
            MathF.Cos(yaw) * force,
            MathF.Sin(yaw) * force,
            Math.Max(current?.Z ?? 0f, upForce));

        player.Pawn.Teleport(velocity: velocity);
        @event.Succeed();
    }
}
