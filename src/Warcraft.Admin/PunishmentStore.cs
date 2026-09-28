using System.Text.Json;
using CounterStrikeSharp.API;

namespace Warcraft.Admin;

internal sealed record PunishmentRecord
{
    public required ulong SteamId { get; init; }
    public required string PlayerName { get; init; }
    public required string Type { get; init; }
    public required string Reason { get; init; }
    public required string Admin { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsActive(DateTimeOffset now)
        => ExpiresAt is null || ExpiresAt > now;
}

internal sealed record WarningRecord
{
    public required ulong SteamId { get; init; }
    public required string PlayerName { get; init; }
    public required string Reason { get; init; }
    public required string Admin { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed record PunishmentFile
{
    public List<PunishmentRecord> Bans { get; init; } = [];
    public List<PunishmentRecord> Mutes { get; init; } = [];
    public List<PunishmentRecord> Gags { get; init; } = [];
    public List<WarningRecord> Warnings { get; init; } = [];
}

internal sealed class PunishmentStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private PunishmentFile _data = new();

    public static string PathName =>
        Path.Combine(Server.GameDirectory, "configs", "warcraft", "admin_punishments.json");

    public void Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        if (!File.Exists(PathName))
        {
            Save();
            return;
        }

        try
        {
            _data = JsonSerializer.Deserialize<PunishmentFile>(
                        File.ReadAllText(PathName),
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            ReadCommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true
                        }) ?? new();
        }
        catch
        {
            _data = new();
        }

        Prune();
    }

    public PunishmentRecord? GetBan(ulong steamId) => Active(_data.Bans, steamId);
    public PunishmentRecord? GetMute(ulong steamId) => Active(_data.Mutes, steamId);
    public PunishmentRecord? GetGag(ulong steamId) => Active(_data.Gags, steamId);

    public int WarningCount(ulong steamId)
        => _data.Warnings.Count(x => x.SteamId == steamId);

    public IReadOnlyList<WarningRecord> Warnings(ulong steamId)
        => _data.Warnings.Where(x => x.SteamId == steamId).OrderByDescending(x => x.CreatedAt).ToArray();

    public void Ban(ulong steamId, string name, int minutes, string reason, string admin)
        => Put(_data.Bans, steamId, name, "ban", minutes, reason, admin);

    public void Mute(ulong steamId, string name, int minutes, string reason, string admin)
        => Put(_data.Mutes, steamId, name, "mute", minutes, reason, admin);

    public void Gag(ulong steamId, string name, int minutes, string reason, string admin)
        => Put(_data.Gags, steamId, name, "gag", minutes, reason, admin);

    public void Warn(ulong steamId, string name, string reason, string admin)
    {
        _data.Warnings.Add(new WarningRecord
        {
            SteamId = steamId,
            PlayerName = name,
            Reason = reason,
            Admin = admin
        });
        Save();
    }

    public bool Unban(ulong steamId) => Remove(_data.Bans, steamId);
    public bool Unmute(ulong steamId) => Remove(_data.Mutes, steamId);
    public bool Ungag(ulong steamId) => Remove(_data.Gags, steamId);

    public bool ClearWarnings(ulong steamId)
    {
        var removed = _data.Warnings.RemoveAll(x => x.SteamId == steamId) > 0;
        if (removed)
            Save();
        return removed;
    }

    public bool Prune()
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        changed |= _data.Bans.RemoveAll(x => !x.IsActive(now)) > 0;
        changed |= _data.Mutes.RemoveAll(x => !x.IsActive(now)) > 0;
        changed |= _data.Gags.RemoveAll(x => !x.IsActive(now)) > 0;
        if (changed)
            Save();
        return changed;
    }

    private static PunishmentRecord? Active(IEnumerable<PunishmentRecord> source, ulong steamId)
    {
        var now = DateTimeOffset.UtcNow;
        return source.LastOrDefault(x => x.SteamId == steamId && x.IsActive(now));
    }

    private void Put(
        List<PunishmentRecord> target,
        ulong steamId,
        string name,
        string type,
        int minutes,
        string reason,
        string admin)
    {
        target.RemoveAll(x => x.SteamId == steamId);
        target.Add(new PunishmentRecord
        {
            SteamId = steamId,
            PlayerName = name,
            Type = type,
            Reason = reason,
            Admin = admin,
            ExpiresAt = minutes <= 0 ? null : DateTimeOffset.UtcNow.AddMinutes(minutes)
        });
        Save();
    }

    private bool Remove(List<PunishmentRecord> target, ulong steamId)
    {
        var changed = target.RemoveAll(x => x.SteamId == steamId) > 0;
        if (changed)
            Save();
        return changed;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var temp = PathName + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, Options));
        File.Move(temp, PathName, overwrite: true);
    }
}
