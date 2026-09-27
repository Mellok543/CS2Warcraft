namespace Warcraft.Api.Achievements;

public interface IAchievementsApi
{
    AchievementProgressSnapshot GetProgress(ulong steamId, string achievementId);
    IReadOnlyDictionary<string, AchievementProgressSnapshot> GetAll(ulong steamId);
    long GetCurrency(ulong steamId);
    bool AddCurrency(ulong steamId, long amount, string reason);
    bool SpendCurrency(ulong steamId, long amount, string reason);

    AchievementMutationResult AddProgress(
        ulong steamId,
        string achievementId,
        long amount,
        long target,
        string reason);

    AchievementMutationResult SetProgress(
        ulong steamId,
        string achievementId,
        long progress,
        long target,
        string reason);

    AchievementMutationResult Unlock(
        ulong steamId,
        string achievementId,
        string reason);
}

public sealed record AchievementProgressSnapshot(
    long Progress,
    bool Unlocked,
    DateTimeOffset? UnlockedAt);

public sealed record AchievementMutationResult(
    bool Success,
    bool NewlyUnlocked,
    long PreviousProgress,
    long CurrentProgress,
    bool Unlocked,
    string Message);
