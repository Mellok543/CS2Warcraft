using System.Numerics;
using CounterStrikeSharp.API;
using Warcraft.Api.Events;

namespace Warcraft.Shop.Effects;

internal sealed class TemporaryBhopService
{
    private readonly Dictionary<ulong, Boost> _boosts = [];
    private readonly Dictionary<ulong, double> _nextAt = [];

    public void Activate(
        ulong steamId,
        double now,
        double durationSeconds,
        double multiplier,
        double maxSpeed,
        double cooldownSeconds)
    {
        _boosts[steamId] = new Boost(
            now + Math.Max(0.1, durationSeconds),
            Math.Max(1.0, multiplier),
            Math.Max(1.0, maxSpeed),
            Math.Max(0.05, cooldownSeconds));
    }

    public void OnJump(PlayerJumpEvent jumped)
    {
        var now = Server.CurrentTime;
        if (!_boosts.TryGetValue(jumped.SteamId, out var boost))
            return;

        if (now >= boost.Until)
        {
            _boosts.Remove(jumped.SteamId);
            _nextAt.Remove(jumped.SteamId);
            return;
        }

        if (_nextAt.GetValueOrDefault(jumped.SteamId) > now)
            return;

        var steamId = jumped.SteamId;
        _nextAt[steamId] = now + boost.CooldownSeconds;

        // The engine applies the jump impulse this frame; scale the resulting velocity on the next one.
        Server.NextFrame(() =>
        {
            var player = Utilities.GetPlayerFromSteamId(steamId);
            var pawn = player?.PlayerPawn.Value;
            var velocity = pawn?.AbsVelocity;
            if (player is not { IsValid: true, PawnIsAlive: true } ||
                pawn is not { IsValid: true } ||
                velocity is null)
            {
                return;
            }

            var horizontal = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
            if (horizontal < 1f)
                return;

            var target = Math.Min(horizontal * (float)boost.Multiplier, (float)boost.MaxSpeed);
            var scale = target / horizontal;
            pawn.Teleport(velocity: new Vector3(
                velocity.X * scale,
                velocity.Y * scale,
                velocity.Z));
        });
    }

    public void Clear()
    {
        _boosts.Clear();
        _nextAt.Clear();
    }

    private readonly record struct Boost(
        double Until,
        double Multiplier,
        double MaxSpeed,
        double CooldownSeconds);
}
