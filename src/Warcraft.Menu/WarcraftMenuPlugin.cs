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
    public override string ModuleVersion => WarcraftVersion.Current;
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

        var current = api.Players.Get(player.SteamID)?.ActiveRaceId;
        var races = api.Races.GetAll()
            .Select(x => (Race: x, Availability: api.Races.GetAvailability(player.SteamID, x.Id)))
            .OrderByDescending(x => x.Availability.IsAvailable)
            .ThenBy(x => x.Race.Requirements?.TotalLevel ?? 0)
            .ThenBy(x => x.Race.Name)
            .ToArray();

        var open = races.Count(x => x.Availability.IsAvailable);
        var menu = new CenterHtmlMenu($"Выбор расы | открыто {open}/{races.Length}", this)
        {
            ExitButton = true
        };

        foreach (var (race, availability) in races)
        {
            var raceId = race.Id;
            var suffix = string.Equals(race.Id, current, StringComparison.OrdinalIgnoreCase)
                ? " [выбрана]"
                : availability.VipLocked
                    ? " [VIP]"
                    : !availability.IsAvailable
                        ? " [закрыта]"
                        : string.Empty;

            menu.AddMenuOption(race.Name + suffix, (_, _) => OpenRacePreview(player, raceId));
        }

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
    }

    private void OpenRacePreview(CCSPlayerController player, string raceId)
    {
        var api = _api;
        var race = api?.Races.Get(raceId);
        if (api is null || race is null)
            return;

        var availability = api.Races.GetAvailability(player.SteamID, race.Id);
        var selected = string.Equals(
            api.Players.Get(player.SteamID)?.ActiveRaceId,
            race.Id,
            StringComparison.OrdinalIgnoreCase);
        var abilities = api.Abilities.GetRaceAbilities(race.Id);

        Print(player, $"{race.Name}: {race.Description}");
        foreach (var ability in abilities)
        {
            foreach (var line in AbilityStatusText.PreviewLines(ability))
                Print(player, line);
        }

        if (!availability.AlreadyUnlocked)
        {
            foreach (var requirement in availability.Requirements)
            {
                Print(player, $"{(requirement.IsMet ? "[+]" : "[-]")} {requirement.Description}: " +
                              $"{requirement.Current}/{requirement.Required}");
            }
        }

        var menu = new CenterHtmlMenu(race.Name, this)
        {
            ExitButton = true
        };

        var selectText = selected
            ? "Уже выбрана"
            : availability.VipLocked
                ? "Только для VIP"
                : !availability.IsAvailable
                    ? "Закрыта (условия в чате)"
                    : "Выбрать расу";

        menu.AddMenuOption(
            selectText,
            (_, _) =>
            {
                var result = api.Races.SelectRace(player.SteamID, race.Id, "menu");
                Print(player, result.Success ? $"Вы выбрали расу {race.Name}." : result.Message);
                OpenMainMenu(player);
            },
            selected || !availability.IsAvailable);

        if (!availability.AlreadyUnlocked)
        {
            foreach (var requirement in availability.Requirements)
            {
                menu.AddMenuOption(
                    $"{(requirement.IsMet ? "✔" : "✘")} {requirement.Description}: {requirement.Current}/{requirement.Required}",
                    (_, _) => { },
                    true);
            }
        }

        foreach (var ability in abilities)
            menu.AddMenuOption(AbilityStatusText.PreviewTitle(ability), (_, _) => { }, true);

        menu.AddMenuOption("Назад", (_, _) => OpenRaceMenu(player));

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

            menu.AddMenuOption(
                AbilityStatusText.UpgradeLine(status),
                (_, _) => OpenAbilityDetails(player, abilityId));
        }

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
    }

    private void OpenAbilityDetails(CCSPlayerController player, string abilityId)
    {
        var api = _api;
        var status = api?.Abilities.GetPlayerAbilities(player.SteamID)
            .FirstOrDefault(x => string.Equals(x.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase));

        if (api is null || status is null)
            return;

        foreach (var line in AbilityStatusText.DetailLines(status))
            Print(player, line);

        var menu = new CenterHtmlMenu(status.DisplayName, this)
        {
            ExitButton = true
        };

        menu.AddMenuOption(
            $"{AbilityStatusText.Tag(status)}Уровень {status.Level}/{status.MaxLevel} | {AbilityStatusText.State(status)}",
            (_, _) => { },
            true);

        menu.AddMenuOption(
            "Улучшить",
            (_, _) =>
            {
                var result = api.Progress.UpgradeAbility(player.SteamID, abilityId);
                Print(player, $"{result.Message} {status.DisplayName}: {result.PreviousLevel}->{result.CurrentLevel}");
                OpenAbilityDetails(player, abilityId);
            },
            !status.CanUpgrade || !status.HandlerRegistered);

        menu.AddMenuOption("Назад", (_, _) => OpenAbilityMenu(player));

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
    }

    private static void Print(CCSPlayerController player, string message)
        => player.PrintToChat($" [Warcraft] {message}");

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
