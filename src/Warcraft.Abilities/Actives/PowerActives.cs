using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Combat;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Actives;

/// <summary>Active: heavy single-target strike on the nearest enemy. Config: range, damage, cooldown.</summary>
internal sealed class SmiteAbility(BeamEffects beams) : ActiveAbilityHandler
{
    private static readonly Color BoltColor = Color.FromArgb(255, 255, 230, 120);

    public override string Id => "smite";
    protected override string Description =>
        "Наносит {damage} урона ближайшему врагу в радиусе {range}. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Кара";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var range = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(activation.Ability, "range", 600));
        var damage = AbilityConfigReader.GetLevelInt(activation.Ability, "damage", 40);

        if (GamePlayers.NearestEnemy(caster, range) is not { } target)
        {
            activation.Fail($"Нет врагов в радиусе {range:0}.");
            return;
        }

        Api?.Combat.DealAbilityDamage(new AbilityDamageRequest(activation.SteamId, target.Controller.Slot, damage, Id));
        beams.Draw(Lift(target.Position, 400f), Lift(target.Position, 0f), BoltColor, 5f, 0.3f);
        activation.Succeed($"Кара поразила {target.Controller.PlayerName}.");
    }

    private static Vector3 Lift(Vector3 position, float height) => position with { Z = position.Z + height };
}

/// <summary>
/// Active: temporary self buff — more damage and speed.
/// Config: percent (damage bonus), multiplier (speed), duration, cooldown.
/// </summary>
internal sealed class RageAbility(TeamBuffs buffs, MovementController movement) : ActiveAbilityHandler
{
    public override string Id => "rage";
    protected override string Description =>
        "Ярость на {duration} с: +{percent%} урона и скорость x{multiplier|1}. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Ярость";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var now = Server.CurrentTime;
        var until = now + Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "duration", 5), 0.5, 30);
        var percent = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "percent", 0.3), 0, 3);
        var multiplier = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "multiplier", 1.0), 1, 2);

        buffs.Grant(
            activation.SteamId,
            BuffKind.DamageBonus,
            percent,
            until,
            now,
            activation.SteamId,
            Id);
        if (multiplier > 1f)
            movement.Boost(caster.Controller.Slot, multiplier, until);

        activation.Succeed("Ярость!");
    }
}
