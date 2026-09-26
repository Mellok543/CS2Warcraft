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

    public static string Seconds(TimeSpan value)
        => Math.Ceiling(value.TotalSeconds).ToString("0", CultureInfo.InvariantCulture);
}
