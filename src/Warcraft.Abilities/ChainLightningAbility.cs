using System.Drawing;
using System.Numerics;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Combat;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Ultimate: strikes the nearest enemy within <c>range</c>, then jumps to the
/// nearest not-yet-hit enemy around the previous target up to <c>jumps</c> times.
/// Config: damage, range, jumps, optional damageFalloff (multiplier per jump).
/// Cooldown (<c>cooldown</c>) and all eligibility checks are enforced by Core.
/// </summary>
internal sealed class ChainLightningAbility(BeamEffects beams) : ActiveAbilityHandler
{
    private const float ChestHeight = 48f;
    private static readonly Color BoltColor = Color.FromArgb(255, 120, 170, 255);

    public override string Id => "chain_lightning";
    protected override AbilityKind Kind => AbilityKind.Ultimate;
    protected override string Description =>
        "Молния бьёт ближайшего врага в радиусе {range} на {damage} урона и перескакивает ещё на {jumps} целей (урон x{damageFalloff|1} за прыжок). Перезарядка {cooldown} с.";
    protected override string DisplayName => "Цепная молния";

    protected override void Activate(AbilityActivationEvent @event, LivePlayer caster)
    {
        var ability = @event.Ability;
        var damage = Math.Max(0.0, AbilityConfigReader.GetLevelDouble(ability, "damage"));
        var range = (float)Math.Max(0.0, AbilityConfigReader.GetLevelDouble(ability, "range"));
        var jumps = Math.Max(0, AbilityConfigReader.GetLevelInt(ability, "jumps"));
        var falloff = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "damageFalloff", 1.0), 0.0, 1.0);

        if (damage <= 0 || range <= 0)
        {
            @event.Fail("Цепная молния не настроена (damage/range).");
            return;
        }

        var enemies = GamePlayers.AllAlive()
            .Where(x => GamePlayers.AreEnemies(caster, x))
            .ToList();

        var from = caster.Position;
        var target = TakeNearest(enemies, from, range);

        if (target is null)
        {
            @event.Fail($"Нет врагов в радиусе {range:0}.");
            return;
        }

        var hits = 0;
        var currentDamage = damage;

        while (target is { } victim)
        {
            var to = victim.Position;
            beams.Draw(Lift(from), Lift(to), BoltColor, 3f, 0.35f);
            var result = Api?.Combat.DealAbilityDamage(new AbilityDamageRequest(
                @event.SteamId,
                victim.Controller.Slot,
                (int)Math.Round(currentDamage),
                Id));

            if (result is { Applied: true, HealthRemoved: > 0 })
                Api?.Events.Publish(new AbilityTelemetryEvent(@event.SteamId, Id, AbilityTelemetryKind.DamageDealt, result.HealthRemoved));

            if (result is { Killed: true })
                Api?.Events.Publish(new AbilityTelemetryEvent(@event.SteamId, Id, AbilityTelemetryKind.Kill));

            hits++;
            if (hits > jumps)
                break;

            currentDamage *= falloff;
            from = to;
            target = TakeNearest(enemies, from, range);
        }

        Api?.Events.Publish(new AbilityTelemetryEvent(@event.SteamId, Id, AbilityTelemetryKind.TargetsHit, hits));
        @event.Succeed($"Цепная молния поразила целей: {hits}.");
    }

    /// <summary>Removes and returns the closest candidate within range.</summary>
    private static LivePlayer? TakeNearest(List<LivePlayer> candidates, Vector3 origin, float range)
    {
        var bestIndex = -1;
        var bestDistance = range * range;

        for (var i = 0; i < candidates.Count; i++)
        {
            var distance = Vector3.DistanceSquared(origin, candidates[i].Position);
            if (distance > bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        if (bestIndex < 0)
            return null;

        var nearest = candidates[bestIndex];
        candidates.RemoveAt(bestIndex);
        return nearest;
    }

    private static Vector3 Lift(Vector3 position) => position with { Z = position.Z + ChestHeight };
}
