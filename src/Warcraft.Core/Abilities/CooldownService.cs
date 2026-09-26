using Warcraft.Api.Abilities;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Abilities;

/// <summary>
/// The single source of truth for ability cooldowns. Ready times live in
/// <see cref="PlayerRuntimeState.Cooldowns"/>; handlers never keep their own.
/// </summary>
internal sealed class CooldownService(PlayerStateStore players, TimeProvider time)
{
    public TimeSpan GetRemaining(ulong steamId, string abilityId)
    {
        var player = players.TryGetRuntime(steamId);
        if (player is null || !player.Cooldowns.TryGetValue(abilityId, out var readyAt))
            return TimeSpan.Zero;

        var remaining = readyAt - time.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public CooldownResult TryStart(ulong steamId, string abilityId, TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        var player = players.TryGetRuntime(steamId);
        if (player is null)
            return new(false, null, TimeSpan.Zero);

        var now = time.GetUtcNow();

        if (player.Cooldowns.TryGetValue(abilityId, out var readyAt) && readyAt > now)
            return new(false, readyAt, readyAt - now);

        if (duration == TimeSpan.Zero)
        {
            player.Cooldowns.Remove(abilityId);
            return new(true, null, TimeSpan.Zero);
        }

        readyAt = now + duration;
        player.Cooldowns[abilityId] = readyAt;
        return new(true, readyAt, TimeSpan.Zero);
    }
}
