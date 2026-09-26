using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace Warcraft.Abilities.Game;

/// <summary>Short-lived beam between two points (e.g. chain lightning bolts).</summary>
internal sealed class BeamEffects(IGameScheduler scheduler)
{
    public void Draw(Vector3 from, Vector3 to, Color color, float width, float lifetimeSeconds)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is null)
            return;

        beam.Render = color;
        beam.Width = width;
        beam.EndWidth = width;
        beam.Teleport(new Vector(from.X, from.Y, from.Z), new QAngle(0, 0, 0), new Vector(0, 0, 0));
        beam.EndPos.X = to.X;
        beam.EndPos.Y = to.Y;
        beam.EndPos.Z = to.Z;
        beam.DispatchSpawn();

        scheduler.Schedule(lifetimeSeconds, () =>
        {
            if (beam.IsValid)
                beam.Remove();
        });
    }
}
