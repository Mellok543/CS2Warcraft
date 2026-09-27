using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API.Core;

namespace Warcraft.Abilities.Game;

/// <summary>A placed totem: fixed position, team, radius and a periodic effect.</summary>
internal sealed class Totem
{
    public required ulong OwnerSteamId { get; init; }
    public required byte Team { get; init; }
    public required string AbilityId { get; init; }
    public required Vector3 Position { get; init; }
    public required float Radius { get; init; }
    public required double Until { get; init; }
    public required double Interval { get; init; }
    public required Action<Totem, double> Pulse { get; init; }
    public double NextPulse { get; set; }

    public IEnumerable<LivePlayer> Allies()
        => GamePlayers.AllAlive().Where(x => x.Team == Team && Vector3.Distance(Position, x.Position) <= Radius);

    public IEnumerable<LivePlayer> Enemies()
        => GamePlayers.AllAlive().Where(x => x.Team != Team && Vector3.Distance(Position, x.Position) <= Radius);
}

/// <summary>Looping effect placed with a totem, <see cref="Height"/> units above its base.</summary>
internal readonly record struct TotemAmbient(string Effect, float Height = 0);

/// <summary>
/// Owns all placed totems (one per owner and totem type), pulses them on Core
/// game ticks and shows the model, its area and ambient particles while it
/// stands (beam pillar and ring without the addon). Game thread only.
/// </summary>
internal sealed class TotemSystem(EffectKit fx)
{
    private const int RingSegments = 12;
    private const float PillarHeight = 90f;

    private readonly Dictionary<(ulong Owner, string AbilityId), Totem> _totems = [];
    private readonly Dictionary<(ulong Owner, string AbilityId), Visuals> _visuals = [];

    /// <param name="model">Totem model; without it (or with models disabled) a beam pillar is drawn.</param>
    public void Place(
        Totem totem,
        Color color,
        FxColor fxColor,
        string? model = null,
        float yaw = 0,
        IReadOnlyList<TotemAmbient>? ambient = null)
    {
        var key = (totem.OwnerSteamId, totem.AbilityId);
        _totems[key] = totem;
        RemoveVisuals(key);

        var lifetime = (float)Math.Max(0.1, totem.Until - totem.NextPulse + totem.Interval);
        var ground = totem.Position with { Z = totem.Position.Z + 4f };
        var visuals = new Visuals();

        visuals.Model = model is null ? null : fx.Props.Spawn(model, totem.Position, yaw, Color.White, lifetime);
        if (visuals.Model is null)
            fx.Beams.Draw(ground, ground with { Z = ground.Z + PillarHeight }, color, 4f, lifetime);

        var area = fx.Particles.Play(
            WarcraftParticles.Radius(fxColor),
            ground,
            lifetime,
            new Vector3(totem.Radius, 0, 0));

        if (area is not null)
            visuals.Particles.Add(area);
        else
        {
            for (var i = 0; i < RingSegments; i++)
                fx.Beams.Draw(Ring(ground, totem.Radius, i), Ring(ground, totem.Radius, i + 1), color, 1.5f, lifetime);
        }

        foreach (var (effect, height) in ambient ?? [])
        {
            if (fx.Particles.Play(effect, totem.Position with { Z = totem.Position.Z + height }, lifetime) is { } particle)
                visuals.Particles.Add(particle);
        }

        fx.Nova(ground, fxColor);
        _visuals[key] = visuals;
    }

    /// <summary>Turns the totem model towards <paramref name="target"/> (turrets).</summary>
    /// <param name="modelYawOffset">Yaw at which the model faces +X.</param>
    public void Face(Totem totem, Vector3 target, float modelYawOffset)
    {
        if (!_visuals.TryGetValue((totem.OwnerSteamId, totem.AbilityId), out var visuals) ||
            visuals.Model is not { IsValid: true } model)
        {
            return;
        }

        var yaw = MathF.Atan2(target.Y - totem.Position.Y, target.X - totem.Position.X) * 180f / MathF.PI;
        model.Teleport(angles: new Vector3(0, yaw + modelYawOffset, 0));
    }

    public void Update(double now)
    {
        foreach (var (key, totem) in _totems.ToArray())
        {
            if (now >= totem.Until)
            {
                _totems.Remove(key);
                RemoveVisuals(key);
                continue;
            }

            if (now < totem.NextPulse)
                continue;

            totem.NextPulse = now + totem.Interval;
            totem.Pulse(totem, now);
        }
    }

    public void Clear()
    {
        _totems.Clear();
        foreach (var key in _visuals.Keys.ToArray())
            RemoveVisuals(key);
    }

    private void RemoveVisuals((ulong Owner, string AbilityId) key)
    {
        if (!_visuals.Remove(key, out var visuals))
            return;

        fx.Props.Remove(visuals.Model);
        foreach (var particle in visuals.Particles)
            fx.Particles.Stop(particle);
    }

    private static Vector3 Ring(Vector3 center, float radius, int segment)
    {
        var angle = segment * MathF.Tau / RingSegments;
        return center + new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0);
    }

    private sealed class Visuals
    {
        public CDynamicProp? Model { get; set; }
        public List<CParticleSystem> Particles { get; } = [];
    }
}
