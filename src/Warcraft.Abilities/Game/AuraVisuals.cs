using System.Drawing;
using CounterStrikeSharp.API.Core;

namespace Warcraft.Abilities.Game;

/// <summary>
/// A glowing ring under each player whose aura is active, tinted per aura.
/// One instance per aura ability; <see cref="Sync"/> is called every aura tick
/// with the owners that currently have the aura, everything else is removed
/// (death, lost condition, race change). Game thread only.
/// </summary>
internal sealed class AuraVisuals(PropEffects props, Color color)
{
    private readonly Dictionary<int, (CCSPlayerPawn Pawn, CDynamicProp Ring)> _rings = [];

    public void Sync(IReadOnlyCollection<LivePlayer> owners)
    {
        if (!props.Enabled)
            return;

        var active = new HashSet<int>();

        foreach (var owner in owners)
        {
            var slot = owner.Controller.Slot;
            active.Add(slot);

            // A respawn gives the player a new pawn: re-attach to it.
            if (_rings.TryGetValue(slot, out var existing) &&
                existing.Ring.IsValid &&
                existing.Pawn.Handle == owner.Pawn.Handle)
            {
                continue;
            }

            if (_rings.Remove(slot, out var stale))
                props.Remove(stale.Ring);

            if (props.Attach(WarcraftModels.AuraRing, owner.Pawn, color) is { } ring)
                _rings[slot] = (owner.Pawn, ring);
        }

        foreach (var slot in _rings.Keys.Where(x => !active.Contains(x)).ToArray())
        {
            props.Remove(_rings[slot].Ring);
            _rings.Remove(slot);
        }
    }

    public void Clear()
    {
        foreach (var (_, ring) in _rings.Values)
            props.Remove(ring);

        _rings.Clear();
    }
}
