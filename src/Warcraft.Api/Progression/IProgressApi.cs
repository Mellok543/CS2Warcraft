namespace Warcraft.Api.Progression;

public interface IProgressApi
{
    ProgressMutationResult AddXp(ulong steamId, long amount, string reason);
    ProgressMutationResult AddXpExact(ulong steamId, long amount, string reason);
    long GetRequiredXpForLevel(int currentLevel);
    ProgressMutationResult SetRaceLevel(ulong steamId, string raceId, int level, string reason);
    ProgressMutationResult GiveSkillPoints(ulong steamId, string raceId, int amount, string reason);
    ProgressMutationResult SetAbilityLevel(ulong steamId, string abilityId, int level, string reason);
    AbilityUpgradeResult UpgradeAbility(ulong steamId, string abilityId);
    SkillResetResult ResetRaceAbilities(ulong steamId, string raceId, string reason);
    ProgressMutationResult ResetPlayer(ulong steamId, string reason);
}

public sealed record ProgressMutationResult(
    bool Success,
    string Message,
    int? PreviousLevel = null,
    int? CurrentLevel = null,
    long? PreviousXp = null,
    long? CurrentXp = null);

public sealed record AbilityUpgradeResult(
    bool Success,
    string Message,
    string AbilityId,
    int PreviousLevel,
    int CurrentLevel,
    int RemainingSkillPoints);

public sealed record SkillResetResult(
    bool Success,
    string Message,
    int RefundedPoints,
    int CurrentSkillPoints);
