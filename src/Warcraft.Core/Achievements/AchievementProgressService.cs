using Warcraft.Api.Achievements;
using Warcraft.Api.Events;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Achievements;

internal sealed class AchievementProgressService(
    PlayerStateStore players,
    IWarcraftEventBus events,
    TimeProvider? time = null) : IAchievementsApi
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public AchievementProgressSnapshot GetProgress(ulong steamId, string achievementId)
    {
        var player = players.TryGetRuntime(steamId);
        if (player is null || !player.Achievements.TryGetValue(achievementId, out var progress))
            return new AchievementProgressSnapshot(0, false, null);

        return progress.ToSnapshot();
    }

    public long GetCurrency(ulong steamId)
        => players.TryGetRuntime(steamId)?.AchievementCurrency ?? 0;

    public bool AddCurrency(ulong steamId, long amount, string reason)
    {
        if (amount <= 0 || players.TryGetRuntime(steamId) is not { } player)
            return false;

        player.AchievementCurrency = checked(player.AchievementCurrency + amount);
        events.Publish(new PlayerStateChangedEvent(steamId, $"achievement-currency:{reason}"));
        return true;
    }

    public bool SpendCurrency(ulong steamId, long amount, string reason)
    {
        if (amount <= 0 || players.TryGetRuntime(steamId) is not { } player || player.AchievementCurrency < amount)
            return false;

        player.AchievementCurrency -= amount;
        events.Publish(new PlayerStateChangedEvent(steamId, $"achievement-currency:{reason}"));
        return true;
    }

    public IReadOnlyDictionary<string, AchievementProgressSnapshot> GetAll(ulong steamId)
    {
        var player = players.TryGetRuntime(steamId);
        if (player is null)
            return new Dictionary<string, AchievementProgressSnapshot>(StringComparer.OrdinalIgnoreCase);

        return player.Achievements.ToDictionary(
            x => x.Key,
            x => x.Value.ToSnapshot(),
            StringComparer.OrdinalIgnoreCase);
    }

    public AchievementMutationResult AddProgress(
        ulong steamId,
        string achievementId,
        long amount,
        long target,
        string reason)
    {
        if (amount <= 0)
            return Failure(steamId, achievementId, "Progress amount must be positive.");

        var current = GetProgress(steamId, achievementId);
        return SetProgress(
            steamId,
            achievementId,
            checked(current.Progress + amount),
            target,
            reason);
    }

    public AchievementMutationResult SetProgress(
        ulong steamId,
        string achievementId,
        long progress,
        long target,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(achievementId) || target <= 0)
            return Failure(steamId, achievementId, "Invalid achievement progress request.");

        var player = players.TryGetRuntime(steamId);
        if (player is null)
            return new AchievementMutationResult(false, false, 0, 0, false, "Player is not loaded.");

        if (!player.Achievements.TryGetValue(achievementId, out var state))
        {
            state = new AchievementProgressRuntime();
            player.Achievements[achievementId] = state;
        }

        var previous = state.Progress;
        if (!state.Unlocked)
            state.Progress = Math.Clamp(Math.Max(state.Progress, progress), 0, target);

        var newlyUnlocked = !state.Unlocked && state.Progress >= target;
        if (newlyUnlocked)
        {
            state.Unlocked = true;
            state.UnlockedAt = _time.GetUtcNow();
            events.Publish(new AchievementUnlockedEvent(steamId, achievementId, state.UnlockedAt.Value));
        }

        if (state.Progress != previous || newlyUnlocked)
            events.Publish(new PlayerStateChangedEvent(steamId, $"achievement:{reason}"));

        return new AchievementMutationResult(
            true,
            newlyUnlocked,
            previous,
            state.Progress,
            state.Unlocked,
            newlyUnlocked ? "Achievement unlocked." : "Achievement progress updated.");
    }

    public AchievementMutationResult Unlock(ulong steamId, string achievementId, string reason)
        => SetProgress(steamId, achievementId, 1, 1, reason);

    private AchievementMutationResult Failure(ulong steamId, string achievementId, string message)
    {
        var current = GetProgress(steamId, achievementId);
        return new AchievementMutationResult(
            false,
            false,
            current.Progress,
            current.Progress,
            current.Unlocked,
            message);
    }
}
