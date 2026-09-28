using System.Text.Json;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace Warcraft.Cosmetics;

/// <summary>
/// Reads the shared <c>models</c> switch of configs/warcraft/visuals.json (owned and
/// created by Warcraft.Abilities): without the Workshop addon mounted on the server,
/// cosmetic models must be neither precached nor spawned.
/// </summary>
internal static class VisualsSettings
{
    public static bool ModelsEnabled(ILogger logger)
    {
        var path = Path.Combine(Server.GameDirectory, "configs", "warcraft", "visuals.json");
        if (!File.Exists(path))
            return true;

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("models", StringComparison.OrdinalIgnoreCase))
                    return property.Value.ValueKind != JsonValueKind.False;
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(exception, "Could not read {Path}; cosmetic models stay enabled.", path);
            return true;
        }
    }
}
