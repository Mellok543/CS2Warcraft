using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Shared;
using Warcraft.Api.Events;
using Warcraft.Api.Modules;

namespace Warcraft.Admin;

[MinimumApiVersion(80)]
public sealed class WarcraftAdminPlugin : BasePlugin
{
    private const string AdminPermission = "@warcraft/admin";

    public override string ModuleName => "Warcraft.Admin";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "Administrative commands for CS2Warcraft through Warcraft.Api.";

    private IWarcraftApi? _api;

    public override void Load(bool hotReload)
    {
        AddCommand("css_wc_xp", "Add Warcraft XP to a player", OnXpCommand);
        AddCommand("css_wc_level", "Set active race level", OnLevelCommand);
        AddCommand("css_wc_race", "Set active race", OnRaceCommand);
        AddCommand("css_wc_reset", "Reset Warcraft progress", OnResetCommand);
        AddCommand("css_wc_givepoints", "Give active race skill points", OnGivePointsCommand);
        AddCommand("css_wc_reload_races", "Reload Warcraft race configs", OnReloadRacesCommand);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = WarcraftCoreCapability.TryGet();

        if (_api is null)
        {
            Logger.LogError(
                WarcraftCoreCapability.UnavailableMessage,
                WarcraftCapabilityNames.CoreApi);
            return;
        }

        _api.Modules.Register(new ModuleRegistration(
            "warcraft.admin",
            ModuleVersion,
            "Warcraft administrative commands"));
    }

    public override void Unload(bool hotReload)
    {
        _api?.Modules.Unregister("warcraft.admin");
        _api = null;
    }

    private void OnXpCommand(
        CCSPlayerController? caller,
        CommandInfo command)
    {
        if (!CanUse(caller, command))
            return;

        if (command.ArgCount < 3 ||
            !long.TryParse(command.GetArg(2), out var amount))
        {
            command.ReplyToCommand(
                "Использование: !wc_xp <player> <amount>");
            return;
        }

        var target = ResolveSingleTarget(caller, command);
        if (target is null || _api is null)
            return;

        var result = _api.Progress.AddXp(
            target.SteamID,
            amount,
            $"admin:{CallerIdentity(caller)}");

        command.ReplyToCommand(
            result.Success
                ? $"[Warcraft] {target.PlayerName}: XP изменён на {amount:+#;-#;0}."
                : $"[Warcraft] Ошибка: {result.Message}");
    }

    private void OnLevelCommand(
        CCSPlayerController? caller,
        CommandInfo command)
    {
        if (!CanUse(caller, command))
            return;

        if (command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var level))
        {
            command.ReplyToCommand(
                "Использование: !wc_level <player> <level>");
            return;
        }

        var target = ResolveSingleTarget(caller, command);
        if (target is null || _api is null)
            return;

        var state = _api.Players.Get(target.SteamID);
        if (state?.ActiveRaceId is null)
        {
            command.ReplyToCommand(
                $"[Warcraft] У {target.PlayerName} не выбрана раса.");
            return;
        }

        var result = _api.Progress.SetRaceLevel(
            target.SteamID,
            state.ActiveRaceId,
            level,
            $"admin:{CallerIdentity(caller)}");

