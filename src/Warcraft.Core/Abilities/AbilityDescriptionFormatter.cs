using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Warcraft.Core.Abilities;

/// <summary>
/// Renders ability description templates with race-specific config values.
/// <list type="bullet">
/// <item><c>{damage}</c> — value at the ability level (<c>35</c>, <c>1.25</c>).</item>
/// <item><c>{chance%}</c> — value × 100 as a percent (<c>0.14</c> → <c>14%</c>).</item>
/// <item><c>{interval|1}</c> — default when the key is absent from config.</item>
/// </list>
/// </summary>
internal static partial class AbilityDescriptionFormatter
{
    private const string MissingValue = "?";

    [GeneratedRegex(@"\{(?<key>[A-Za-z0-9_]+)(?<percent>%)?(?:\|(?<default>[^}]*))?\}")]
    private static partial Regex Placeholder();

    public static string Format(string template, JsonElement config, int level)
        => Placeholder().Replace(template, match =>
        {
            var key = match.Groups["key"].Value;
            var percent = match.Groups["percent"].Success;
            var value = AbilityConfigValues.GetLevelDouble(config, key, Math.Max(1, level), double.NaN);

            if (double.IsNaN(value))
            {
                if (!match.Groups["default"].Success)
                    return MissingValue;

                var fallback = match.Groups["default"].Value;
                if (!double.TryParse(fallback, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return fallback;
            }

            return percent
                ? (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"
                : value.ToString("0.##", CultureInfo.InvariantCulture);
        });

    /// <summary>Placeholders without a default whose key is missing from config.</summary>
    public static IEnumerable<string> MissingKeys(string template, JsonElement config)
    {
        foreach (Match match in Placeholder().Matches(template))
        {
            var key = match.Groups["key"].Value;
            if (match.Groups["default"].Success)
                continue;

            if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty(key, out _))
                yield return key;
        }
    }
}
