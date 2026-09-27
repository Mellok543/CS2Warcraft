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

/// <summary>
/// Owns all placed totems (one per owner and totem type), pulses them on Core
/// game ticks and draws a pillar and radius ring when placed. Game thread only.
/// </summary>
internal sealed class TotemSystem(BeamEffects beams, PropEffects props)
{
    private const int RingSegments = 12;
    private const float PillarHeight = 90f;

    private readonly Dictionary<(ulong Owner, string AbilityId), Totem> _totems = [];
    private readonly Dictionary<(ulong Owner, string AbilityId), CDynamicProp> _models = [];

    /// <param name="model">Totem model; without it (or with models disabled) a beam pillar is drawn.</param>
    public void Place(Totem totem, Color color, string? model = null, float yaw = 0)
    {
        var key = (totem.OwnerSteamId, totem.AbilityId);
        _totems[key] = totem;
        RemoveModel(key);

        var lifetime = (float)Math.Max(0.1, totem.Until - totem.NextPulse + totem.Interval);
        var ground = totem.Position with { Z = totem.Position.Z + 4f };

        var prop = model is null ? null : props.Spawn(model, totem.Position, yaw, Color.White, lifetime);
        if (prop is not null)
            _models[key] = prop;
        else
            beams.Draw(ground, ground with { Z = ground.Z + PillarHeight }, color, 4f, lifetime);

        for (var i = 0; i < RingSegments; i++)
        {
            beams.Draw(Ring(ground, totem.Radius, i), Ring(ground, totem.Radius, i + 1), color, 1.5f, lifetime);
        }
    }

    public void Update(double now)
    {
        foreach (var (key, totem) in _totems.ToArray())
        {
            if (now >= totem.Until)
            {
                _totems.Remove(key);
                RemoveModel(key);
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
        foreach (var key in _models.Keys.ToArray())
            RemoveModel(key);
    }

    private void RemoveModel((ulong Owner, string AbilityId) key)
    {
        if (_models.Remove(key, out var prop))
            props.Remove(prop);
    }

    private static Vector3 Ring(Vector3 center, float radius, int segment)
    {
        var angle = segment * MathF.Tau / RingSegments;
        return center + new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0);
    }
}
