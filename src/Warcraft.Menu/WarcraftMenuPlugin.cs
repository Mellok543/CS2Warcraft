using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Modules;
using Warcraft.Api.Races;
using Warcraft.Shared;

namespace Warcraft.Menu;

[MinimumApiVersion(80)]
public sealed class WarcraftMenuPlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Menu";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "JBF-style bindable CenterHtml UI for CS2Warcraft.";

    private IWarcraftApi? _api;
    private WarcraftMenuService? _menus;

    public override void Load(bool hotReload)
    {
        _menus = new WarcraftMenuService(this, WarcraftMenuConfig.LoadOrCreate());

        AddCommand("css_wc", "Open Warcraft menu", OnWarcraftCommand);
        AddCommand("css_races", "Open Warcraft race selection", OnRacesCommand);

        AddCommand("css_menu_up", "Move Warcraft menu selection up", OnMenuUp);
        AddCommand("css_menu_down", "Move Warcraft menu selection down", OnMenuDown);
        AddCommand("css_menu_select", "Select current Warcraft menu option", OnMenuSelect);
        AddCommand("css_menu_close", "Close Warcraft menu", OnMenuClose);
        AddCommand("css_wc_menubinds", "Show recommended Warcraft menu binds", OnMenuBinds);
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
            "Bindable Warcraft CenterHtml UI"));
    }

    public override void Unload(bool hotReload)
    {
        _api?.Modules.Unregister("warcraft.menu");
        _api = null;
        _menus = null;
    }

    private void OnWarcraftCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (IsHuman(player) && _api is not null)
            OpenMainMenu(player!);
    }

    private void OnRacesCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (IsHuman(player) && _api is not null)
            OpenRaceMenu(player!);
    }

    private void OnMenuUp(CCSPlayerController? player, CommandInfo command)
        => _menus?.Move(player, -1);

    private void OnMenuDown(CCSPlayerController? player, CommandInfo command)
        => _menus?.Move(player, 1);

    private void OnMenuSelect(CCSPlayerController? player, CommandInfo command)
        => _menus?.Select(player);

    private void OnMenuClose(CCSPlayerController? player, CommandInfo command)
        => _menus?.Close(player);

    private static void OnMenuBinds(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsHuman(player))
            return;

        player!.PrintToChat(" [Warcraft] Рекомендуемые бинды меню:");
        player.PrintToChat(" [Warcraft] bind UPARROW css_menu_up");
        player.PrintToChat(" [Warcraft] bind DOWNARROW css_menu_down");
        player.PrintToChat(" [Warcraft] bind ENTER css_menu_select");
        player.PrintToChat(" [Warcraft] bind BACKSPACE css_menu_close");
    }

    private void OpenMainMenu(CCSPlayerController player)
    {
        var api = _api;
        var menus = _menus;
        if (api is null || menus is null)
            return;

        var state = api.Players.Get(player.SteamID);
        var race = state?.ActiveRaceId is null ? null : api.Races.Get(state.ActiveRaceId);

        var subtitle = race is null
            ? "раса не выбрана"
            : $"{race.Name} • ур. {state!.Races.GetValueOrDefault(race.Id)?.Level ?? 1}";

        var menu = menus.Create("WARCRAFT", subtitle);

        menu.Add(
            race is null ? "Текущая раса: не выбрана" : $"Текущая раса: {race.Name}",
            _ => OpenCurrentRaceMenu(player),
            race is null);

        if (race is not null)
        {
            var ultimate = api.Abilities.GetPlayerAbilities(player.SteamID)
                .FirstOrDefault(x => x.IsUltimate);
            menu.AddInfo(AbilityStatusText.UltimateSummary(ultimate));
        }

        menu.Add("Выбор расы", _ => OpenRaceMenu(player));
        menu.Add("Прокачка способностей", _ => OpenAbilityMenu(player), race is null);
        menu.Add("Статистика", _ => OpenStatsMenu(player));

        foreach (var entry in api.Menu.GetEntries("root", player.SteamID))
        {
            var entryId = entry.Id;
            menu.Add(
                entry.DisplayName,
                _ => api.Menu.Invoke(entryId, player.SteamID),
                !entry.Enabled);
        }

        menus.Open(player, menu);
    }

    private void OpenRaceMenu(CCSPlayerController player)
    {
        var api = _api;
        var menus = _menus;
        if (api is null || menus is null)
            return;

        var current = api.Players.Get(player.SteamID)?.ActiveRaceId;
        var races = api.Races.GetAll()
            .Select(x => (Race: x, Availability: api.Races.GetAvailability(player.SteamID, x.Id)))
            .OrderByDescending(x => x.Availability.IsAvailable)
            .ThenBy(x => x.Race.Requirements?.TotalLevel ?? 0)
            .ThenBy(x => x.Race.Name)
            .ToArray();

        var open = races.Count(x => x.Availability.IsAvailable);
        var menu = menus.Create("ВЫБОР РАСЫ", $"открыто {open}/{races.Length}");

        foreach (var (race, availability) in races)
        {
            var suffix = string.Equals(race.Id, current, StringComparison.OrdinalIgnoreCase)
                ? "  ✓ выбрана"
                : availability.VipLocked
                    ? "  ◆ VIP"
                    : !availability.IsAvailable
                        ? "  🔒"
                        : string.Empty;

            var raceId = race.Id;
            menu.Add($"{race.Name}{suffix}", _ => OpenRacePreview(player, raceId));
        }

        menu.Add("← Назад", _ => OpenMainMenu(player));
        menus.Open(player, menu);
    }

    private void OpenRacePreview(CCSPlayerController player, string raceId)
    {
        var api = _api;
        var menus = _menus;
        var race = api?.Races.Get(raceId);
        if (api is null || menus is null || race is null)
            return;

        var availability = api.Races.GetAvailability(player.SteamID, race.Id);
        var selected = string.Equals(
            api.Players.Get(player.SteamID)?.ActiveRaceId,
            race.Id,
            StringComparison.OrdinalIgnoreCase);

        var menu = menus.Create(race.Name.ToUpperInvariant(), race.Description);

        var selectText = selected
            ? "✓ Уже выбрана"
            : availability.VipLocked
                ? "◆ Только для VIP"
                : !availability.IsAvailable
                    ? "🔒 Раса закрыта"
                    : "Выбрать эту расу";

        menu.Add(
            selectText,
            _ =>
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
                menu.AddInfo(
                    $"{(requirement.IsMet ? "✓" : "✗")} {requirement.Description}: " +
                    $"{requirement.Current}/{requirement.Required}");
            }
        }

        foreach (var ability in api.Abilities.GetRaceAbilities(race.Id))
            menu.AddInfo(AbilityStatusText.PreviewTitle(ability));

        menu.Add("← Назад к расам", _ => OpenRaceMenu(player));
        menus.Open(player, menu);
    }

    private void OpenAbilityMenu(CCSPlayerController player)
    {
        var api = _api;
        var menus = _menus;
        if (api is null || menus is null)
            return;

        var state = api.Players.Get(player.SteamID);
        if (state?.ActiveRaceId is null)
        {
            Print(player, "Сначала выберите расу.");
            OpenRaceMenu(player);
            return;
        }

        var race = api.Races.Get(state.ActiveRaceId);
        if (race is null || !state.Races.TryGetValue(race.Id, out var progress))
            return;

        var menu = menus.Create("НАВЫКИ", $"{race.Name} • очки: {progress.SkillPoints}");

        foreach (var status in api.Abilities.GetPlayerAbilities(player.SteamID))
        {
            var abilityId = status.AbilityId;
            menu.Add(
                AbilityStatusText.UpgradeLine(status),
                _ => OpenAbilityDetails(player, abilityId));
        }

        menu.Add("← Назад", _ => OpenMainMenu(player));
        menus.Open(player, menu);
    }

    private void OpenAbilityDetails(CCSPlayerController player, string abilityId)
    {
        var api = _api;
        var menus = _menus;
        var status = api?.Abilities.GetPlayerAbilities(player.SteamID)
            .FirstOrDefault(x =>
                string.Equals(x.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase));

        if (api is null || menus is null || status is null)
            return;

        var menu = menus.Create(
            status.DisplayName.ToUpperInvariant(),
            $"уровень {status.Level}/{status.MaxLevel}");

        menu.AddInfo($"{AbilityStatusText.Tag(status)}{AbilityStatusText.State(status)}");

        foreach (var line in AbilityStatusText.DetailLines(status))
            menu.AddInfo(line);

        menu.Add(
            status.CanUpgrade ? "Улучшить способность" : "Улучшение недоступно",
            _ =>
            {
                var result = api.Progress.UpgradeAbility(player.SteamID, abilityId);
                Print(
                    player,
                    $"{result.Message} {status.DisplayName}: " +
                    $"{result.PreviousLevel}->{result.CurrentLevel}");
                OpenAbilityDetails(player, abilityId);
            },
            !status.CanUpgrade || !status.HandlerRegistered);

        menu.Add("← Назад к навыкам", _ => OpenAbilityMenu(player));
        menus.Open(player, menu);
    }

    private void OpenCurrentRaceMenu(CCSPlayerController player)
    {
        var api = _api;
        var menus = _menus;
        if (api is null || menus is null)
            return;

        var state = api.Players.Get(player.SteamID);
        if (state?.ActiveRaceId is null)
            return;

        var race = api.Races.Get(state.ActiveRaceId);
        if (race is null || !state.Races.TryGetValue(race.Id, out var progress))
            return;

        var menu = menus.Create(race.Name.ToUpperInvariant(), $"уровень {progress.Level}/{race.MaxLevel}");
        menu.AddInfo($"XP расы: {progress.Xp}");
        menu.AddInfo($"Очки навыков: {progress.SkillPoints}");

        foreach (var status in api.Abilities.GetPlayerAbilities(player.SteamID))
            menu.AddInfo(AbilityStatusText.InfoLine(status));

        menu.Add("Прокачка способностей", _ => OpenAbilityMenu(player));
        menu.Add(
            "Как использовать способности",
            _ =>
            {
                Print(player, "Ультимейт: bind x css_ultimate  (или !ultimate)");
                Print(player, "Активная способность: bind c "css_ability 1"  (или !ability 1)");
                OpenCurrentRaceMenu(player);
            });
        menu.Add("← Назад", _ => OpenMainMenu(player));

        menus.Open(player, menu);
    }

    private void OpenStatsMenu(CCSPlayerController player)
    {
        var state = _api?.Players.Get(player.SteamID);
        var menus = _menus;
        if (state is null || menus is null)
            return;

        var stats = state.Stats;
        var playTime = TimeSpan.FromSeconds(stats.PlaySeconds);
        var menu = menus.Create("СТАТИСТИКА", player.PlayerName);

        menu.AddInfo($"Общий XP: {state.GlobalXp}");
        menu.AddInfo($"Изучено рас: {state.Races.Count}");
        menu.AddInfo($"Убийства: {stats.Kills}  •  Смерти: {stats.Deaths}");
        menu.AddInfo($"В голову: {stats.Headshots}");
        menu.AddInfo($"Раунды: {stats.RoundsPlayed}  •  Победы: {stats.RoundsWon}");
        menu.AddInfo($"В игре: {(int)playTime.TotalHours} ч {playTime.Minutes} мин");
        menu.Add("← Назад", _ => OpenMainMenu(player));

        menus.Open(player, menu);
    }

    private static void Print(CCSPlayerController player, string message)
        => player.PrintToChat($" [Warcraft] {message}");

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
