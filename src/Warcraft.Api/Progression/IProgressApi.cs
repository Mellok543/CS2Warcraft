namespace Warcraft.Api.Progression;

public interface IProgressApi
{
    ProgressMutationResult AddXp(ulong steamId, long amount, string reason);
    ProgressMutationResult SetRaceLevel(ulong steamId, string raceId, int level, string reason);
    ProgressMutationResult GiveSkillPoints(ulong steamId, string raceId, int amount, string reason);
    ProgressMutationResult SetAbilityLevel(ulong steamId, string abilityId, int level, string reason);
    ProgressMutationResult ResetPlayer(ulong steamId, string reason);
}

public sealed record ProgressMutationResult(
    bool Success,
    string Message,
    int? PreviousLevel = null,
    int? CurrentLevel = null,
    long? PreviousXp = null,
    long? CurrentXp = null);
