using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Menu;
using Warcraft.Api.Modules;
using Warcraft.Shared;

namespace Warcraft.Menu;

[MinimumApiVersion(80)]
public sealed class WarcraftMenuPlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Menu";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription => "Panorama XML/CSS HUD menu for CS2Warcraft.";

    private IWarcraftApi? _api;
    private WarcraftMenuService? _menus;
    private IDisposable? _openRequestSubscription;
    private IDisposable? _notificationSubscription;
    private IDisposable? _stateSubscription;
    private IDisposable? _spawnSubscription;

    public override void Load(bool hotReload)
    {
        _menus = new WarcraftMenuService(message => Logger.LogInformation("{Message}", message));
        _menus.Start(this, hotReload);

        AddCommand("css_wc", "Open Warcraft menu", OnWarcraftCommand);
        AddCommand("css_races", "Open Warcraft race selection", OnRacesCommand);

        RegisterListener<Listeners.OnPlayerButtonsChanged>(_menus.HandleButtonsChanged);
        RegisterListener<Listeners.OnClientDisconnect>(_menus.HandleClientDisconnect);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = WarcraftCoreCapability.TryGet();
        if (_api is null)
        {
            Logger.LogError(WarcraftCoreCapability.UnavailableMessage, WarcraftCapabilityNames.CoreApi);
            return;
        }

        _openRequestSubscription = _api.Menu.SubscribeOpenRequests(HandleOpenRequest);
        _notificationSubscription = _api.Menu.SubscribeNotifications(HandleNotification);
        _stateSubscription = _api.Events.Subscribe<Warcraft.Api.Events.PlayerStateChangedEvent>(
            changed => RefreshProgressHud(changed.SteamId));
        _spawnSubscription = _api.Events.Subscribe<Warcraft.Api.Events.PlayerSpawnEvent>(
            spawned => RefreshProgressHud(spawned.SteamId));

        foreach (var player in _api.Players.GetLoadedPlayers())
            RefreshProgressHud(player.SteamId);

        _api.Modules.Register(new ModuleRegistration(
            "warcraft.menu",
            ModuleVersion,
            "Panorama XML/CSS Warcraft menu"));
    }

    public override void Unload(bool hotReload)
    {
        _openRequestSubscription?.Dispose();
        _openRequestSubscription = null;

        _notificationSubscription?.Dispose();
        _notificationSubscription = null;

        _stateSubscription?.Dispose();
        _stateSubscription = null;

        _spawnSubscription?.Dispose();
        _spawnSubscription = null;

        _api?.Modules.Unregister("warcraft.menu");
        _api = null;
        _menus?.Stop();
        _menus = null;
    }

    private void HandleOpenRequest(MenuOpenRequest request)
    {
        Server.NextFrame(() =>
        {
            var player = Utilities.GetPlayerFromSteamId(request.SteamId);
            if (player is { IsValid: true, IsBot: false })
                OpenExtensionPage(player, request.PageId);
        });
    }

    private void HandleNotification(MenuNotificationRequest notification)
    {
        Server.NextFrame(() =>
        {
            var player = Utilities.GetPlayerFromSteamId(notification.SteamId);
            if (player is not { IsValid: true, IsBot: false } || _menus is null)
                return;

            _menus.ShowNotification(
                player,
                notification.Heading,
                notification.Title,
                notification.Description,
                notification.Style,
                notification.DurationSeconds);
        });
    }

    private void OpenExtensionPage(CCSPlayerController player, string pageId)
    {
        var api = _api;
        var menus = _menus;
        if (api is null || menus is null)
            return;

        var page = api.Menu.GetPage(pageId, player.SteamID);
        if (page is null)
        {
            Print(player, "Раздел меню сейчас недоступен.");
            return;
        }

        var options = page.Items
            .Select(item => new WarcraftHudMenuOption(
                item.Text,
                _ => item.OnSelected(player.SteamID),
                !item.Enabled,
                item.DisabledReason))
            .ToList();

        if (!string.IsNullOrWhiteSpace(page.ParentPageId))
        {
            options.Add(new WarcraftHudMenuOption(
                "← Назад",
                _ =>
                {
                    if (string.Equals(page.ParentPageId, "root", StringComparison.OrdinalIgnoreCase))
                        OpenMainMenu(player);
                    else
                        OpenExtensionPage(player, page.ParentPageId);
                }));
        }

        menus.Open(player, page.Title, page.Subtitle, options);
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

    private void OpenMainMenu(CCSPlayerController player)
    {
        var api = _api;
        var menus = _menus;
        if (api is null || menus is null)
            return;

        var state = api.Players.Get(player.SteamID);
        var race = state?.ActiveRaceId is null ? null : api.Races.Get(state.ActiveRaceId);
        var subtitle = race is null
            ? "РАСА НЕ ВЫБРАНА"
            : $"{race.Name.ToUpperInvariant()} • УРОВЕНЬ {state!.Races.GetValueOrDefault(race.Id)?.Level ?? 1}";

        var options = new List<WarcraftHudMenuOption>
        {
            new(race is null ? "Текущая раса: не выбрана" : $"Текущая раса: {race.Name}",
                _ => OpenCurrentRaceMenu(player), race is null, "Сначала выберите расу"),
            new("Выбор расы", _ => OpenRaceMenu(player)),
            new("Прокачка способностей", _ => OpenAbilityMenu(player), race is null,
                "Прокачка доступна после выбора расы"),
            new("Профиль", _ => OpenProfileMenu(player)),
            new("Топ уровней", _ => OpenTopLevelsMenu(player))
        };

        foreach (var entry in api.Menu.GetEntries("root", player.SteamID))
        {
            var entryId = entry.Id;
            options.Add(new WarcraftHudMenuOption(
                entry.DisplayName,
                _ => api.Menu.Invoke(entryId, player.SteamID),
                !entry.Enabled,
                !entry.Enabled ? "Этот раздел сейчас недоступен" : null));
        }

        menus.Open(player, "WARCRAFT", subtitle, options);
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
        var options = new List<WarcraftHudMenuOption>();

        foreach (var (race, availability) in races)
        {
            var suffix = string.Equals(race.Id, current, StringComparison.OrdinalIgnoreCase)
                ? "  ✓"
                : availability.VipLocked ? "  [VIP]"
                : !availability.IsAvailable ? "  [ЗАКРЫТА]" : string.Empty;
            var raceId = race.Id;
            options.Add(new WarcraftHudMenuOption(race.Name + suffix, _ => OpenRacePreview(player, raceId)));
        }

        options.Add(new WarcraftHudMenuOption("← Назад", _ => OpenMainMenu(player)));
        menus.Open(player, "ВЫБОР РАСЫ", $"ОТКРЫТО {open}/{races.Length}", options);
    }

    private void OpenRacePreview(CCSPlayerController player, string raceId)
    {
        var api = _api;
        var menus = _menus;
        var race = api?.Races.Get(raceId);
        if (api is null || menus is null || race is null)
            return;

        var availability = api.Races.GetAvailability(player.SteamID, race.Id);
        var selected = string.Equals(api.Players.Get(player.SteamID)?.ActiveRaceId, race.Id, StringComparison.OrdinalIgnoreCase);
        var options = new List<WarcraftHudMenuOption>();

        options.Add(new WarcraftHudMenuOption(
            selected ? "✓ Уже выбрана"
            : availability.VipLocked ? "Только для VIP"
            : !availability.IsAvailable ? "Раса закрыта"
            : "Выбрать эту расу",
            _ =>
            {
                var result = api.Races.SelectRace(player.SteamID, race.Id, "menu");
                Print(player, result.Success ? $"Вы выбрали расу {race.Name}." : result.Message);
                OpenMainMenu(player);
            },
            selected || !availability.IsAvailable,
            availability.VipLocked ? "Эта раса доступна только VIP"
            : !availability.IsAvailable ? "Сначала выполните условия разблокировки"
            : selected ? "Эта раса уже выбрана" : null));

        foreach (var requirement in availability.Requirements)
        {
            options.Add(new WarcraftHudMenuOption(
                $"{(requirement.IsMet ? "✓" : "✗")} {requirement.Description}: {requirement.Current}/{requirement.Required}",
                _ => { }, true, requirement.IsMet ? "Условие выполнено" : "Условие ещё не выполнено"));
        }

        foreach (var ability in api.Abilities.GetRaceAbilities(race.Id))
        {
            options.Add(new WarcraftHudMenuOption(
                AbilityStatusText.PreviewTitle(ability), _ => { }, true, "Способность этой расы"));
        }

        options.Add(new WarcraftHudMenuOption("← Назад к расам", _ => OpenRaceMenu(player)));
        menus.Open(player, race.Name.ToUpperInvariant(), (race.Description ?? string.Empty).ToUpperInvariant(), options);
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
            OpenRaceMenu(player);
            return;
        }

        var race = api.Races.Get(state.ActiveRaceId);
        if (race is null || !state.Races.TryGetValue(race.Id, out var progress))
            return;

        var options = api.Abilities.GetPlayerAbilities(player.SteamID)
            .Select(status =>
            {
                var abilityId = status.AbilityId;
                return new WarcraftHudMenuOption(
                    AbilityStatusText.UpgradeLine(status),
                    _ => OpenAbilityDetails(player, abilityId));
            })
            .ToList();

        options.Add(new WarcraftHudMenuOption("← Назад", _ => OpenMainMenu(player)));
        menus.Open(player, "НАВЫКИ", $"{race.Name.ToUpperInvariant()} • ОЧКИ: {progress.SkillPoints}", options);
    }

    private void OpenAbilityDetails(CCSPlayerController player, string abilityId)
    {
        var api = _api;
        var menus = _menus;
        var status = api?.Abilities.GetPlayerAbilities(player.SteamID)
            .FirstOrDefault(x => string.Equals(x.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase));
        if (api is null || menus is null || status is null)
            return;

        var options = new List<WarcraftHudMenuOption>();
        foreach (var line in AbilityStatusText.DetailLines(status))
            options.Add(new WarcraftHudMenuOption(line, _ => { }, true, line));

        options.Add(new WarcraftHudMenuOption(
            status.CanUpgrade ? "Улучшить способность" : "Улучшение недоступно",
            _ =>
            {
                var result = api.Progress.UpgradeAbility(player.SteamID, abilityId);
                Print(player, $"{result.Message} {status.DisplayName}: {result.PreviousLevel}->{result.CurrentLevel}");
                OpenAbilityDetails(player, abilityId);
            },
            !status.CanUpgrade || !status.HandlerRegistered,
            !status.HandlerRegistered ? "Для способности не зарегистрирован обработчик"
            : !status.CanUpgrade ? AbilityStatusText.State(status) : null));

        options.Add(new WarcraftHudMenuOption("← Назад к навыкам", _ => OpenAbilityMenu(player)));
        menus.Open(player, status.DisplayName.ToUpperInvariant(), $"УРОВЕНЬ {status.Level}/{status.MaxLevel}", options);
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

        var options = new List<WarcraftHudMenuOption>
        {
            new($"XP расы: {progress.Xp}", _ => { }, true, "Текущий опыт этой расы"),
            new($"Очки навыков: {progress.SkillPoints}", _ => { }, true, "Свободные очки навыков")
        };

        foreach (var status in api.Abilities.GetPlayerAbilities(player.SteamID))
            options.Add(new WarcraftHudMenuOption(AbilityStatusText.InfoLine(status), _ => { }, true, AbilityStatusText.State(status)));

        options.Add(new WarcraftHudMenuOption("Прокачка способностей", _ => OpenAbilityMenu(player)));
        options.Add(new WarcraftHudMenuOption("← Назад", _ => OpenMainMenu(player)));
        menus.Open(player, race.Name.ToUpperInvariant(), $"УРОВЕНЬ {progress.Level}/{race.MaxLevel}", options);
    }

    private void OpenProfileMenu(CCSPlayerController player)
    {
        var api = _api;
        var menus = _menus;
        var state = api?.Players.Get(player.SteamID);
        if (api is null || state is null || menus is null)
            return;

        var stats = state.Stats;
        var playTime = TimeSpan.FromSeconds(stats.PlaySeconds);
        var activeRace = state.ActiveRaceId is null ? null : api.Races.Get(state.ActiveRaceId);
        var activeProgress = state.ActiveRaceId is null ? null : state.Races.GetValueOrDefault(state.ActiveRaceId);
        var totalRaceLevels = state.Races.Values.Sum(x => x.Level);
        var unlockedAchievements = api.Achievements.GetAll(player.SteamID).Values.Count(x => x.Unlocked);
        var winRate = stats.RoundsPlayed == 0 ? 0d : stats.RoundsWon * 100d / stats.RoundsPlayed;
        var kd = stats.Deaths == 0 ? stats.Kills : (double)stats.Kills / stats.Deaths;

        var options = new List<WarcraftHudMenuOption>
        {
            new(activeRace is null
                    ? "Активная раса: не выбрана"
                    : $"Активная раса: {activeRace.Name} • ур. {activeProgress?.Level ?? 1}",
                _ => { }, true, "Текущая выбранная раса"),
            new($"Общий XP: {state.GlobalXp} • уровни рас: {totalRaceLevels}",
                _ => { }, true, "Суммарный прогресс аккаунта"),
            new($"Расы с прогрессом: {state.Races.Count}",
                _ => { }, true, "Количество рас, в которых есть прогресс"),
            new($"Убийства: {stats.Kills} • смерти: {stats.Deaths} • K/D {kd:0.00}",
                _ => { }, true, "Боевая статистика"),
            new($"Headshots: {stats.Headshots}",
                _ => { }, true, "Убийства в голову"),
            new($"Победы: {stats.RoundsWon}/{stats.RoundsPlayed} • {winRate:0.0}%",
                _ => { }, true, "Процент выигранных раундов"),
            new($"Достижения открыто: {unlockedAchievements} • {state.AchievementCurrency} ✦",
                _ => api.Menu.RequestOpenPage("warcraft.achievements.page", player.SteamID),
                false, "Открыть достижения и магазин валюты"),
            new($"В игре: {(int)playTime.TotalHours} ч {playTime.Minutes} мин",
                _ => { }, true, "Общее время игры"),
            new("← Назад", _ => OpenMainMenu(player))
        };

        menus.Open(player, "ПРОФИЛЬ", player.PlayerName.ToUpperInvariant(), options);
    }

    private void OpenTopLevelsMenu(CCSPlayerController player)
    {
        var api = _api;
        var menus = _menus;
        if (api is null || menus is null)
            return;

        var leaders = api.Players.GetLoadedPlayers()
            .Select(state => new
            {
                State = state,
                TotalLevels = state.Races.Values.Sum(x => x.Level)
            })
            .OrderByDescending(x => x.TotalLevels)
            .ThenByDescending(x => x.State.GlobalXp)
            .ThenBy(x => x.State.Name)
            .Take(10)
            .ToArray();

        var options = new List<WarcraftHudMenuOption>();

        for (var index = 0; index < leaders.Length; index++)
        {
            var leader = leaders[index];
            options.Add(new WarcraftHudMenuOption(
                $"{index + 1}. {leader.State.Name} — {leader.TotalLevels} ур. • {leader.State.GlobalXp} XP",
                _ => { },
                true,
                "Сумма уровней всех рас игрока"));
        }

        if (leaders.Length == 0)
            options.Add(new WarcraftHudMenuOption("Нет игроков в рейтинге", _ => { }, true, "Рейтинг пока пуст"));

        options.Add(new WarcraftHudMenuOption("← Назад", _ => OpenMainMenu(player)));
        menus.Open(player, "ТОП УРОВНЕЙ", "ИГРОКИ ОНЛАЙН", options);
    }

    private void RefreshProgressHud(ulong steamId)
    {
        var api = _api;
        var menus = _menus;
        if (api is null || menus is null)
            return;

        var player = Utilities.GetPlayerFromSteamId(steamId);
        var state = api.Players.Get(steamId);
        if (player is not { IsValid: true, IsBot: false } || state?.ActiveRaceId is null)
        {
            if (player is { IsValid: true })
                menus.HideProgress(player);
            return;
        }

        var race = api.Races.Get(state.ActiveRaceId);
        if (race is null || !state.Races.TryGetValue(race.Id, out var progress))
        {
            menus.HideProgress(player);
            return;
        }

        var isMax = progress.Level >= race.MaxLevel;
        var required = isMax ? 0 : api.Progress.GetRequiredXpForLevel(progress.Level);
        menus.ShowProgress(player, race.Name, progress.Level, progress.Xp, required, isMax);
    }

    private static void Print(CCSPlayerController player, string message)
        => player.PrintToChat($" [Warcraft] {message}");

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
