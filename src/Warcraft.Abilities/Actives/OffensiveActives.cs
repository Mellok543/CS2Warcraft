using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Combat;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Actives;

/// <summary>
/// Active: damages and stuns every enemy around the caster.
/// Config: radius, damage, stun (seconds), cooldown.
/// </summary>
internal sealed class WarStompAbility(MovementController movement) : ActiveAbilityHandler
{
    public override string Id => "war_stomp";
    protected override string Description =>
        "Удар о землю: {damage} урона и оглушение на {stun} с всем врагам в радиусе {radius}. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Громовая поступь";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var radius = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(activation.Ability, "radius", 300));
        var damage = AbilityConfigReader.GetLevelInt(activation.Ability, "damage");
        var stun = Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "stun", 1.0), 0, 5);

        var targets = GamePlayers.EnemiesAround(caster, caster.Position, radius).ToArray();
        if (targets.Length == 0)
        {
            activation.Fail($"Нет врагов в радиусе {radius:0}.");
            return;
        }

        var until = Server.CurrentTime + stun;
        foreach (var target in targets)
        {
            if (stun > 0)
                movement.Stun(target, 0f, until);

            Api?.Combat.DealAbilityDamage(
                new AbilityDamageRequest(activation.SteamId, target.Controller.Slot, damage, Id));
        }

        activation.Succeed($"Оглушено врагов: {targets.Length}.");
    }
}

/// <summary>
/// Active: roots the nearest enemy and damages it every second.
/// Config: range, duration (s), damage (per second), cooldown.
/// </summary>
internal sealed class EntangleAbility(MovementController movement, DamageOverTime dots, BeamEffects beams) : ActiveAbilityHandler
{
    private static readonly Color RootColor = Color.FromArgb(255, 60, 200, 60);

    public override string Id => "entangle";
    protected override string Description =>
        "Опутывает ближайшего врага в радиусе {range} на {duration} с, нанося {damage} урона в секунду. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Опутывающие корни";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var range = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(activation.Ability, "range", 600));
        var duration = Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "duration", 2), 0.1, 10);
        var damage = AbilityConfigReader.GetLevelInt(activation.Ability, "damage");

        if (GamePlayers.NearestEnemy(caster, range) is not { } target)
        {
            activation.Fail($"Нет врагов в радиусе {range:0}.");
            return;
        }

        var now = Server.CurrentTime;
        movement.Stun(target, 0f, now + duration);
        dots.Apply(activation.SteamId, target.Controller.Slot, Id, damage, (int)Math.Ceiling(duration), 1.0, now);
        beams.Draw(Chest(caster.Position), Chest(target.Position), RootColor, 2f, 0.5f);

        activation.Succeed($"{target.Controller.PlayerName} опутан.");
    }

    private static Vector3 Chest(Vector3 position) => position with { Z = position.Z + 40f };
}

/// <summary>
/// Active: drains health from the nearest enemy.
/// Config: range, damage, healPercent (share of dealt damage returned, default 1), cooldown.
/// </summary>
internal sealed class LifeDrainAbility(BeamEffects beams) : ActiveAbilityHandler
{
    private static readonly Color DrainColor = Color.FromArgb(255, 200, 30, 40);

    public override string Id => "life_drain";
    protected override string Description =>
        "Высасывает {damage} HP у ближайшего врага в радиусе {range} и лечит вас. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Похищение жизни";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var range = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(activation.Ability, "range", 500));
        var damage = AbilityConfigReader.GetLevelInt(activation.Ability, "damage", 25);
        var healPercent = Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "healPercent", 1.0), 0, 5);

        if (GamePlayers.NearestEnemy(caster, range) is not { } target || Api is null)
        {
            activation.Fail($"Нет врагов в радиусе {range:0}.");
            return;
        }

        var result = Api.Combat.DealAbilityDamage(
            new AbilityDamageRequest(activation.SteamId, target.Controller.Slot, damage, Id));

        if (!result.Applied)
        {
            activation.Fail("Не удалось похитить жизнь.");
            return;
        }

        PlayerHealth.Heal(caster.Pawn, (int)Math.Round(result.HealthRemoved * healPercent));
        beams.Draw(Chest(target.Position), Chest(caster.Position), DrainColor, 2.5f, 0.4f);
        activation.Succeed($"Похищено {result.HealthRemoved} HP.");
    }

    private static Vector3 Chest(Vector3 position) => position with { Z = position.Z + 40f };
}
