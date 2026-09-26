using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Shared;
using Warcraft.Api.Abilities;
using Warcraft.Api.Modules;
using Warcraft.Api.Races;

namespace Warcraft.Menu;

[MinimumApiVersion(80)]
public sealed class WarcraftMenuPlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Menu";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "Main UI shell and race/ability menus for CS2Warcraft.";

    private IWarcraftApi? _api;

    public override void Load(bool hotReload)
    {
        AddCommand("css_wc", "Open Warcraft menu", OnWarcraftCommand);
        AddCommand("css_races", "Open Warcraft race selection", OnRacesCommand);
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
            "warcraft.menu",
            ModuleVersion,
            "Main Warcraft menu shell"));
    }

    public override void Unload(bool hotReload)
    {
        _api?.Modules.Unregister("warcraft.menu");
        _api = null;
    }

    private void OnWarcraftCommand(
        CCSPlayerController? player,
        CommandInfo command)
    {
        if (!IsHuman(player) || _api is null)
            return;

        OpenMainMenu(player!);
    }

    private void OnRacesCommand(
        CCSPlayerController? player,
        CommandInfo command)
    {
        if (!IsHuman(player) || _api is null)
            return;

        OpenRaceMenu(player!);
    }

    private void OpenMainMenu(CCSPlayerController player)
    {
        var api = _api;
        if (api is null)
            return;

        var state = api.Players.Get(player.SteamID);
        var race = state?.ActiveRaceId is null
            ? null
            : api.Races.Get(state.ActiveRaceId);

        var menu = new CenterHtmlMenu("Warcraft", this)
        {
            ExitButton = true
        };

        menu.AddMenuOption(
            $"Раса: {race?.Name ?? "не выбрана"}",
            (_, _) => OpenCurrentRaceMenu(player),
            race is null);

        if (race is not null)
        {
            var ultimate = api.Abilities.GetPlayerAbilities(player.SteamID)
                .FirstOrDefault(x => x.IsUltimate);

            menu.AddMenuOption(AbilityStatusText.UltimateSummary(ultimate), (_, _) => { }, true);
        }

        menu.AddMenuOption(
            "Выбор расы",
            (_, _) => OpenRaceMenu(player));

        menu.AddMenuOption(
            "Прокачка способностей",
            (_, _) => OpenAbilityMenu(player),
            race is null);

        menu.AddMenuOption(
            "Статистика",
            (_, _) => OpenStatsMenu(player));

        foreach (var entry in api.Menu.GetEntries("root", player.SteamID))
        {
            var entryId = entry.Id;
            menu.AddMenuOption(
                entry.DisplayName,
                (_, _) => api.Menu.Invoke(entryId, player.SteamID),
                !entry.Enabled);
        }

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
    }

    private void OpenRaceMenu(CCSPlayerController player)
    {
        var api = _api;
        if (api is null)
            return;

        var modifiers = api.Modifiers.GetCombined(player.SteamID);
        var current = api.Players.Get(player.SteamID)?.ActiveRaceId;

        var menu = new CenterHtmlMenu("Выбор расы", this)
        {
            ExitButton = true
        };

        foreach (var race in api.Races.GetAll().OrderBy(x => x.Name))
        {
            var definition = race;
            var vipBlocked = definition.VipOnly && !modifiers.CanAccessVipRaces;
            var selected = string.Equals(
                definition.Id,
                current,
                StringComparison.OrdinalIgnoreCase);

            var suffix = selected
                ? " [выбрана]"
                : vipBlocked
                    ? " [VIP]"
                    : string.Empty;

            menu.AddMenuOption(
                definition.Name + suffix,
                (_, _) =>
                {
                    var result = api.Races.SelectRace(
                        player.SteamID,
                        definition.Id,
                        "menu");

                    player.PrintToChat(
                        result.Success
                            ? $" [Warcraft] Вы выбрали расу {definition.Name}."
                            : $" [Warcraft] {result.Message}");

                    OpenMainMenu(player);
                },
                vipBlocked);
        }

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
    }

    private void OpenAbilityMenu(CCSPlayerController player)
    {
        var api = _api;
        if (api is null)
            return;

        var state = api.Players.Get(player.SteamID);
        if (state?.ActiveRaceId is null)
        {
            player.PrintToChat(" [Warcraft] Сначала выберите расу.");
            OpenRaceMenu(player);
            return;
        }

        var race = api.Races.Get(state.ActiveRaceId);
        if (race is null ||
            !state.Races.TryGetValue(race.Id, out var progress))
        {
            return;
        }

        var menu = new CenterHtmlMenu(
            $"Навыки | Очки: {progress.SkillPoints}",
            this)
        {
            ExitButton = true
        };

        foreach (var status in api.Abilities.GetPlayerAbilities(player.SteamID))
        {
            var abilityId = status.AbilityId;
            var displayName = status.DisplayName;

            menu.AddMenuOption(
                AbilityStatusText.UpgradeLine(status),
                (_, _) =>
                {
                    var result = api.Progress.UpgradeAbility(player.SteamID, abilityId);

                    player.PrintToChat(
                        $" [Warcraft] {result.Message} " +
                        $"{displayName}: {result.PreviousLevel}->{result.CurrentLevel}");

                    OpenAbilityMenu(player);
                },
                !status.CanUpgrade || !status.HandlerRegistered);
        }

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
    }

    private void OpenCurrentRaceMenu(CCSPlayerController player)
    {
        var api = _api;
        if (api is null)
            return;

        var state = api.Players.Get(player.SteamID);
        if (state?.ActiveRaceId is null)
            return;

        var race = api.Races.Get(state.ActiveRaceId);
        if (race is null ||
            !state.Races.TryGetValue(race.Id, out var progress))
        {
            return;
        }

        var menu = new CenterHtmlMenu(race.Name, this)
        {
            ExitButton = true
        };

        menu.AddMenuOption(
            $"Уровень: {progress.Level}/{race.MaxLevel}",
            (_, _) => { },
            true);

        menu.AddMenuOption(
            $"XP расы: {progress.Xp}",
            (_, _) => { },
            true);

        menu.AddMenuOption(
            $"Очки навыков: {progress.SkillPoints}",
            (_, _) => { },
            true);

        foreach (var status in api.Abilities.GetPlayerAbilities(player.SteamID))
            menu.AddMenuOption(AbilityStatusText.InfoLine(status), (_, _) => { }, true);

        menu.AddMenuOption(
            "Прокачка способностей",
            (_, _) => OpenAbilityMenu(player));

        menu.AddMenuOption(
            "Как использовать способности",
            (_, _) =>
            {
                player.PrintToChat(" [Warcraft] Ультимейт: bind x css_ultimate  (или !ultimate)");
                player.PrintToChat(" [Warcraft] Активная способность: bind c \"css_ability 1\"  (или !ability 1)");
            });

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
    }

    private void OpenStatsMenu(CCSPlayerController player)
    {
        var state = _api?.Players.Get(player.SteamID);
        if (state is null)
            return;

        var menu = new CenterHtmlMenu("Статистика", this)
        {
            ExitButton = true
        };

        menu.AddMenuOption(
            $"Общий XP: {state.GlobalXp}",
            (_, _) => { },
            true);

        menu.AddMenuOption(
            $"Изучено рас: {state.Races.Count}",
            (_, _) => { },
            true);

        var stats = state.Stats;
        var playTime = TimeSpan.FromSeconds(stats.PlaySeconds);

        foreach (var line in new[]
                 {
                     $"Убийства: {stats.Kills} | Смерти: {stats.Deaths}",
                     $"В голову: {stats.Headshots}",
                     $"Раунды: {stats.RoundsPlayed} | Победы: {stats.RoundsWon}",
                     $"Время в игре: {(int)playTime.TotalHours} ч {playTime.Minutes} мин"
                 })
        {
            menu.AddMenuOption(line, (_, _) => { }, true);
        }

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
    }

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
