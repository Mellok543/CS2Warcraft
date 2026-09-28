using System.Text.Json;
using CounterStrikeSharp.API;

namespace Warcraft.Admin;

internal sealed record AdminConfig
{

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

internal sealed class AdminAccess
{
    private readonly Dictionary<ulong, Warcraft.Api.Persistence.AdminPersistenceEntry> _admins = [];

    public void Replace(IEnumerable<Warcraft.Api.Persistence.AdminPersistenceEntry> admins)
    {
        _admins.Clear();
        var now = DateTimeOffset.UtcNow;
        foreach (var admin in admins)
        {
            if (admin.ExpiresAt is null || admin.ExpiresAt > now)
                _admins[admin.SteamId] = admin;
        }
    }

    public void Upsert(Warcraft.Api.Persistence.AdminPersistenceEntry admin)
    {
        if (admin.ExpiresAt is null || admin.ExpiresAt > DateTimeOffset.UtcNow)
            _admins[admin.SteamId] = admin;
        else
            _admins.Remove(admin.SteamId);
    }

    public void Remove(ulong steamId) => _admins.Remove(steamId);

    public bool Has(ulong steamId, char flag)
    {
        if (!TryGet(steamId, out var admin))
            return false;

        var flags = admin.Flags.ToLowerInvariant();
        return flags.Contains('z') || flags.Contains(char.ToLowerInvariant(flag));
    }

    public bool IsAdmin(ulong steamId) => TryGet(steamId, out _);

    public int Immunity(ulong steamId)
        => TryGet(steamId, out var admin) ? admin.Immunity : 0;

    public string Flags(ulong steamId)
        => TryGet(steamId, out var admin) ? admin.Flags : string.Empty;

    public Warcraft.Api.Persistence.AdminPersistenceEntry? Get(ulong steamId)
        => TryGet(steamId, out var admin) ? admin : null;

    public IReadOnlyCollection<Warcraft.Api.Persistence.AdminPersistenceEntry> GetAll()
    {
        PruneExpired();
        return _admins.Values.OrderByDescending(x => x.Immunity).ThenBy(x => x.SteamId).ToArray();
    }

    public bool CanTarget(ulong callerSteamId, ulong targetSteamId)
    {
        if (callerSteamId == targetSteamId)
            return true;

        if (Has(callerSteamId, 'z'))
            return true;

        return Immunity(callerSteamId) >= Immunity(targetSteamId);
    }

    private bool TryGet(ulong steamId, out Warcraft.Api.Persistence.AdminPersistenceEntry admin)
    {
        if (_admins.TryGetValue(steamId, out admin!))
        {
            if (admin.ExpiresAt is null || admin.ExpiresAt > DateTimeOffset.UtcNow)
                return true;

            _admins.Remove(steamId);
        }

        admin = null!;
        return false;
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var id in _admins.Where(x => x.Value.ExpiresAt is not null && x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
            _admins.Remove(id);
    }
}