        command.ReplyToCommand(
            result.Success
                ? $"[Warcraft] {target.PlayerName}: уровень расы установлен на {level}."
                : $"[Warcraft] Ошибка: {result.Message}");
    }

    private void OnRaceCommand(
        CCSPlayerController? caller,
        CommandInfo command)
    {
        if (!CanUse(caller, command))
            return;

        if (command.ArgCount < 3)
        {
            command.ReplyToCommand(
                "Использование: !wc_race <player> <raceId>");
            return;
        }

        var target = ResolveSingleTarget(caller, command);
        if (target is null || _api is null)
            return;

        var raceId = command.GetArg(2);

        var result = _api.Races.SelectRace(
            target.SteamID,
            raceId,
            $"admin:{CallerIdentity(caller)}");

        command.ReplyToCommand(
            result.Success
                ? $"[Warcraft] {target.PlayerName}: выбрана раса {raceId}."
                : $"[Warcraft] Ошибка: {result.Message}");
    }

    private void OnResetCommand(
        CCSPlayerController? caller,
        CommandInfo command)
    {
        if (!CanUse(caller, command))
            return;

        if (command.ArgCount < 2)
        {
            command.ReplyToCommand(
                "Использование: !wc_reset <player>");
            return;
        }

        var target = ResolveSingleTarget(caller, command);
        if (target is null || _api is null)
            return;

        var result = _api.Progress.ResetPlayer(
            target.SteamID,
            $"admin:{CallerIdentity(caller)}");

        command.ReplyToCommand(
            result.Success
                ? $"[Warcraft] Прогресс {target.PlayerName} сброшен."
                : $"[Warcraft] Ошибка: {result.Message}");
    }

    private void OnGivePointsCommand(
        CCSPlayerController? caller,
        CommandInfo command)
    {
        if (!CanUse(caller, command))
            return;

        if (command.ArgCount < 3 ||
            !int.TryParse(command.GetArg(2), out var amount))
        {
            command.ReplyToCommand(
                "Использование: !wc_givepoints <player> <amount>");
            return;
        }

        var target = ResolveSingleTarget(caller, command);
        if (target is null || _api is null)
            return;

        var state = _api.Players.Get(target.SteamID);
        if (state?.ActiveRaceId is null)
        {
            command.ReplyToCommand(
                $"[Warcraft] У {target.PlayerName} не выбрана раса.");
            return;
        }

        var result = _api.Progress.GiveSkillPoints(
            target.SteamID,
            state.ActiveRaceId,
            amount,
            $"admin:{CallerIdentity(caller)}");

        command.ReplyToCommand(
            result.Success
                ? $"[Warcraft] {target.PlayerName}: очки навыков изменены на {amount:+#;-#;0}."
                : $"[Warcraft] Ошибка: {result.Message}");
    }

    private void OnReloadRacesCommand(
        CCSPlayerController? caller,
        CommandInfo command)
    {
        if (!CanUse(caller, command))
            return;

        if (_api is null)
            return;

        _api.Events.Publish(new RaceReloadRequestedEvent(
            $"admin:{CallerIdentity(caller)}"));

        command.ReplyToCommand(
            "[Warcraft] Запрошена перезагрузка конфигов рас.");
    }

    private static bool CanUse(
        CCSPlayerController? caller,
        CommandInfo command)
    {
        if (caller is null)
            return true;

        if (AdminManager.PlayerHasPermissions(caller, AdminPermission))
            return true;

        command.ReplyToCommand(
            $"[Warcraft] Требуется право {AdminPermission}.");
        return false;
    }

    private static CCSPlayerController? ResolveSingleTarget(
        CCSPlayerController? caller,
        CommandInfo command)
    {
        var result = command.GetArgTargetResult(1);
        var targets = result.Players
            .Where(IsHuman)
            .ToArray();

        if (targets.Length == 0)
        {
            command.ReplyToCommand(
                "[Warcraft] Игрок не найден.");
            return null;
        }

        if (targets.Length > 1)
        {
            command.ReplyToCommand(
                "[Warcraft] Найдено несколько игроков. Уточните цель.");
            return null;
        }

        var target = targets[0];

        if (caller is not null &&
            !AdminManager.CanPlayerTarget(caller, target))
        {
            command.ReplyToCommand(
                "[Warcraft] Нельзя применить команду к этому игроку из-за immunity.");
            return null;
        }

        return target;
    }

    private static string CallerIdentity(CCSPlayerController? caller)
        => caller is null
            ? "server"
            : caller.SteamID.ToString();

    private static bool IsHuman(CCSPlayerController player)
        => player is { IsValid: true, IsBot: false } &&
           player.SteamID != 0;
}
