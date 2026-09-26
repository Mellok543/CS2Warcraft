using System.Text.Json;

namespace Warcraft.Core.Abilities;

/// <summary>
/// Reads Core-owned keys (such as <c>cooldown</c>) from race ability config.
/// A value is either a scalar or an array indexed by ability level.
/// </summary>
internal static class AbilityConfigValues
{
    public const string CooldownKey = "cooldown";

    public static double GetLevelDouble(JsonElement config, string property, int level, double fallback)
    {
        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetDouble(out var scalar) ? scalar : fallback;

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            return fallback;

        var index = Math.Clamp(level - 1, 0, value.GetArrayLength() - 1);
        var element = value[index];

        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var result)
            ? result
            : fallback;
    }

    public static TimeSpan GetCooldown(JsonElement config, int level)
        => TimeSpan.FromSeconds(Math.Max(0.0, GetLevelDouble(config, CooldownKey, level, 0.0)));

    /// <summary>Returns an error when the key exists but is not a non-negative number (array).</summary>
    public static string? ValidateNonNegativeNumber(JsonElement config, string property)
    {
        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty(property, out var value))
        {
            return null;
        }

        var items = value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [value];

        if (items.Length == 0)
            return $"'{property}' must not be an empty array.";

        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Number ||
                !item.TryGetDouble(out var number) ||
                number < 0)
            {
                return $"'{property}' must be a non-negative number or an array of them.";
            }
        }

        return null;
    }
}
