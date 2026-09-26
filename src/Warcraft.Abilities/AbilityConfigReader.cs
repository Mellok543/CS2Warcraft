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

    /// <summary>
    /// Reads a list of strings: either one list for every level
    /// (<c>["a", "b"]</c>) or one list per level (<c>[["a"], ["a", "b"]]</c>).
    /// </summary>
    public static IReadOnlyList<string> GetLevelStrings(PlayerAbilitySnapshot ability, string property)
    {
        if (ability.Config.ValueKind != JsonValueKind.Object ||
            !ability.Config.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() == 0)
        {
            return [];
        }

        var list = value;
        if (value[0].ValueKind == JsonValueKind.Array)
            list = value[Math.Clamp(ability.Level - 1, 0, value.GetArrayLength() - 1)];

        return list.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .ToArray();
    }
}
