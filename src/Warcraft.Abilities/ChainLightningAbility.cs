using System.Drawing;
using System.Numerics;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Ultimate: strikes the nearest enemy within <c>range</c>, then jumps to the
/// nearest not-yet-hit enemy around the previous target up to <c>jumps</c> times.
/// Config: damage, range, jumps, optional damageFalloff (multiplier per jump).
/// Cooldown (<c>cooldown</c>) and all eligibility checks are enforced by Core.
/// </summary>
internal sealed class ChainLightningAbility(BeamEffects beams) : AbilityHandler
{
    private const float ChestHeight = 48f;
    private static readonly Color BoltColor = Color.FromArgb(255, 120, 170, 255);

    public override string Id => "chain_lightning";
    protected override AbilityKind Kind => AbilityKind.Ultimate;
    protected override string Description => "Lightning that jumps between nearby enemies.";
    protected override string DisplayName => "Цепная молния";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<UltimatePressedEvent>(OnUltimatePressed));

    private void OnUltimatePressed(UltimatePressedEvent @event)
    {
        if (!@event.IsFor(Id))
            return;

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

        if (GamePlayers.FindAlive(@event.SteamId) is not { } caster)
        {
            @event.Fail("Вы должны быть живы.");
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
            PlayerHealth.Damage(victim, (int)Math.Round(currentDamage));

            hits++;
            if (hits > jumps)
                break;

            currentDamage *= falloff;
            from = to;
            target = TakeNearest(enemies, from, range);
        }

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
