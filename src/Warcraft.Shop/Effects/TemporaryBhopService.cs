using System.Numerics;
using CounterStrikeSharp.API;
using Warcraft.Api.Events;

namespace Warcraft.Shop.Effects;

internal sealed class TemporaryBhopService(Action<float, Action> schedule)
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

        // player_jump fires before CS2 has completely settled the jump velocity.
        // Delay slightly so the engine impulse is already present before applying bhop.
        schedule(0.04f, () =>
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
            var minimumSpeed = Math.Min(250f, (float)boost.MaxSpeed);

            float dirX;
            float dirY;
            if (horizontal >= 20f)
            {
                dirX = velocity.X / horizontal;
                dirY = velocity.Y / horizontal;
            }
            else
            {
                var yaw = pawn.EyeAngles.Y * MathF.PI / 180f;
                dirX = MathF.Cos(yaw);
                dirY = MathF.Sin(yaw);
            }

            var target = Math.Min(
                Math.Max(horizontal * (float)boost.Multiplier, minimumSpeed),
                (float)boost.MaxSpeed);

            pawn.Teleport(velocity: new Vector3(
                dirX * target,
                dirY * target,
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
