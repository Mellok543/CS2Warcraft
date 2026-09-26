using Warcraft.Api.Abilities;
using Warcraft.Api.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Abilities;

/// <summary>Single implementation of the ability upgrade rules shared by progression and UI status.</summary>
internal static class AbilityUpgradeRules
{
    public static AbilityUpgradeBlock Check(
        RaceProgressRuntime? progress,
        RaceAbilityDefinition definition)
    {
        var raceLevel = progress?.Level ?? 1;
        var currentLevel = progress?.AbilityLevels.GetValueOrDefault(definition.Id) ?? 0;
        var skillPoints = progress?.SkillPoints ?? 0;

        if (raceLevel < definition.UnlockLevel)
            return AbilityUpgradeBlock.RaceLevelTooLow;

        if (currentLevel >= definition.MaxLevel)
            return AbilityUpgradeBlock.MaxLevelReached;

        if (skillPoints <= 0)
            return AbilityUpgradeBlock.NoSkillPoints;

        return AbilityUpgradeBlock.None;
    }

    public static string Describe(AbilityUpgradeBlock block, RaceAbilityDefinition? definition)
        => block switch
        {
            AbilityUpgradeBlock.None => "Способность можно улучшить.",
            AbilityUpgradeBlock.NoActiveRace => "Сначала выберите расу.",
            AbilityUpgradeBlock.NotInActiveRace => "Способность отсутствует у активной расы.",
            AbilityUpgradeBlock.RaceLevelTooLow =>
                $"Способность откроется на уровне расы {definition?.UnlockLevel}.",
            AbilityUpgradeBlock.MaxLevelReached => "Способность уже прокачана до максимума.",
            AbilityUpgradeBlock.NoSkillPoints => "Нет свободных очков навыков.",
            _ => block.ToString()
        };
}
