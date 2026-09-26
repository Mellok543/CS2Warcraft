using System.Numerics;
using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Passives;

/// <summary>Passive: speed boost after taking damage. Config: multiplier, duration (s).</summary>
internal sealed class AdrenalineAbility(MovementController movement) : AbilityHandler
{
    public override string Id => "adrenaline";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "После получения урона скорость x{multiplier} на {duration} с.";
    protected override string DisplayName => "Адреналин";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePostEvent>(OnDamagePost));

    private void OnDamagePost(DamagePostEvent @event)
    {
        if (@event.FinalDamage <= 0 ||
            GetUsable(@event.VictimSteamId) is not { } ability ||
            GamePlayers.FindAlive(@event.VictimSteamId) is not { } victim)
        {
            return;
        }

        movement.Boost(
            victim.Controller.Slot,
            (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "multiplier", 1.2), 1, 3),
            Server.CurrentTime + Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "duration", 2), 0.1, 10));
    }
}

/// <summary>Passive: speed boost after a kill. Config: multiplier, duration (s).</summary>
internal sealed class KillSpeedAbility(MovementController movement) : AbilityHandler
{
    public override string Id => "kill_speed";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "После убийства скорость x{multiplier} на {duration} с.";
    protected override string DisplayName => "Жажда охоты";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerKillEvent>(OnKill));

    private void OnKill(PlayerKillEvent kill)
    {
        if (kill.TeamKill ||
            GetUsable(kill.KillerSteamId) is not { } ability ||
            GamePlayers.FindAlive(kill.KillerSteamId) is not { } killer)
        {
            return;
        }

        movement.Boost(
            killer.Controller.Slot,
            (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "multiplier", 1.3), 1, 3),
            Server.CurrentTime + Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "duration", 3), 0.1, 10));
    }
}

/// <summary>
/// Passive: stronger jumps. Config: forward (horizontal speed multiplier, default 1),
/// up (extra vertical speed, default 0).
/// </summary>
internal sealed class JumpBoostAbility : AbilityHandler
{
    public override string Id => "jump_boost";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Прыжок: горизонтальная скорость x{forward|1}, выше на {up|0}.";
    protected override string DisplayName => "Длинный прыжок";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerJumpEvent>(OnJump));

    private void OnJump(PlayerJumpEvent jump)
    {
        if (GetUsable(jump.SteamId) is not { } ability)
            return;

        var forward = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "forward", 1.0), 1, 3);
        var up = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "up", 0), 0, 800);
        var steamId = jump.SteamId;

        // The engine applies the jump impulse this frame; boost it on the next one.
        Server.NextFrame(() =>
        {
            if (GamePlayers.FindAlive(steamId) is not { } player || player.Pawn.AbsVelocity is not { } velocity)
                return;

            player.Pawn.Teleport(velocity: new Vector3(velocity.X * forward, velocity.Y * forward, velocity.Z + up));
        });
    }
}
