using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace Warcraft.Abilities.Game;

/// <summary>
/// info_particle_system entities playing effects from the Warcraft UI addon.
/// Disabled when the server does not mount the addon
/// (configs/warcraft/visuals.json → "particles": false); callers then fall back to beams.
/// Tracks live systems so unload can remove them. Game thread only.
/// </summary>
internal sealed class ParticleEffects(IGameScheduler scheduler, bool enabled) : IDisposable
{
    // Stopped systems keep drawing their live particles until they fade.
    private const float StopFadeSeconds = 1.5f;

    private readonly HashSet<CParticleSystem> _active = [];

    public bool Enabled => enabled;

    /// <param name="lifetimeSeconds">Removal delay; null keeps the system until <see cref="Stop"/>.</param>
    /// <param name="controlPoint1">Line end or data (radius) for effects that read control point 1.</param>
    public CParticleSystem? Play(string effect, Vector3 position, float? lifetimeSeconds, Vector3? controlPoint1 = null)
    {
        if (!enabled)
            return null;

        // CounterStrikeSharp currently exposes m_vServerControlPoints as Span<Vector>,
        // but Vector is a native wrapper rather than a blittable managed value type.
        // Writing its X/Y/Z fields throws at runtime. Returning null here lets EffectKit
        // use the existing beam fallback for line/radius effects without breaking the ability.
        if (controlPoint1 is not null)
            return null;

        var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
        if (particle is null)
            return null;

        particle.EffectName = effect;
        particle.StartActive = true;

        particle.Teleport(new Vector(position.X, position.Y, position.Z), new QAngle(0, 0, 0), new Vector(0, 0, 0));
        particle.DispatchSpawn();
        particle.AcceptInput("Start");
        _active.Add(particle);

        if (lifetimeSeconds is { } lifetime)
            scheduler.Schedule(lifetime, () => Remove(particle));

        return particle;
    }

    /// <summary>Plays the effect at the pawn's feet and parents it so it follows the player.</summary>
    public CParticleSystem? Follow(string effect, CCSPlayerPawn owner, float? lifetimeSeconds = null)
    {
        var origin = owner.AbsOrigin;
        if (origin is null)
            return null;

        var particle = Play(effect, new Vector3(origin.X, origin.Y, origin.Z), lifetimeSeconds);
        particle?.AcceptInput("SetParent", owner, particle, "!activator");
        return particle;
    }

    /// <summary>Stops emitting and removes the entity once the remaining particles faded.</summary>
    public void Stop(CParticleSystem? particle)
    {
        if (particle is null || !particle.IsValid)
        {
            Remove(particle);
            return;
        }

        particle.AcceptInput("Stop");
        scheduler.Schedule(StopFadeSeconds, () => Remove(particle));
    }

    public void Remove(CParticleSystem? particle)
    {
        if (particle is null)
            return;

        _active.Remove(particle);
        if (particle.IsValid)
            particle.Remove();
    }

    /// <summary>Drops wrappers of systems destroyed by a map change (their removal timers stopped).</summary>
    public void PruneInvalid() => _active.RemoveWhere(particle => !particle.IsValid);

    public void Dispose()
    {
        foreach (var particle in _active)
        {
            if (particle.IsValid)
                particle.Remove();
        }

        _active.Clear();
    }
}
