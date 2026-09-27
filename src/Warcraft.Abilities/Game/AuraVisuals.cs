using System.Drawing;
using CounterStrikeSharp.API.Core;

namespace Warcraft.Abilities.Game;

/// <summary>
/// A glowing ring and a particle loop on each player whose aura is active, tinted per aura.
/// One instance per aura ability; <see cref="Sync"/> is called every aura tick
/// with the owners that currently have the aura, everything else is removed
/// (death, lost condition, race change). Game thread only.
/// </summary>
internal sealed class AuraVisuals(EffectKit fx, Color color, string ambient)
{
    private readonly Dictionary<int, Attached> _attached = [];

    public void Sync(IReadOnlyCollection<LivePlayer> owners)
    {
        if (!fx.Props.Enabled && !fx.Particles.Enabled)
            return;

        var active = new HashSet<int>();

        foreach (var owner in owners)
        {
            var slot = owner.Controller.Slot;
            active.Add(slot);

            // A respawn gives the player a new pawn: re-attach to it.
            if (_attached.TryGetValue(slot, out var existing) &&
                existing.Pawn.Handle == owner.Pawn.Handle &&
                existing.Ring?.IsValid != false &&
                existing.Particle?.IsValid != false)
            {
                continue;
            }

            if (_attached.Remove(slot, out var stale))
                Detach(stale);

            _attached[slot] = new Attached(
                owner.Pawn,
                fx.Props.Attach(WarcraftModels.AuraRing, owner.Pawn, color),
                fx.Particles.Follow(ambient, owner.Pawn));
        }

        foreach (var slot in _attached.Keys.Where(x => !active.Contains(x)).ToArray())
        {
            Detach(_attached[slot]);
            _attached.Remove(slot);
        }
    }

    public void Clear()
    {
        foreach (var attached in _attached.Values)
            Detach(attached);

        _attached.Clear();
    }

    private void Detach(Attached attached)
    {
        fx.Props.Remove(attached.Ring);
        fx.Particles.Stop(attached.Particle);
    }

    private readonly record struct Attached(CCSPlayerPawn Pawn, CDynamicProp? Ring, CParticleSystem? Particle);
}
