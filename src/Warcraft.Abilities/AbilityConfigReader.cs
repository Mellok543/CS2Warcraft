using System.Text.Json;
using Warcraft.Api.Abilities;

namespace Warcraft.Abilities;

internal static class AbilityConfigReader
{
    public static double GetLevelDouble(
        PlayerAbilitySnapshot ability,
        string property,
        double fallback = 0.0)
    {
        if (ability.Config.ValueKind != JsonValueKind.Object ||
            !ability.Config.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var scalar))
        {
            return scalar;
        }

        if (value.ValueKind != JsonValueKind.Array || ability.Level <= 0)
            return fallback;

        var index = Math.Min(ability.Level - 1, value.GetArrayLength() - 1);
        if (index < 0)
            return fallback;

        var element = value[index];
        return element.ValueKind == JsonValueKind.Number &&
               element.TryGetDouble(out var result)
            ? result
            : fallback;
    }

    public static int GetLevelInt(
        PlayerAbilitySnapshot ability,
        string property,
        int fallback = 0)
        => (int)Math.Round(GetLevelDouble(ability, property, fallback));
}
