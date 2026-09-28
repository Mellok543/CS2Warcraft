using System.Text.Json;
using CounterStrikeSharp.API;

namespace Warcraft.Admin;

internal sealed record AdminEntry
{
    public string Name { get; init; } = string.Empty;
    public string Flags { get; init; } = string.Empty;
    public int Immunity { get; init; }
}

internal sealed record AdminConfig
{
    public Dictionary<string, AdminEntry> Admins { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int WarnKickThreshold { get; init; } = 3;
    public int[] BanDurationsMinutes { get; init; } = [30, 120, 1440, 10080, 0];
    public int[] MuteDurationsMinutes { get; init; } = [10, 30, 120, 1440, 0];
    public int[] GagDurationsMinutes { get; init; } = [10, 30, 120, 1440, 0];

    public static string ConfigPath =>
        Path.Combine(Server.GameDirectory, "configs", "warcraft", "admin.json");

    public static AdminConfig LoadOrCreate(string moduleDirectory)
    {
        if (!File.Exists(ConfigPath))
        {
            var source = Path.Combine(moduleDirectory, "defaults", "admin.json");
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            if (File.Exists(source))
                File.Copy(source, ConfigPath, overwrite: false);
        }

        if (!File.Exists(ConfigPath))
            return new();

        try
        {
            return JsonSerializer.Deserialize<AdminConfig>(
                       File.ReadAllText(ConfigPath),
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true,
                           ReadCommentHandling = JsonCommentHandling.Skip,
                           AllowTrailingCommas = true
                       }) ?? new();
        }
        catch
        {
            return new();
        }
    }
}

internal sealed class AdminAccess(AdminConfig config)
{
    public bool Has(ulong steamId, char flag)
    {
        if (!config.Admins.TryGetValue(steamId.ToString(), out var admin))
            return false;

        var flags = admin.Flags.ToLowerInvariant();
        return flags.Contains('z') || flags.Contains(char.ToLowerInvariant(flag));
    }

    public bool IsAdmin(ulong steamId)
        => config.Admins.ContainsKey(steamId.ToString());

    public int Immunity(ulong steamId)
        => config.Admins.TryGetValue(steamId.ToString(), out var admin)
            ? admin.Immunity
            : 0;

    public string Flags(ulong steamId)
        => config.Admins.TryGetValue(steamId.ToString(), out var admin)
            ? admin.Flags
            : string.Empty;

    public bool CanTarget(ulong callerSteamId, ulong targetSteamId)
    {
        if (callerSteamId == targetSteamId)
            return true;

        if (Has(callerSteamId, 'z'))
            return true;

        return Immunity(callerSteamId) >= Immunity(targetSteamId);
    }
}
