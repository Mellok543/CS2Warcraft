using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Warcraft.Abilities.Game;

/// <summary>
/// Visual vocabulary shared by all ability handlers: one-shot particle effects,
/// rays with beam fallbacks and per-player status effects. Every call is a no-op
/// (or a beam) when particles are disabled. Game thread only.
/// </summary>
internal sealed class EffectKit(IGameScheduler scheduler, BeamEffects beams, PropEffects props, ParticleEffects particles)
{
    public const float ChestHeight = 40f;

    // One-shot systems are removed after their longest particle faded.
    private const float OneShotSeconds = 2.5f;

    private readonly Dictionary<(string Key, int Slot), AttachedStatus> _statuses = [];
    private readonly Dictionary<(string Key, ulong Owner), double> _throttle = [];

    public BeamEffects Beams => beams;
    public PropEffects Props => props;
    public ParticleEffects Particles => particles;

    public void Burst(Vector3 position, FxColor color) => Play(WarcraftParticles.Burst(color), position);

    public void BurstOn(LivePlayer player, FxColor color) => Burst(Chest(player.Position), color);

    public void Flash(Vector3 position, FxColor color) => Play(WarcraftParticles.Flash(color), position);

    public void Sparks(Vector3 position, FxColor color) => Play(WarcraftParticles.Sparks(color), position);

    public void SparksOn(LivePlayer player, FxColor color) => Sparks(Chest(player.Position), color);

    /// <summary>Shockwave along the ground at <paramref name="ground"/>.</summary>
    public void Nova(Vector3 ground, FxColor color) => Play(WarcraftParticles.Nova(color), ground);

    /// <summary>Pillar of light standing on <paramref name="ground"/>.</summary>
    public void Column(Vector3 ground, FxColor color) => Play(WarcraftParticles.Column(color), ground);

    public void Smoke(Vector3 ground, FxColor color) => Play(WarcraftParticles.Smoke(color), ground);

    /// <summary>
    /// Ray between two points. Storm uses a safe jagged lightning fallback because
    /// CounterStrikeSharp cannot currently write particle control points without
    /// throwing for its native Vector wrapper.
    /// </summary>
    public void Line(Vector3 from, Vector3 to, FxColor color, Color fallback, float width, float lifetime = 0.4f)
    {
        if (color == FxColor.Storm)
        {
            DrawJaggedLightning(from, to, fallback, width, lifetime);
            return;
        }

        if (particles.Play(WarcraftParticles.Line(color), from, lifetime + OneShotSeconds, to) is null)
            beams.Draw(from, to, fallback, width, lifetime);
    }

    private void DrawJaggedLightning(Vector3 from, Vector3 to, Color fallback, float width, float lifetime)
    {
        var delta = to - from;
        var distance = delta.Length();
        if (distance < 1f)
            return;

        var direction = Vector3.Normalize(delta);
        var segmentCount = Math.Clamp((int)MathF.Ceiling(distance / 110f), 5, 8);

        var reference = MathF.Abs(direction.Z) < 0.9f
            ? Vector3.UnitZ
            : Vector3.UnitY;

        var right = Vector3.Normalize(Vector3.Cross(direction, reference));
        var up = Vector3.Normalize(Vector3.Cross(right, direction));
        var jitter = Math.Clamp(distance * 0.035f, 8f, 26f);

        var points = new Vector3[segmentCount + 1];
        points[0] = from;
        points[^1] = to;

        for (var i = 1; i < segmentCount; i++)
        {
            var t = i / (float)segmentCount;
            var center = Vector3.Lerp(from, to, t);
            var falloff = MathF.Sin(MathF.PI * t);

            var side = ((float)Random.Shared.NextDouble() * 2f - 1f) * jitter * falloff;
            var vertical = ((float)Random.Shared.NextDouble() * 2f - 1f) * jitter * 0.7f * falloff;

            points[i] = center + right * side + up * vertical;

            if ((i & 1) == 1)
                particles.Play(WarcraftParticles.Flash(FxColor.Storm), points[i], 0.9f);
        }

        var core = Color.FromArgb(255, 220, 235, 255);

        for (var i = 0; i < segmentCount; i++)
        {
            beams.Draw(points[i], points[i + 1], fallback, width, lifetime);
            beams.Draw(points[i], points[i + 1], core, Math.Max(0.8f, width * 0.35f), lifetime);
        }

        particles.Play(WarcraftParticles.Sparks(FxColor.Storm), to, 1.0f);
    }

    /// <summary>
    /// Keeps <paramref name="effect"/> attached to <paramref name="player"/> for
    /// <paramref name="seconds"/>; calling again with the same key refreshes the duration
    /// instead of stacking a second system.
    /// </summary>
    public void Status(string key, LivePlayer player, string effect, double seconds)
    {
        if (!particles.Enabled)
            return;

        var id = (key, player.Controller.Slot);
        var until = Server.CurrentTime + seconds;

        if (_statuses.TryGetValue(id, out var status) &&
            status.Particle.IsValid &&
            status.Pawn == player.Pawn.Handle)
        {
            if (until > status.Until)
                _statuses[id] = status with { Until = until };
            return;
        }

        if (_statuses.Remove(id, out var stale))
            particles.Stop(stale.Particle);

        if (particles.Follow(effect, player.Pawn) is not { } particle)
            return;

        _statuses[id] = new AttachedStatus(particle, player.Pawn.Handle, until);
        scheduler.Schedule((float)seconds, () => Expire(id, particle));
    }

    /// <summary>Ends a status effect early (e.g. the buff was consumed).</summary>
    public void EndStatus(string key, int slot)
    {
        if (_statuses.Remove((key, slot), out var status))
            particles.Stop(status.Particle);
    }

    /// <summary>True at most once per <paramref name="seconds"/> for the key and owner (rate limit for per-hit effects).</summary>
    public bool Throttle(string key, ulong owner, double seconds)
    {
        var now = Server.CurrentTime;
        if (_throttle.TryGetValue((key, owner), out var next) && now < next)
            return false;

        _throttle[(key, owner)] = now + seconds;
        return true;
    }

    /// <summary>Round start / map change: attached systems are gone or about to be removed.</summary>
    public void Clear()
    {
        foreach (var status in _statuses.Values)
            particles.Stop(status.Particle);

        _statuses.Clear();
        _throttle.Clear();
    }

    public static Vector3 Chest(Vector3 position) => position with { Z = position.Z + ChestHeight };

    private void Play(string effect, Vector3 position) => particles.Play(effect, position, OneShotSeconds);

    private void Expire((string Key, int Slot) id, CParticleSystem particle)
    {
        if (!_statuses.TryGetValue(id, out var status) || status.Particle != particle)
            return;

        var left = status.Until - Server.CurrentTime;
        if (left > 0.05)
        {
            scheduler.Schedule((float)left, () => Expire(id, particle));
            return;
        }

        _statuses.Remove(id);
        particles.Stop(particle);
    }

    private readonly record struct AttachedStatus(CParticleSystem Particle, nint Pawn, double Until);
}
