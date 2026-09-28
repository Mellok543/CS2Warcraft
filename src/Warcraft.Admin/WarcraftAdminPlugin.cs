using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Api.Menu;
using Warcraft.Api.Modules;
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
    private AdminAccess _access = new(new());
    private readonly PunishmentStore _punishments = new();
    private readonly Dictionary<ulong, ulong> _selectedTargets = [];
    private double _nextPruneAt;

    public override void Load(bool hotReload)
    {
        ReloadAdminConfig();
        _punishments.Load();

        AddCommand("css_admin", "Open Warcraft admin menu", OnAdminMenu);
        AddCommand("css_a", "Open Warcraft admin menu", OnAdminMenu);

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
            items.Add(Item("Перезагрузить admin.json", id =>
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
            items.Add(Item("Кик", _ => DoKick(target, "Кик администратором")));
            items.Add(Item($"Предупреждение ({_punishments.WarningCount(targetSteam)})", _ => DoWarn(target, "Предупреждение администратора")));
            items.Add(Item("Очистить предупреждения", _ => _punishments.ClearWarnings(targetSteam)));
        }

        if (_access.Has(steamId, 'm'))
            items.Add(Item(_punishments.GetMute(targetSteam) is null ? "Mute" : "Unmute", id =>
            {
                if (_punishments.GetMute(targetSteam) is null)
                    _api.Menu.RequestOpenPage(MutePage, id);
                else
                    DoUnmute(targetSteam);
            }));

        if (_access.Has(steamId, 'g'))
            items.Add(Item(_punishments.GetGag(targetSteam) is null ? "Gag" : "Ungag", id =>
            {
                if (_punishments.GetGag(targetSteam) is null)
                    _api.Menu.RequestOpenPage(GagPage, id);
                else
                    _punishments.Ungag(targetSteam);
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
            Item("Slay", _ => Slay(target)),
            Item("Slap 20 HP", _ => Slap(target, 20)),
            Item("HP = 1", _ => SetHp(target, 1)),
            Item("HP = 100", _ => SetHp(target, 100)),
            Item("Заморозить", _ => SetFrozen(target, true)),
            Item("Разморозить", _ => SetFrozen(target, false)),
            Item("$16000", _ => SetMoney(target, 16000))
        };

        var caller = Utilities.GetPlayerFromSteamId(steamId);
        if (caller is { IsValid: true })
            items.Add(Item("Телепортировать ко мне", _ => Bring(caller, target)));

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
            Item("+100 XP", _ => _api.Progress.AddXp(captured.SteamID, 100, $"admin:{steamId}")),
            Item("+500 XP", _ => _api.Progress.AddXp(captured.SteamID, 500, $"admin:{steamId}")),
            Item("+1000 XP", _ => _api.Progress.AddXp(captured.SteamID, 1000, $"admin:{steamId}")),
            Item("-100 XP", _ => _api.Progress.AddXp(captured.SteamID, -100, $"admin:{steamId}")),
            Item("+1 skill point", _ =>
            {
                var current = _api.Players.Get(captured.SteamID);
                if (current?.ActiveRaceId is { } raceId)
                    _api.Progress.GiveSkillPoints(captured.SteamID, raceId, 1, $"admin:{steamId}");
            }),
            Item("+5 skill points", _ =>
            {
                var current = _api.Players.Get(captured.SteamID);
                if (current?.ActiveRaceId is { } raceId)
                    _api.Progress.GiveSkillPoints(captured.SteamID, raceId, 5, $"admin:{steamId}");
            }),
            Item("Уровень активной расы = 10", _ =>
            {
                var current = _api.Players.Get(captured.SteamID);
                if (current?.ActiveRaceId is { } raceId)
                    _api.Progress.SetRaceLevel(captured.SteamID, raceId, 10, $"admin:{steamId}");
            }),
            Item("Сбросить Warcraft-прогресс", _ => _api.Progress.ResetPlayer(captured.SteamID, $"admin:{steamId}"))
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
                Item("Рестарт раунда через 1 сек.", _ => Server.ExecuteCommand("mp_restartgame 1"))
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
            return Item(captured.Name, _ => ChangeMap(captured));
        }).ToArray();

        return new MenuPageDescriptor(
            MapsPage,
            "СМЕНА КАРТЫ",
            $"КАРТ: {maps.Count}",
            items,
            ServerPage);
    }

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
        if (Server.CurrentTime < _nextPruneAt)
            return;

        _nextPruneAt = Server.CurrentTime + 1.0;
        if (_punishments.Prune())
        {
            foreach (var player in Utilities.GetPlayers().Where(IsHuman))
                ApplyVoiceState(player);
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
        Kick(target, $"Ban: {reason}");
    }

    private void DoKick(CCSPlayerController target, string reason)
        => Kick(target, reason);

    private void DoWarn(CCSPlayerController target, string reason)
    {
        _punishments.Warn(target.SteamID, target.PlayerName, reason, "admin");
        target.PrintToChat($" [WC] Предупреждение: {reason}");

        if (_config.WarnKickThreshold > 0 &&
            _punishments.WarningCount(target.SteamID) >= _config.WarnKickThreshold)
        {
            Kick(target, $"Достигнут лимит предупреждений ({_config.WarnKickThreshold})");
        }
    }

    private void DoMute(CCSPlayerController target, int minutes, string reason, ulong adminSteam)
    {
        _punishments.Mute(target.SteamID, target.PlayerName, minutes, reason, AdminIdentity(adminSteam));
        ApplyVoiceState(target);
        target.PrintToChat($" [WC] Mute: {DurationName(minutes)}. {reason}");
    }

    private void DoUnmute(ulong targetSteam)
    {
        _punishments.Unmute(targetSteam);
        var player = Utilities.GetPlayerFromSteamId(targetSteam);
        if (player is { IsValid: true })
            ApplyVoiceState(player);
    }

    private void DoGag(CCSPlayerController target, int minutes, string reason, ulong adminSteam)
    {
        _punishments.Gag(target.SteamID, target.PlayerName, minutes, reason, AdminIdentity(adminSteam));
        target.PrintToChat($" [WC] Gag: {DurationName(minutes)}. {reason}");
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
        Kick(target, Rest(command, 2, "Кик администратором"));
    }

    private void OnWarnCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'k')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is null) return;
        _punishments.Warn(target.SteamID, target.PlayerName, Rest(command, 2, "Предупреждение"), CallerIdentity(caller));
        target.PrintToChat($" [WC] Вы получили предупреждение ({_punishments.WarningCount(target.SteamID)}/{_config.WarnKickThreshold}).");
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
            DoUnmute(target.SteamID);
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
            _punishments.Ungag(target.SteamID);
    }

    private void OnSlayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null) Slay(target);
    }

    private void OnSlapCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is null) return;
        var damage = command.ArgCount >= 3 && int.TryParse(command.GetArg(2), out var parsed) ? parsed : 20;
        Slap(target, Math.Clamp(damage, 0, 1000));
    }

    private void OnFreezeCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null) SetFrozen(target, true);
    }

    private void OnUnfreezeCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f')) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null) SetFrozen(target, false);
    }

    private void OnHpCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f') || command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var hp))
            return;

        var target = ResolveSingleTarget(caller, command);
        if (target is not null) SetHp(target, Math.Clamp(hp, 1, 1000));
    }

    private void OnMoneyCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f') || command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var money))
            return;

        var target = ResolveSingleTarget(caller, command);
        if (target is not null) SetMoney(target, Math.Clamp(money, 0, 16000));
    }

    private void OnBringCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Require(caller, command, 'f') || caller is null) return;
        var target = ResolveSingleTarget(caller, command);
        if (target is not null) Bring(caller, target);
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
            ChangeMap(known);
        else if (Server.IsMapValid(name))
            Server.ExecuteCommand($"changelevel \"{Safe(name)}\"");
        else
            command.ReplyToCommand("[WC] Карта не найдена.");
    }

    private void OnRestartRoundCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (Require(caller, command, 'i'))
            Server.ExecuteCommand("mp_restartgame 1");
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

    // -------------------- common helpers --------------------

    private void ReloadAdminConfig()
    {
        _config = AdminConfig.LoadOrCreate(ModuleDirectory);
        _access = new AdminAccess(_config);
        Logger.LogInformation("Warcraft.Admin loaded {Count} admins.", _config.Admins.Count);
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

    private string AdminIdentity(ulong steamId)
        => steamId == 0
            ? "server"
            : _config.Admins.TryGetValue(steamId.ToString(), out var admin) && !string.IsNullOrWhiteSpace(admin.Name)
                ? admin.Name
                : steamId.ToString();

    private static string CallerIdentity(CCSPlayerController? caller)
        => caller is null ? "server" : caller.SteamID.ToString();

    private static string Safe(string text)
        => text.Replace("\\", string.Empty).Replace("\"", "'").Replace(";", string.Empty).Replace("\n", " ").Replace("\r", " ");

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
