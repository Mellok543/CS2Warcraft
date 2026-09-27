using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace Warcraft.Abilities.Game;

/// <summary>Short-lived beam between two points (e.g. chain lightning bolts).</summary>
internal sealed class BeamEffects(IGameScheduler scheduler) : IDisposable
{
    // Removal timers die with the plugin, so live beams are tracked for unload.
    private readonly HashSet<CBeam> _active = [];

    public void Draw(
        Vector3 from,
        Vector3 to,
        Color color,
        float width,
        float lifetimeSeconds,
        float amplitude = 0f)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is null)
            return;

        beam.Render = color;
        beam.Width = width;
        beam.EndWidth = width;
        beam.Amplitude = Math.Max(0f, amplitude);
        beam.Teleport(new Vector(from.X, from.Y, from.Z), new QAngle(0, 0, 0), new Vector(0, 0, 0));
        beam.EndPos.X = to.X;
        beam.EndPos.Y = to.Y;
        beam.EndPos.Z = to.Z;
        beam.DispatchSpawn();
        _active.Add(beam);

        scheduler.Schedule(lifetimeSeconds, () =>
        {
            _active.Remove(beam);
            if (beam.IsValid)
                beam.Remove();
        });
    }

    /// <summary>Drops wrappers of beams already destroyed by a map change (their timers stopped).</summary>
    public void PruneInvalid() => _active.RemoveWhere(beam => !beam.IsValid);

    public void Dispose()
    {
        foreach (var beam in _active)
        {
            if (beam.IsValid)
                beam.Remove();
        }

        _active.Clear();
    }
}
