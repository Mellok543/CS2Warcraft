using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace Warcraft.Abilities.Game;

/// <summary>
/// Non-solid decorative models (totems, roots, shield bubble, aura rings).
/// Disabled when the server does not mount the Warcraft UI addon
/// (configs/warcraft/visuals.json → "models": false); callers then fall back to beams.
/// Tracks live props so unload can remove them. Game thread only.
/// </summary>
internal sealed class PropEffects(IGameScheduler scheduler, bool enabled) : IDisposable
{
    private readonly HashSet<CDynamicProp> _active = [];

    public bool Enabled => enabled;

    public CDynamicProp? Spawn(string model, Vector3 position, float yaw, Color tint, float? lifetimeSeconds = null)
    {
        if (!enabled)
            return null;

        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic_override");
        if (prop is null)
            return null;

        prop.Collision.SolidType = SolidType_t.SOLID_NONE;
        prop.Render = tint;
        prop.DispatchSpawn();
        prop.SetModel(model);
        prop.Teleport(new Vector(position.X, position.Y, position.Z), new QAngle(0, yaw, 0), new Vector(0, 0, 0));
        _active.Add(prop);

        if (lifetimeSeconds is { } lifetime)
            scheduler.Schedule(lifetime, () => Remove(prop));

        return prop;
    }

    /// <summary>Spawns the model at the pawn's feet and parents it so it follows the player.</summary>
    public CDynamicProp? Attach(string model, CCSPlayerPawn owner, Color tint, float? lifetimeSeconds = null)
    {
        var origin = owner.AbsOrigin;
        if (origin is null)
            return null;

        var prop = Spawn(model, new Vector3(origin.X, origin.Y, origin.Z), 0, tint, lifetimeSeconds);
        prop?.AcceptInput("SetParent", owner, prop, "!activator");
        return prop;
    }

    public void Remove(CDynamicProp? prop)
    {
        if (prop is null)
            return;

        _active.Remove(prop);
        if (prop.IsValid)
            prop.Remove();
    }

    /// <summary>Drops wrappers of props destroyed by a map change (their removal timers stopped).</summary>
    public void PruneInvalid() => _active.RemoveWhere(prop => !prop.IsValid);

    public void Dispose()
    {
        foreach (var prop in _active)
        {
            if (prop.IsValid)
                prop.Remove();
        }

        _active.Clear();
    }
}
