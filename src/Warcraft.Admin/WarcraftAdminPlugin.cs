using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Api.Menu;
using Warcraft.Api.Modules;
using Warcraft.Api.Persistence;
using Warcraft.Shared;

namespace Warcraft.Admin;

[MinimumApiVersion(80)]
public sealed class WarcraftAdminPlugin : BasePlugin
{
    private const string ModuleId = "warcraft.admin";
    private const string RootPage = "warcraft.admin.root";
    private const string PlayersPage = "warcraft.admin.players";
    private const string TargetPage = "warcraft.admin.target";
    private const string BanPage = "warcraft.admin.ban";
    private const string MutePage = "warcraft.admin.mute";
    private const string GagPage = "warcraft.admin.gag";
    private const string FunPage = "warcraft.admin.fun";
    private const string WarcraftPage = "warcraft.admin.warcraft";
    private const string ServerPage = "warcraft.admin.server";
    private const string MapsPage = "warcraft.admin.maps";

    public override string ModuleName => "Warcraft.Admin";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "Flag-based admin menu, punishments, server controls and Warcraft management.";

    private IWarcraftApi? _api;
    private AdminConfig _config = new();
    private readonly AdminAccess _access = new();
    private readonly PunishmentStore _punishments = new();
    private readonly Dictionary<ulong, ulong> _selectedTargets = [];
    private double _nextPruneAt;
    private double _nextAdminRefreshAt;
    private bool _adminRefreshInFlight;

