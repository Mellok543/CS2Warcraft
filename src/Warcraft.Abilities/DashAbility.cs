using System.Numerics;
using Warcraft.Abilities.Game;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Active: pushes the player horizontally in the look direction.
/// Config: force (units/s), upForce (minimum vertical speed), cooldown.
/// Uses velocity instead of position teleport, so it can never place a player inside a wall.
/// </summary>
internal sealed class DashAbility : ActiveAbilityHandler
{
    private const double DefaultForce = 600;
    private const double DefaultUpForce = 200;

    public override string Id => "dash";
    protected override string Description =>
        "Рывок в сторону взгляда со скоростью {force}. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Рывок";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var force = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "force", DefaultForce), 0, 3000);
        var upForce = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "upForce", DefaultUpForce), 0, 1500);

        var yaw = caster.Pawn.EyeAngles.Y * MathF.PI / 180f;
        var current = caster.Pawn.AbsVelocity;
        var velocity = new Vector3(
            MathF.Cos(yaw) * force,
            MathF.Sin(yaw) * force,
            Math.Max(current?.Z ?? 0f, upForce));

        caster.Pawn.Teleport(velocity: velocity);
        activation.Succeed();
    }
}
