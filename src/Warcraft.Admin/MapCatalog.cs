using CounterStrikeSharp.API;

namespace Warcraft.Admin;

internal sealed record AdminMapEntry(string Name, ulong? WorkshopId);

internal static class MapCatalog
{
    public static IReadOnlyList<AdminMapEntry> Load()
    {
        var paths = new[]
        {
            Path.Combine(Server.GameDirectory, "configs", "warcraft", "maplist.txt"),
            Path.Combine(Server.GameDirectory, "configs", "warcraft", "rtv", "maplist.txt")
        };

        var path = paths.FirstOrDefault(File.Exists);
        if (path is null)
            return [];

        return File.ReadAllLines(path)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith('#'))
            .Select(Parse)
            .Where(x => x is not null)
            .Cast<AdminMapEntry>()
            .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AdminMapEntry? Parse(string line)
    {
        var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            return null;

        return new AdminMapEntry(
            parts[0],
            parts.Length == 2 && ulong.TryParse(parts[1], out var id) ? id : null);
    }
}
