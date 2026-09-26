using System.Globalization;
using Warcraft.Api.Abilities;

namespace Warcraft.Menu;

/// <summary>Renders Core-computed <see cref="PlayerAbilityStatus"/> values. No game rules here.</summary>
internal static class AbilityStatusText
{
    public static string Tag(PlayerAbilityStatus status)
        => status.IsUltimate
            ? "[U] "
            : status.ActiveSlot is { } slot
                ? $"[A{slot}] "
                : string.Empty;

    public static string UpgradeLine(PlayerAbilityStatus status)
    {
        var text = $"{Tag(status)}{status.DisplayName} [{status.Level}/{status.MaxLevel}]";

        if (!status.IsUnlocked)
            text += $" | ур. расы {status.UnlockLevel}";

        if (!status.HandlerRegistered)
            text += " | нет обработчика";

        return text;
    }

    public static string InfoLine(PlayerAbilityStatus status)
        => $"{Tag(status)}{status.DisplayName} [{status.Level}/{status.MaxLevel}] | {State(status)}";

    public static string UltimateSummary(PlayerAbilityStatus? ultimate)
        => ultimate is null
            ? "Ультимейт: нет"
            : $"Ультимейт: {ultimate.DisplayName} | {State(ultimate)}";

    public static string State(PlayerAbilityStatus status)
    {
        if (!status.IsUnlocked)
            return $"закрыт до ур. {status.UnlockLevel}";

        if (!status.IsLearned)
            return "не изучен";

        if (status.CooldownRemaining > TimeSpan.Zero)
            return $"перезарядка {Seconds(status.CooldownRemaining)} с";

        return status.Kind is AbilityKind.Active or AbilityKind.Ultimate ? "готов" : "активен";
    }

    /// <summary>Chat lines for the ability card: current effect, next level, requirements.</summary>
    public static IEnumerable<string> DetailLines(PlayerAbilityStatus status)
    {
        yield return $"{Tag(status)}{status.DisplayName} [{status.Level}/{status.MaxLevel}]";
        yield return (status.IsLearned ? "Сейчас: " : "На 1 уровне: ") + status.Description;

        if (status.IsLearned && status.NextLevelDescription is { } next)
            yield return "Следующий уровень: " + next;

        if (!status.IsUnlocked)
            yield return $"Требуется уровень расы {status.UnlockLevel} (сейчас {status.RaceLevel}).";

        if (status.ConditionsDescription is { } conditions)
            yield return "Условия: " + conditions;

        if (status.IsUltimate)
            yield return "Активация: bind x css_ultimate (или !ultimate)";
        else if (status.ActiveSlot is { } slot)
            yield return $"Активация: bind c \"css_ability {slot}\" (или !ability {slot})";
    }

    public static string PreviewTitle(AbilityInfo ability)
        => $"{(ability.IsUltimate ? "[U] " : string.Empty)}{ability.DisplayName} | ур. {ability.UnlockLevel}";

    public static IEnumerable<string> PreviewLines(AbilityInfo ability)
    {
        var effect = ability.MaxLevel > 1 && ability.FirstLevelDescription != ability.MaxLevelDescription
            ? $"{ability.FirstLevelDescription} → макс.: {ability.MaxLevelDescription}"
            : ability.FirstLevelDescription;

        yield return $"{PreviewTitle(ability)}: {effect}";

        if (ability.ConditionsDescription is { } conditions)
            yield return "   Условия: " + conditions;
    }

    public static string Seconds(TimeSpan value)
        => Math.Ceiling(value.TotalSeconds).ToString("0", CultureInfo.InvariantCulture);
}
