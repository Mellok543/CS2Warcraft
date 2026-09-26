using System.Drawing;
using System.Numerics;

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
internal sealed class TotemSystem(BeamEffects beams)
{
    private const int RingSegments = 12;
    private const float PillarHeight = 90f;

    private readonly Dictionary<(ulong Owner, string AbilityId), Totem> _totems = [];

    public void Place(Totem totem, Color color)
    {
        _totems[(totem.OwnerSteamId, totem.AbilityId)] = totem;

        var lifetime = (float)Math.Max(0.1, totem.Until - totem.NextPulse + totem.Interval);
        var ground = totem.Position with { Z = totem.Position.Z + 4f };
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
                continue;
            }

            if (now < totem.NextPulse)
                continue;

            totem.NextPulse = now + totem.Interval;
            totem.Pulse(totem, now);
        }
    }

    public void Clear() => _totems.Clear();

    private static Vector3 Ring(Vector3 center, float radius, int segment)
    {
        var angle = segment * MathF.Tau / RingSegments;
        return center + new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0);
    }
}