    public override void Load(bool hotReload)
    {
        ReloadAdminConfig();
        _punishments.Load();

        AddCommand("css_admin", "Open Warcraft admin menu", OnAdminMenu);
        AddCommand("css_a", "Open Warcraft admin menu", OnAdminMenu);
        AddCommand("css_addadmin", "Console: add/update database admin", OnAddAdminCommand);
        AddCommand("css_deladmin", "Console: remove database admin", OnDeleteAdminCommand);
        AddCommand("css_admins", "Console: list database admins", OnAdminsCommand);

        AddCommand("css_ban", "Ban player", OnBanCommand);
        AddCommand("css_unban", "Unban SteamID64", OnUnbanCommand);
        AddCommand("css_kick", "Kick player", OnKickCommand);
        AddCommand("css_warn", "Warn player", OnWarnCommand);
        AddCommand("css_clearwarns", "Clear warnings", OnClearWarnsCommand);
        AddCommand("css_mute", "Mute player", OnMuteCommand);
        AddCommand("css_unmute", "Unmute player", OnUnmuteCommand);
        AddCommand("css_gag", "Gag player", OnGagCommand);
        AddCommand("css_ungag", "Ungag player", OnUngagCommand);

        AddCommand("css_slay", "Slay player", OnSlayCommand);
        AddCommand("css_slap", "Slap player", OnSlapCommand);
        AddCommand("css_freeze", "Freeze player", OnFreezeCommand);
        AddCommand("css_unfreeze", "Unfreeze player", OnUnfreezeCommand);
        AddCommand("css_hp", "Set player HP", OnHpCommand);
        AddCommand("css_money", "Set player money", OnMoneyCommand);
        AddCommand("css_bring", "Teleport player to admin", OnBringCommand);

        AddCommand("css_map", "Change map", OnMapCommand);
        AddCommand("css_rr", "Restart round", OnRestartRoundCommand);

        AddCommand("css_wc_xp", "Add Warcraft XP to a player", OnXpCommand);
        AddCommand("css_wc_level", "Set active race level", OnLevelCommand);
        AddCommand("css_wc_race", "Set active race", OnRaceCommand);
        AddCommand("css_wc_reset", "Reset Warcraft progress", OnResetCommand);
        AddCommand("css_wc_givepoints", "Give active race skill points", OnGivePointsCommand);
        AddCommand("css_wc_reload_races", "Reload Warcraft race configs", OnReloadRacesCommand);
        AddCommand("css_wc_status", "Show Warcraft diagnostics", OnStatusCommand);
        AddCommand("css_wc_reload_admin", "Reload admin.json", OnReloadAdminCommand);

        AddCommandListener("say", OnSay);
        AddCommandListener("say_team", OnSay);

        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = WarcraftCoreCapability.TryGet();
        if (_api is null)
        {
            Logger.LogError(WarcraftCoreCapability.UnavailableMessage, WarcraftCapabilityNames.CoreApi);
            return;
        }

        RegisterPages();
        _ = RefreshAdminsAsync();

        _api.Modules.Register(new ModuleRegistration(
            ModuleId,
            ModuleVersion,
            "Flag admin menu and punishments"));

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers().Where(IsHuman))
                ApplyVoiceState(player);
        }
    }

    public override void Unload(bool hotReload)
    {
        if (_api is not null)
        {
            foreach (var page in AllPages)
                _api.Menu.UnregisterPage(page, ModuleId);

            _api.Modules.Unregister(ModuleId);
        }

        _selectedTargets.Clear();
        _api = null;
    }

    // -------------------- menu --------------------

    private static readonly string[] AllPages =
    [
        RootPage, PlayersPage, TargetPage, BanPage, MutePage, GagPage,
        FunPage, WarcraftPage, ServerPage, MapsPage
    ];

    private void RegisterPages()
    {
        var menu = _api!.Menu;
        menu.RegisterPage(new MenuPageRegistration(RootPage, ModuleId, BuildRootPage));
        menu.RegisterPage(new MenuPageRegistration(PlayersPage, ModuleId, BuildPlayersPage));
        menu.RegisterPage(new MenuPageRegistration(TargetPage, ModuleId, BuildTargetPage));
        menu.RegisterPage(new MenuPageRegistration(BanPage, ModuleId, BuildBanPage));
        menu.RegisterPage(new MenuPageRegistration(MutePage, ModuleId, BuildMutePage));
        menu.RegisterPage(new MenuPageRegistration(GagPage, ModuleId, BuildGagPage));
        menu.RegisterPage(new MenuPageRegistration(FunPage, ModuleId, BuildFunPage));
        menu.RegisterPage(new MenuPageRegistration(WarcraftPage, ModuleId, BuildWarcraftPage));
        menu.RegisterPage(new MenuPageRegistration(ServerPage, ModuleId, BuildServerPage));
        menu.RegisterPage(new MenuPageRegistration(MapsPage, ModuleId, BuildMapsPage));
    }

    private void OnAdminMenu(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller is null)
        {
            command.ReplyToCommand("[WC] !admin доступно только игроку.");
            return;
        }

        if (!IsAdmin(caller))
        {
            command.ReplyToCommand("[WC] У вас нет доступа к админ-меню.");
            return;
        }

        _api?.Menu.RequestOpenPage(RootPage, caller.SteamID);
    }

    private MenuPageDescriptor? BuildRootPage(ulong steamId)
    {
        if (_api is null || !_access.IsAdmin(steamId))
            return null;

        var items = new List<MenuPageItemDescriptor>();

        if (HasAny(steamId, "bkmgfz"))
            items.Add(Item("Игроки", id => _api.Menu.RequestOpenPage(PlayersPage, id)));

        if (_access.Has(steamId, 'i'))
            items.Add(Item("Управление сервером", id => _api.Menu.RequestOpenPage(ServerPage, id)));

        if (_access.Has(steamId, 'z'))
            items.Add(Item("Warcraft / прогресс", id => _api.Menu.RequestOpenPage(WarcraftPage, id)));

        if (_access.Has(steamId, 'z'))
            items.Add(Item("Перезагрузить настройки админки", id =>
            {
                ReloadAdminConfig();
                _api.Menu.RequestOpenPage(RootPage, id);
            }));

        return new MenuPageDescriptor(
            RootPage,
            "ADMIN MENU",
            $"ФЛАГИ: {_access.Flags(steamId).ToUpperInvariant()}",
            items,
            "root");
    }

    private MenuPageDescriptor? BuildPlayersPage(ulong steamId)
    {
        if (_api is null || !HasAny(steamId, "bkmgfz"))
            return null;

        var players = Utilities.GetPlayers()
            .Where(IsHuman)
            .OrderBy(x => x.PlayerName)
            .Select(target =>
            {
                var captured = target.SteamID;
                var warnings = _punishments.WarningCount(captured);
                return new MenuPageItemDescriptor(
                    $"{target.PlayerName} • W:{warnings}",
                    admin =>
                    {
                        _selectedTargets[admin] = captured;
                        _api.Menu.RequestOpenPage(TargetPage, admin);
                    },
                    _access.CanTarget(steamId, captured),
                    !_access.CanTarget(steamId, captured) ? "Недостаточный immunity" : null);
            })
            .ToArray();

        return new MenuPageDescriptor(
            PlayersPage,
            "ИГРОКИ",
            "ВЫБЕРИТЕ ЦЕЛЬ",
            players,
            RootPage);
    }

    private MenuPageDescriptor? BuildTargetPage(ulong steamId)
    {
        if (_api is null || !TrySelectedTarget(steamId, out var target))
            return null;

        var targetSteam = target.SteamID;
        var items = new List<MenuPageItemDescriptor>();

        if (_access.Has(steamId, 'b'))
            items.Add(Item("Бан", id => _api.Menu.RequestOpenPage(BanPage, id)));

        if (_access.Has(steamId, 'k'))
        {
            items.Add(Item("Кик", admin => DoKick(target, "Кик администратором", admin)));
            items.Add(Item($"Предупреждение ({_punishments.WarningCount(targetSteam)})", admin => DoWarn(target, "Предупреждение администратора", admin)));
            items.Add(Item("Очистить предупреждения", _ => _punishments.ClearWarnings(targetSteam)));
        }

        if (_access.Has(steamId, 'm'))
            items.Add(Item(_punishments.GetMute(targetSteam) is null ? "Mute" : "Unmute", id =>
            {
                if (_punishments.GetMute(targetSteam) is null)
                    _api.Menu.RequestOpenPage(MutePage, id);
                else
                    DoUnmute(targetSteam, id);
            }));

        if (_access.Has(steamId, 'g'))
            items.Add(Item(_punishments.GetGag(targetSteam) is null ? "Gag" : "Ungag", id =>
            {
                if (_punishments.GetGag(targetSteam) is null)
                    _api.Menu.RequestOpenPage(GagPage, id);
                else
                {
                    _punishments.Ungag(targetSteam);
                    BroadcastAdminAction(id, "снял gag с", target);
                }
            }));

        if (_access.Has(steamId, 'f'))
            items.Add(Item("Fun-команды", id => _api.Menu.RequestOpenPage(FunPage, id)));

        if (_access.Has(steamId, 'z'))
            items.Add(Item("Warcraft-прогресс игрока", id => _api.Menu.RequestOpenPage(WarcraftPage, id)));

        return new MenuPageDescriptor(
            TargetPage,
            target.PlayerName.ToUpperInvariant(),
            $"STEAM {targetSteam}",
            items,
            PlayersPage);
    }

    private MenuPageDescriptor? BuildBanPage(ulong steamId)
        => BuildDurationPage(steamId, "БАН", _config.BanDurationsMinutes, minutes =>
        {
            if (TrySelectedTarget(steamId, out var target))
                DoBan(target, minutes, "Бан администратором", steamId);
        }, TargetPage);

    private MenuPageDescriptor? BuildMutePage(ulong steamId)
        => BuildDurationPage(steamId, "MUTE", _config.MuteDurationsMinutes, minutes =>
        {
            if (TrySelectedTarget(steamId, out var target))
                DoMute(target, minutes, "Mute администратором", steamId);
        }, TargetPage);

    private MenuPageDescriptor? BuildGagPage(ulong steamId)
        => BuildDurationPage(steamId, "GAG", _config.GagDurationsMinutes, minutes =>
        {
            if (TrySelectedTarget(steamId, out var target))
                DoGag(target, minutes, "Gag администратором", steamId);
        }, TargetPage);

    private MenuPageDescriptor? BuildDurationPage(
        ulong steamId,
        string title,
        IEnumerable<int> durations,
        Action<int> action,
        string parent)
    {
        if (!_selectedTargets.ContainsKey(steamId))
            return null;

        var items = durations
            .Distinct()
            .Select(minutes => Item(DurationName(minutes), _ => action(minutes)))
            .ToArray();

        return new MenuPageDescriptor(title.ToLowerInvariant(), title, "СРОК НАКАЗАНИЯ", items, parent);
    }

    private MenuPageDescriptor? BuildFunPage(ulong steamId)
    {
        if (!_access.Has(steamId, 'f') || !TrySelectedTarget(steamId, out var target))
            return null;

        var items = new List<MenuPageItemDescriptor>
        {
            Item("Slay", admin => { Slay(target); BroadcastAdminAction(admin, "убил", target); }),
            Item("Slap 20 HP", admin => { Slap(target, 20); BroadcastAdminAction(admin, "ударил", target, "-20 HP"); }),
            Item("HP = 1", admin => { SetHp(target, 1); BroadcastAdminAction(admin, "установил HP", target, "1"); }),
            Item("HP = 100", admin => { SetHp(target, 100); BroadcastAdminAction(admin, "установил HP", target, "100"); }),
            Item("Заморозить", admin => { SetFrozen(target, true); BroadcastAdminAction(admin, "заморозил", target); }),
            Item("Разморозить", admin => { SetFrozen(target, false); BroadcastAdminAction(admin, "разморозил", target); }),
            Item("$16000", admin => { SetMoney(target, 16000); BroadcastAdminAction(admin, "выдал деньги", target, "$16000"); })
        };

        var caller = Utilities.GetPlayerFromSteamId(steamId);
        if (caller is { IsValid: true })
            items.Add(Item("Телепортировать ко мне", admin => { Bring(caller, target); BroadcastAdminAction(admin, "телепортировал к себе", target); }));

        return new MenuPageDescriptor(
            FunPage,
            "FUN",
            target.PlayerName.ToUpperInvariant(),
            items,
            TargetPage);
    }

    private MenuPageDescriptor? BuildWarcraftPage(ulong steamId)
    {
        if (_api is null || !_access.Has(steamId, 'z'))
            return null;

        CCSPlayerController? target = null;
        if (_selectedTargets.TryGetValue(steamId, out var selected))
            target = Utilities.GetPlayerFromSteamId(selected);

        if (target is not { IsValid: true })
            target = Utilities.GetPlayerFromSteamId(steamId);

        if (target is not { IsValid: true })
            return null;

        var captured = target;
        var state = _api.Players.Get(captured.SteamID);
        var items = new List<MenuPageItemDescriptor>
        {
            Item("+100 XP", admin => { _api.Progress.AddXp(captured.SteamID, 100, $"admin:{steamId}"); BroadcastAdminAction(admin, "выдал XP", captured, "+100"); }),
            Item("+500 XP", admin => { _api.Progress.AddXp(captured.SteamID, 500, $"admin:{steamId}"); BroadcastAdminAction(admin, "выдал XP", captured, "+500"); }),
            Item("+1000 XP", admin => { _api.Progress.AddXp(captured.SteamID, 1000, $"admin:{steamId}"); BroadcastAdminAction(admin, "выдал XP", captured, "+1000"); }),
            Item("-100 XP", admin => { _api.Progress.AddXp(captured.SteamID, -100, $"admin:{steamId}"); BroadcastAdminAction(admin, "снял XP", captured, "-100"); }),
            Item("+1 skill point", admin =>
            {
                var current = _api.Players.Get(captured.SteamID);
                if (current?.ActiveRaceId is { } raceId)
                {
                    _api.Progress.GiveSkillPoints(captured.SteamID, raceId, 1, $"admin:{steamId}");
                    BroadcastAdminAction(admin, "выдал очки навыков", captured, "+1");
                }
            }),
            Item("+5 skill points", admin =>
            {
                var current = _api.Players.Get(captured.SteamID);
                if (current?.ActiveRaceId is { } raceId)
                {
                    _api.Progress.GiveSkillPoints(captured.SteamID, raceId, 5, $"admin:{steamId}");
                    BroadcastAdminAction(admin, "выдал очки навыков", captured, "+5");
                }
            }),
            Item("Уровень активной расы = 10", admin =>
            {
                var current = _api.Players.Get(captured.SteamID);
                if (current?.ActiveRaceId is { } raceId)
                {
                    _api.Progress.SetRaceLevel(captured.SteamID, raceId, 10, $"admin:{steamId}");
                    BroadcastAdminAction(admin, "установил уровень расы", captured, "10");
                }
            }),
            Item("Сбросить Warcraft-прогресс", admin =>
            {
                _api.Progress.ResetPlayer(captured.SteamID, $"admin:{steamId}");
                BroadcastAdminAction(admin, "сбросил Warcraft-прогресс", captured);
            })
        };

        return new MenuPageDescriptor(
            WarcraftPage,
            "WARCRAFT ADMIN",
            $"{captured.PlayerName} • XP {state?.GlobalXp ?? 0}",
            items,
            _selectedTargets.ContainsKey(steamId) ? TargetPage : RootPage);
    }

    private MenuPageDescriptor? BuildServerPage(ulong steamId)
    {
        if (_api is null || !_access.Has(steamId, 'i'))
            return null;

        return new MenuPageDescriptor(
            ServerPage,
            "SERVER",
            Server.MapName.ToUpperInvariant(),
            [
                Item("Сменить карту", id => _api.Menu.RequestOpenPage(MapsPage, id)),
                Item("Рестарт раунда через 1 сек.", admin => { BroadcastServerAction(admin, "перезапустил раунд"); Server.ExecuteCommand("mp_restartgame 1"); })
            ],
            RootPage);
    }

    private MenuPageDescriptor? BuildMapsPage(ulong steamId)
    {
        if (!_access.Has(steamId, 'i'))
            return null;

        var maps = MapCatalog.Load();
        var items = maps.Select(map =>
        {
            var captured = map;
            return Item(captured.Name, admin => { BroadcastServerAction(admin, "сменил карту на " + captured.Name); ChangeMap(captured); });
        }).ToArray();

        return new MenuPageDescriptor(
            MapsPage,
            "СМЕНА КАРТЫ",
            $"КАРТ: {maps.Count}",
            items,
            ServerPage);
    }

    // -------------------- database admins --------------------

    private void OnAddAdminCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller is not null)
        {
            command.ReplyToCommand("[WC] css_addadmin доступна только из server console/RCON.");
            return;
        }

        if (command.ArgCount < 5 ||
            !ulong.TryParse(command.GetArg(1), out var steamId) ||
            steamId == 0 ||
            !TryParseAdminDuration(command.GetArg(2), out var duration) ||
            !TryNormalizeFlags(command.GetArg(3), out var flags) ||
            !int.TryParse(command.GetArg(4), out var immunity) ||
            immunity < 0)
        {
            command.ReplyToCommand(
                "Использование: css_addadmin <steamid64> <time> <flags> <immunity> | time: 0, 30, 30m, 2h, 7d, 4w");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var online = Utilities.GetPlayerFromSteamId(steamId);
        var adminName = online is { IsValid: true } && !string.IsNullOrWhiteSpace(online.PlayerName)
            ? online.PlayerName
            : steamId.ToString();

        var entry = new AdminPersistenceEntry(
            steamId,
            adminName,
            flags,
            immunity,
            now,
            duration is null ? null : now.Add(duration.Value));

        _ = AddAdminAsync(entry, command);
    }

    private async Task AddAdminAsync(AdminPersistenceEntry entry, CommandInfo command)
    {
        var api = _api;
        if (api?.Persistence.HasProvider != true)
        {
            ReplyLater(command, "[WC] База данных недоступна.");
            return;
        }

        try
        {
            await api.Persistence.UpsertAdminAsync(entry);
            Server.NextFrame(() =>
            {
                _access.Upsert(entry);
                command.ReplyToCommand(
                    $"[WC] Admin {entry.SteamId} сохранён. flags={entry.Flags}, immunity={entry.Immunity}, expires={FormatExpiry(entry.ExpiresAt)}");
            });
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to add admin {SteamId}.", entry.SteamId);
            ReplyLater(command, "[WC] Ошибка записи администратора в БД.");
        }
    }

    private void OnDeleteAdminCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller is not null)
        {
            command.ReplyToCommand("[WC] css_deladmin доступна только из server console/RCON.");
            return;
        }

        if (command.ArgCount < 2 || !ulong.TryParse(command.GetArg(1), out var steamId) || steamId == 0)
        {
            command.ReplyToCommand("Использование: css_deladmin <steamid64>");
            return;
        }

        _ = DeleteAdminAsync(steamId, command);
    }

    private async Task DeleteAdminAsync(ulong steamId, CommandInfo command)
    {
        var api = _api;
        if (api?.Persistence.HasProvider != true)
        {
            ReplyLater(command, "[WC] База данных недоступна.");
            return;
        }

        try
        {
            await api.Persistence.DeleteAdminAsync(steamId);
            Server.NextFrame(() =>
            {
                _access.Remove(steamId);
                command.ReplyToCommand($"[WC] Admin {steamId} удалён.");
            });
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to delete admin {SteamId}.", steamId);
            ReplyLater(command, "[WC] Ошибка удаления администратора из БД.");
        }
    }

    private void OnAdminsCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller is not null)
        {
            command.ReplyToCommand("[WC] css_admins доступна только из server console/RCON.");
            return;
        }

        _ = ListAdminsAsync(command);
    }

    private async Task ListAdminsAsync(CommandInfo command)
    {
        var api = _api;
        if (api?.Persistence.HasProvider != true)
        {
            ReplyLater(command, "[WC] База данных недоступна.");
            return;
        }

        try
        {
            var admins = await api.Persistence.LoadAdminsAsync();
            Server.NextFrame(() =>
            {
                _access.Replace(admins);
                command.ReplyToCommand($"[WC] Администраторов: {admins.Count}");
                foreach (var admin in admins)
                {
                    command.ReplyToCommand(
                        $"[WC] {admin.Name} ({admin.SteamId}) | flags={admin.Flags} | immunity={admin.Immunity} | expires={FormatExpiry(admin.ExpiresAt)}");
                }
            });
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to list admins.");
            ReplyLater(command, "[WC] Ошибка чтения администраторов из БД.");
        }
    }

    private async Task RefreshAdminsAsync()
    {
        if (_adminRefreshInFlight)
            return;

        var api = _api;
        if (api?.Persistence.HasProvider != true)
            return;

        _adminRefreshInFlight = true;
        try
        {
            var admins = await api.Persistence.LoadAdminsAsync();
            Server.NextFrame(() =>
            {
                _access.Replace(admins);
                foreach (var player in Utilities.GetPlayers().Where(IsHuman))
                    SyncAdminName(player);
            });
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Failed to refresh Warcraft admin cache.");
        }
        finally
        {
            _adminRefreshInFlight = false;
        }
    }

    private static bool TryNormalizeFlags(string raw, out string flags)
    {
        const string allowed = "bkmgfiz";
        var normalized = new string(raw
            .ToLowerInvariant()
            .Where(char.IsLetter)
            .Where(allowed.Contains)
            .Distinct()
            .ToArray());

        flags = normalized;
        return normalized.Length > 0 &&
               raw.ToLowerInvariant().Where(char.IsLetter).All(allowed.Contains);
    }

    private static bool TryParseAdminDuration(string raw, out TimeSpan? duration)
    {
        duration = null;
        raw = raw.Trim().ToLowerInvariant();

        if (raw == "0")
            return true;

        var suffix = raw[^1];
        var numberText = char.IsLetter(suffix) ? raw[..^1] : raw;
        if (!double.TryParse(numberText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ||
            value <= 0)
        {
            return false;
        }

        duration = suffix switch
        {
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            'd' => TimeSpan.FromDays(value),
            'w' => TimeSpan.FromDays(value * 7),
            _ when !char.IsLetter(suffix) => TimeSpan.FromMinutes(value),
            _ => null
        };

        return duration is not null;
    }

    private static string FormatExpiry(DateTimeOffset? expiresAt)
        => expiresAt is null
            ? "never"
            : expiresAt.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

    private static void ReplyLater(CommandInfo command, string text)
        => Server.NextFrame(() => command.ReplyToCommand(text));

    // -------------------- punishments --------------------

    private void OnClientAuthorized(int slot, SteamID steamId)
    {
        var id = steamId.SteamId64;
        var ban = _punishments.GetBan(id);
        if (ban is null)
            return;

        Server.NextFrame(() =>
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is { IsValid: true })
                Kick(player, $"Ban: {ban.Reason}");
        });
    }

    private void OnClientPutInServer(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (!IsHuman(player))
            return;

        ApplyVoiceState(player!);
        SyncAdminName(player!);

        foreach (var existing in Utilities.GetPlayers().Where(IsHuman))
        {
            if (_punishments.GetMute(existing.SteamID) is not null)
                player!.SetListenOverride(existing, ListenOverride.Mute);
        }
    }

    private void OnClientDisconnect(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is not null)
            _selectedTargets.Remove(player.SteamID);
    }

    private void OnTick()
    {
        var now = Server.CurrentTime;

        if (now >= _nextPruneAt)
        {
            _nextPruneAt = now + 1.0;
            if (_punishments.Prune())
            {
                foreach (var player in Utilities.GetPlayers().Where(IsHuman))
                    ApplyVoiceState(player);
            }
        }

        if (now >= _nextAdminRefreshAt)
        {
            _nextAdminRefreshAt = now + 15.0;
            _ = RefreshAdminsAsync();
        }
    }

    private HookResult OnSay(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsHuman(player))
            return HookResult.Continue;

        if (_punishments.GetGag(player!.SteamID) is null)
            return HookResult.Continue;

        var text = command.ArgString.Trim().Trim('"');
        if (text.StartsWith('!') || text.StartsWith('/'))
            return HookResult.Continue;

        player.PrintToChat(" [WC] Вам запрещено писать в чат.");
        return HookResult.Handled;
    }

    private void DoBan(CCSPlayerController target, int minutes, string reason, ulong adminSteam)
    {
        _punishments.Ban(target.SteamID, target.PlayerName, minutes, reason, AdminIdentity(adminSteam));
        BroadcastAdminAction(adminSteam, "забанил", target, DurationName(minutes) + " • " + reason);
        Kick(target, "Ban: " + reason);
    }

    private void DoKick(CCSPlayerController target, string reason, ulong adminSteam)
    {
        BroadcastAdminAction(adminSteam, "кикнул", target, reason);
        Kick(target, reason);
    }

    private void DoWarn(CCSPlayerController target, string reason, ulong adminSteam)
    {
        _punishments.Warn(target.SteamID, target.PlayerName, reason, AdminIdentity(adminSteam));
        BroadcastAdminAction(adminSteam, "выдал предупреждение", target, reason);
        target.PrintToChat(" [WC] Предупреждение: " + reason);

        if (_config.WarnKickThreshold > 0 &&
            _punishments.WarningCount(target.SteamID) >= _config.WarnKickThreshold)
        {
            Kick(target, "Достигнут лимит предупреждений (" + _config.WarnKickThreshold + ")");
        }
    }

    private void DoMute(CCSPlayerController target, int minutes, string reason, ulong adminSteam)
    {
        _punishments.Mute(target.SteamID, target.PlayerName, minutes, reason, AdminIdentity(adminSteam));
        BroadcastAdminAction(adminSteam, "выдал mute", target, DurationName(minutes) + " • " + reason);
        ApplyVoiceState(target);
        target.PrintToChat(" [WC] Mute: " + DurationName(minutes) + ". " + reason);
    }

    private void DoUnmute(ulong targetSteam, ulong adminSteam)
    {
        _punishments.Unmute(targetSteam);
        var player = Utilities.GetPlayerFromSteamId(targetSteam);
        if (player is { IsValid: true })
        {
            ApplyVoiceState(player);
            BroadcastAdminAction(adminSteam, "снял mute с", player);
        }
    }

    private void DoGag(CCSPlayerController target, int minutes, string reason, ulong adminSteam)
    {
        _punishments.Gag(target.SteamID, target.PlayerName, minutes, reason, AdminIdentity(adminSteam));
        BroadcastAdminAction(adminSteam, "выдал gag", target, DurationName(minutes) + " • " + reason);
        target.PrintToChat(" [WC] Gag: " + DurationName(minutes) + ". " + reason);
    }

    private void ApplyVoiceState(CCSPlayerController sender)
    {
        var muted = _punishments.GetMute(sender.SteamID) is not null;
        foreach (var receiver in Utilities.GetPlayers().Where(IsHuman))
        {
            receiver.SetListenOverride(
                sender,
                muted ? ListenOverride.Mute : ListenOverride.Default);
        }
    }

    // -------------------- command handlers --------------------

    private void OnBanCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'b') || command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var minutes))
        {
            if (command.ArgCount < 3)
                command.ReplyToCommand("Использование: !ban <player> <minutes|0> [reason]");
            return;
        }

        var target = ResolveSingleTarget(caller, command);
        if (target is null) return;
        DoBan(target, minutes, Rest(command, 3, "Бан администратором"), caller?.SteamID ?? 0);
    }

    private void OnUnbanCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'b') || command.ArgCount < 2 ||
            !ulong.TryParse(command.GetArg(1), out var steamId))
        {
            if (command.ArgCount < 2)
                command.ReplyToCommand("Использование: !unban <steamid64>");
            return;
        }

        command.ReplyToCommand(_punishments.Unban(steamId)
            ? "[WC] Бан снят."
            : "[WC] Активный бан не найден.");
    }

    private void OnKickCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'k')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is null) return;
        DoKick(target, Rest(command, 2, "Кик администратором"), caller?.SteamID ?? 0);
    }

    private void OnWarnCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'k')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is null) return;
        DoWarn(target, Rest(command, 2, "Предупреждение"), caller?.SteamID ?? 0);
        target.PrintToChat(" [WC] Вы получили предупреждение (" + _punishments.WarningCount(target.SteamID) + "/" + _config.WarnKickThreshold + ").");
        if (_config.WarnKickThreshold > 0 && _punishments.WarningCount(target.SteamID) >= _config.WarnKickThreshold)
            Kick(target, "Лимит предупреждений");
    }

    private void OnClearWarnsCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'k')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null)
            _punishments.ClearWarnings(target.SteamID);
    }

    private void OnMuteCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'm') || command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var minutes))
        {
            if (command.ArgCount < 3)
                command.ReplyToCommand("Использование: !mute <player> <minutes|0> [reason]");
            return;
        }

        var target = ResolveSingleTarget(caller, command);
        if (target is not null)
            DoMute(target, minutes, Rest(command, 3, "Mute администратором"), caller?.SteamID ?? 0);
    }

    private void OnUnmuteCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'm')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null)
            DoUnmute(target.SteamID, caller?.SteamID ?? 0);
    }

    private void OnGagCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'g') || command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var minutes))
        {
            if (command.ArgCount < 3)
                command.ReplyToCommand("Использование: !gag <player> <minutes|0> [reason]");
            return;
        }

        var target = ResolveSingleTarget(caller, command);
        if (target is not null)
            DoGag(target, minutes, Rest(command, 3, "Gag администратором"), caller?.SteamID ?? 0);
    }

    private void OnUngagCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'g')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null)
        {
            _punishments.Ungag(target.SteamID);
            BroadcastAdminAction(caller?.SteamID ?? 0, "снял gag с", target);
        }
    }

    private void OnSlayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null) { Slay(target); BroadcastAdminAction(caller?.SteamID ?? 0, "убил", target); }
    }

    private void OnSlapCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is null) return;
        var damage = command.ArgCount >= 3 && int.TryParse(command.GetArg(2), out var parsed) ? parsed : 20;
        damage = Math.Clamp(damage, 0, 1000);
        Slap(target, damage);
        BroadcastAdminAction(caller?.SteamID ?? 0, "ударил", target, "-" + damage + " HP");
    }

    private void OnFreezeCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null) { SetFrozen(target, true); BroadcastAdminAction(caller?.SteamID ?? 0, "заморозил", target); }
    }

    private void OnUnfreezeCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null) { SetFrozen(target, false); BroadcastAdminAction(caller?.SteamID ?? 0, "разморозил", target); }
    }

    private void OnHpCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f') || command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var hp))
            return;

        var target = ResolveSingleTarget(caller, command);
        if (target is not null)
        {
            hp = Math.Clamp(hp, 1, 1000);
            SetHp(target, hp);
            BroadcastAdminAction(caller?.SteamID ?? 0, "установил HP", target, hp.ToString());
        }
    }

    private void OnMoneyCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f') || command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var money))
            return;

        var target = ResolveSingleTarget(caller, command);
        if (target is not null)
        {
            money = Math.Clamp(money, 0, 16000);
            SetMoney(target, money);
            BroadcastAdminAction(caller?.SteamID ?? 0, "установил деньги", target, "$" + money);
        }
    }

    private void OnBringCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f') || caller is null) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null) { Bring(caller, target); BroadcastAdminAction(caller.SteamID, "телепортировал к себе", target); }
    }

    private void OnMapCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'i') || command.ArgCount < 2)
        {
            if (command.ArgCount < 2)
                command.ReplyToCommand("Использование: !map <map>");
            return;
        }

        var name = command.GetArg(1);
        var known = MapCatalog.Load().FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

        if (known is not null)
        {
            BroadcastServerAction(caller?.SteamID ?? 0, "сменил карту на " + known.Name);
            ChangeMap(known);
        }
        else if (Server.IsMapValid(name))
        {
            BroadcastServerAction(caller?.SteamID ?? 0, "сменил карту на " + name);
            Server.ExecuteCommand($"changelevel \"{Safe(name)}\"");
        }
        else
            command.ReplyToCommand("[WC] Карта не найдена.");
    }

    private void OnRestartRoundCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (Require(caller, command, 'i'))
        {
            BroadcastServerAction(caller?.SteamID ?? 0, "перезапустил раунд");
            Server.ExecuteCommand("mp_restartgame 1");
        }
    }

    // -------------------- Warcraft z commands --------------------

    private void OnXpCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'z') || command.ArgCount < 3 ||
            !long.TryParse(command.GetArg(2), out var amount))
            return;

        var target = ResolveSingleTarget(caller, command);
        if (target is null || _api is null) return;

        var result = _api.Progress.AddXp(target.SteamID, amount, $"admin:{CallerIdentity(caller)}");
        command.ReplyToCommand(result.Success ? $"[WC] {target.PlayerName}: XP {amount:+#;-#;0}." : $"[WC] {result.Message}");
    }

    private void OnLevelCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'z') || command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var level))
            return;

        var target = ResolveSingleTarget(caller, command);
        var state = target is null ? null : _api?.Players.Get(target.SteamID);
        if (target is null || state?.ActiveRaceId is null || _api is null) return;

        var result = _api.Progress.SetRaceLevel(target.SteamID, state.ActiveRaceId, level, $"admin:{CallerIdentity(caller)}");
        command.ReplyToCommand(result.Message);
    }

    private void OnRaceCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'z') || command.ArgCount < 3) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is null || _api is null) return;
        command.ReplyToCommand(_api.Races.SelectRace(target.SteamID, command.GetArg(2), $"admin:{CallerIdentity(caller)}", force: true).Message);
    }

    private void OnResetCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'z')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null && _api is not null)
            command.ReplyToCommand(_api.Progress.ResetPlayer(target.SteamID, $"admin:{CallerIdentity(caller)}").Message);
    }

    private void OnGivePointsCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'z') || command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var amount))
            return;

        var target = ResolveSingleTarget(caller, command);
        var state = target is null ? null : _api?.Players.Get(target.SteamID);
        if (target is null || state?.ActiveRaceId is null || _api is null) return;
        command.ReplyToCommand(_api.Progress.GiveSkillPoints(target.SteamID, state.ActiveRaceId, amount, $"admin:{CallerIdentity(caller)}").Message);
    }

    private void OnReloadRacesCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'z') || _api is null) return;
        _api.Events.Publish(new RaceReloadRequestedEvent($"admin:{CallerIdentity(caller)}"));
    }

    private void OnStatusCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'z') || _api is null) return;
        foreach (var line in StatusReport.Build(_api.Diagnostics.GetStatus(), _api.Diagnostics.GetRaceCatalogHealth()))
            command.ReplyToCommand(line);
    }

    private void OnReloadAdminCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'z')) return;
        ReloadAdminConfig();
        command.ReplyToCommand("[WC] admin.json перезагружен.");
    }

    // -------------------- fun/server helpers --------------------

    private static void Slay(CCSPlayerController target)
    {
        if (target is { IsValid: true, PawnIsAlive: true })
            target.ExecuteClientCommandFromServer("kill");
    }

    private static void Slap(CCSPlayerController target, int damage)
    {
        var pawn = target.PlayerPawn.Value;
        if (pawn is not { IsValid: true } || !target.PawnIsAlive)
            return;

        if (damage >= pawn.Health)
        {
            Slay(target);
            return;
        }

        pawn.Health = Math.Max(1, pawn.Health - damage);
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

        var velocity = pawn.AbsVelocity;
        if (velocity is not null)
            pawn.Teleport(velocity: new Vector3(velocity.X, velocity.Y, Math.Max(250f, velocity.Z + 250f)));
    }

    private static void SetHp(CCSPlayerController target, int hp)
    {
        var pawn = target.PlayerPawn.Value;
        if (pawn is not { IsValid: true }) return;
        pawn.Health = hp;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
    }

    private static void SetMoney(CCSPlayerController target, int amount)
    {
        if (target.InGameMoneyServices is not { } money) return;
        money.Account = amount;
        Utilities.SetStateChanged(target, "CCSPlayerController", "m_pInGameMoneyServices");
    }

    private static void SetFrozen(CCSPlayerController target, bool frozen)
    {
        var pawn = target.PlayerPawn.Value;
        if (pawn is not { IsValid: true }) return;

        pawn.MoveType = frozen ? MoveType_t.MOVETYPE_NONE : MoveType_t.MOVETYPE_WALK;
        if (frozen)
            pawn.Teleport(velocity: Vector3.Zero);
    }

    private static void Bring(CCSPlayerController caller, CCSPlayerController target)
    {
        var callerPawn = caller.PlayerPawn.Value;
        var targetPawn = target.PlayerPawn.Value;
        var origin = callerPawn?.AbsOrigin;
        if (callerPawn is not { IsValid: true } || targetPawn is not { IsValid: true } || origin is null)
            return;

        targetPawn.Teleport(
            new CounterStrikeSharp.API.Modules.Utils.Vector(origin.X, origin.Y, origin.Z),
            targetPawn.EyeAngles,
            new CounterStrikeSharp.API.Modules.Utils.Vector(0, 0, 0));
    }

    private static void ChangeMap(AdminMapEntry map)
    {
        if (map.WorkshopId is not null)
            Server.ExecuteCommand($"ds_workshop_changelevel \"{Safe(map.Name)}\"");
        else
            Server.ExecuteCommand($"changelevel \"{Safe(map.Name)}\"");
    }

    private static void Kick(CCSPlayerController player, string reason)
    {
        if (!player.IsValid)
            return;

        if (player.UserId is { } userId)
            Server.ExecuteCommand($"kickid {userId} \"{Safe(reason)}\"");
    }

    private void SyncAdminName(CCSPlayerController player)
    {
        var current = _access.Get(player.SteamID);
        var api = _api;
        if (current is null || api?.Persistence.HasProvider != true ||
            string.Equals(current.Name, player.PlayerName, StringComparison.Ordinal))
        {
            return;
        }

        var updated = current with { Name = player.PlayerName };
        _access.Upsert(updated);
        _ = PersistAdminNameAsync(updated);
    }

    private async Task PersistAdminNameAsync(AdminPersistenceEntry entry)
    {
        try
        {
            if (_api?.Persistence.HasProvider == true)
                await _api.Persistence.UpsertAdminAsync(entry);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Failed to update admin name for {SteamId}.", entry.SteamId);
        }
    }

    private void BroadcastAdminAction(
        ulong adminSteamId,
        string action,
        CCSPlayerController target,
        string? details = null)
    {
        var adminName = AdminDisplayName(adminSteamId);
        var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : " • " + details;
        var message =
            " " + ChatColors.Gold + "[WC]" + ChatColors.Default + " " +
            ChatColors.Red + adminName + ChatColors.Default + " " + action + " " +
            ChatColors.Red + target.PlayerName + ChatColors.Default + suffix;

        foreach (var player in Utilities.GetPlayers().Where(IsHuman))
            player.PrintToChat(message);
    }

    private void BroadcastServerAction(ulong adminSteamId, string action)
    {
        var adminName = AdminDisplayName(adminSteamId);
        var message =
            " " + ChatColors.Gold + "[WC]" + ChatColors.Default + " " +
            ChatColors.Red + adminName + ChatColors.Default + " " + action;

        foreach (var player in Utilities.GetPlayers().Where(IsHuman))
            player.PrintToChat(message);
    }

    private string AdminDisplayName(ulong steamId)
    {
        if (steamId == 0)
            return "SERVER";

        var player = Utilities.GetPlayerFromSteamId(steamId);
        if (player is { IsValid: true } && !string.IsNullOrWhiteSpace(player.PlayerName))
            return player.PlayerName;

        return _access.Get(steamId)?.Name ?? steamId.ToString();
    }

    // -------------------- common helpers --------------------

    private void ReloadAdminConfig()
    {
        _config = AdminConfig.LoadOrCreate(ModuleDirectory);
        Logger.LogInformation("Warcraft.Admin settings loaded.");
    }

    private bool Require(CCSPlayerController? caller, CommandInfo command, char flag)
    {
        if (caller is null)
            return true;

        if (_access.Has(caller.SteamID, flag))
            return true;

        command.ReplyToCommand($"[WC] Требуется флаг '{flag}' или 'z'.");
        return false;
    }

    private bool IsAdmin(CCSPlayerController player)
        => player.IsValid && _access.IsAdmin(player.SteamID);

    private bool HasAny(ulong steamId, string flags)
        => flags.Any(flag => _access.Has(steamId, flag));

    private bool TrySelectedTarget(ulong adminSteamId, out CCSPlayerController target)
    {
        target = null!;
        if (!_selectedTargets.TryGetValue(adminSteamId, out var targetSteamId))
            return false;

        var resolved = Utilities.GetPlayerFromSteamId(targetSteamId);
        if (!IsHuman(resolved) || !_access.CanTarget(adminSteamId, targetSteamId))
            return false;

        target = resolved!;
        return true;
    }

    private CCSPlayerController? ResolveSingleTarget(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand("[WC] Укажите игрока.");
            return null;
        }

        var targets = command.GetArgTargetResult(1).Players.Where(IsHuman).ToArray();
        if (targets.Length != 1)
        {
            command.ReplyToCommand(targets.Length == 0 ? "[WC] Игрок не найден." : "[WC] Уточните цель.");
            return null;
        }

        var target = targets[0];
        if (caller is not null &&
            (!_access.CanTarget(caller.SteamID, target.SteamID) ||
             !CounterStrikeSharp.API.Modules.Admin.AdminManager.CanPlayerTarget(caller, target)))
        {
            command.ReplyToCommand("[WC] Нельзя применить действие к этому игроку из-за immunity.");
            return null;
        }

        return target;
    }

    private static MenuPageItemDescriptor Item(string text, Action<ulong> action)
        => new(text, action);

    private static string Rest(CommandInfo command, int start, string fallback)
    {
        if (command.ArgCount <= start)
            return fallback;

        var parts = new List<string>();
        for (var i = start; i < command.ArgCount; i++)
            parts.Add(command.GetArg(i));

        var text = string.Join(' ', parts).Trim();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static string DurationName(int minutes)
        => minutes <= 0 ? "Навсегда"
        : minutes < 60 ? $"{minutes} мин."
        : minutes % 1440 == 0 ? $"{minutes / 1440} дн."
        : $"{minutes / 60} ч.";

    private static string AdminIdentity(ulong steamId)
    {
        if (steamId == 0)
            return "server";

        var player = Utilities.GetPlayerFromSteamId(steamId);
        return player is { IsValid: true }
            ? $"{player.PlayerName} ({steamId})"
            : steamId.ToString();
    }

    private static string CallerIdentity(CCSPlayerController? caller)
        => caller is null ? "server" : caller.SteamID.ToString();

    private static string Safe(string text)
        => text.Replace("\\", string.Empty).Replace("\"", "'").Replace(";", string.Empty).Replace("\n", " ").Replace("\r", " ");

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
