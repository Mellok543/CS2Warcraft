using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Api.Menu;
using Warcraft.Api.Modules;
using Warcraft.Shared;

namespace Warcraft.Achievements;

[MinimumApiVersion(80)]
public sealed class WarcraftAchievementsPlugin : BasePlugin
{
    private const string ModuleId = "warcraft.achievements";
    private const string MenuEntryId = "warcraft.achievements.open";
    private const string RootPageId = "warcraft.achievements.page";

    public override string ModuleName => "Warcraft.Achievements";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "Persistent achievements, race mastery and Panorama achievement pages.";

    private IWarcraftApi? _api;
    private AchievementTracker? _tracker;
    private IDisposable? _unlockSubscription;
    private IDisposable? _catalogSubscription;
    private IReadOnlyList<AchievementDefinition> _definitions = [];

    public override void Load(bool hotReload)
    {
        AddCommand("css_achievements", "Open Warcraft achievements", OnAchievementsCommand);
        AddCommand("css_ach", "Open Warcraft achievements", OnAchievementsCommand);
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

        RefreshDefinitions();

        _tracker = new AchievementTracker(_api, () => _definitions);
        _unlockSubscription = _api.Events.Subscribe<AchievementUnlockedEvent>(OnUnlocked);
        _catalogSubscription = _api.Events.Subscribe<RaceCatalogReloadedEvent>(reload =>
        {
            if (reload.Success)
                RefreshDefinitions();
        });

        _api.Menu.RegisterPage(new MenuPageRegistration(
            RootPageId,
            ModuleId,
            BuildOverviewPage));

        foreach (var category in Enum.GetValues<AchievementCategory>())
        {
            var pageId = PageId(category);
            _api.Menu.RegisterPage(new MenuPageRegistration(
                pageId,
                ModuleId,
                steamId => BuildCategoryPage(steamId, category)));
        }

        _api.Menu.Register(new MenuEntryRegistration(
            MenuEntryId,
            ModuleId,
            "root",
            "Достижения",
            30,
            OpenAchievements));

        _api.Modules.Register(new ModuleRegistration(
            ModuleId,
            ModuleVersion,
            $"{_definitions.Count} achievements"));

        foreach (var player in _api.Players.GetLoadedPlayers())
            _tracker.EvaluateSnapshot(player.SteamId);

        Logger.LogInformation(
            "Warcraft.Achievements loaded {Count} achievements.",
            _definitions.Count);
    }

    public override void Unload(bool hotReload)
    {
        _unlockSubscription?.Dispose();
        _unlockSubscription = null;

        _catalogSubscription?.Dispose();
        _catalogSubscription = null;

        _tracker?.Dispose();
        _tracker = null;

        if (_api is not null)
        {
            _api.Menu.Unregister(MenuEntryId, ModuleId);
            _api.Menu.UnregisterPage(RootPageId, ModuleId);

            foreach (var category in Enum.GetValues<AchievementCategory>())
                _api.Menu.UnregisterPage(PageId(category), ModuleId);

            _api.Modules.Unregister(ModuleId);
        }

        _api = null;
        _definitions = [];
    }

    private void OnAchievementsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is { IsValid: true, IsBot: false })
            OpenAchievements(player.SteamID);
    }

    private void OpenAchievements(ulong steamId)
    {
        _tracker?.EvaluateSnapshot(steamId);
        _api?.Menu.RequestOpenPage(RootPageId, steamId);
    }

    private MenuPageDescriptor? BuildOverviewPage(ulong steamId)
    {
        var api = _api;
        if (api is null)
            return null;

        RefreshDefinitions();
        _tracker?.EvaluateSnapshot(steamId);

        var progress = api.Achievements.GetAll(steamId);
        var unlocked = _definitions.Count(x =>
            progress.TryGetValue(x.Id, out var state) && state.Unlocked);

        var items = Enum.GetValues<AchievementCategory>()
            .Select(category =>
            {
                var categoryDefinitions = _definitions.Where(x => x.Category == category).ToArray();
                var categoryUnlocked = categoryDefinitions.Count(x =>
                    progress.TryGetValue(x.Id, out var state) && state.Unlocked);

                return new MenuPageItemDescriptor(
                    $"{CategoryName(category)} — {categoryUnlocked}/{categoryDefinitions.Length}",
                    id => api.Menu.RequestOpenPage(PageId(category), id));
            })
            .ToArray();

        return new MenuPageDescriptor(
            RootPageId,
            "ДОСТИЖЕНИЯ",
            $"ОТКРЫТО {unlocked}/{_definitions.Count}",
            items,
            "root");
    }

    private MenuPageDescriptor? BuildCategoryPage(ulong steamId, AchievementCategory category)
    {
        var api = _api;
        if (api is null)
            return null;

        _tracker?.EvaluateSnapshot(steamId);
        var progress = api.Achievements.GetAll(steamId);
        var definitions = _definitions.Where(x => x.Category == category).ToArray();

        var items = definitions.Select(definition =>
        {
            progress.TryGetValue(definition.Id, out var state);
            state ??= new Warcraft.Api.Achievements.AchievementProgressSnapshot(0, false, null);

            var text = state.Unlocked
                ? $"✓ [{RarityName(definition.Rarity)}] {definition.Name}"
                : definition.Secret
                    ? $"? [СЕКРЕТНОЕ] ??? — {Math.Min(state.Progress, definition.Target)}/{definition.Target}"
                    : $"• [{RarityName(definition.Rarity)}] {definition.Name} — {Math.Min(state.Progress, definition.Target)}/{definition.Target}";

            var reason = definition.Secret && !state.Unlocked
                ? "Условие скрыто"
                : definition.Description;

            return new MenuPageItemDescriptor(
                text,
                _ => { },
                false,
                reason);
        }).ToArray();

        var unlocked = definitions.Count(x =>
            progress.TryGetValue(x.Id, out var state) && state.Unlocked);

        return new MenuPageDescriptor(
            PageId(category),
            CategoryName(category).ToUpperInvariant(),
            $"ОТКРЫТО {unlocked}/{definitions.Length}",
            items,
            RootPageId);
    }

    private void OnUnlocked(AchievementUnlockedEvent unlocked)
    {
        var definition = _definitions.FirstOrDefault(x =>
            string.Equals(x.Id, unlocked.AchievementId, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
            return;

        var player = Utilities.GetPlayerFromSteamId(unlocked.SteamId);
        if (player is not { IsValid: true })
            return;

        var shown = _api?.Menu.RequestNotification(new MenuNotificationRequest(
            unlocked.SteamId,
            $"ДОСТИЖЕНИЕ • {RarityName(definition.Rarity)}",
            definition.Name,
            definition.Description,
            NotificationStyle(definition.Rarity),
            5.0f)) == true;

        if (!shown)
        {
            player.PrintToChat(
                $" [Warcraft] ★ Достижение разблокировано: {definition.Name} [{RarityName(definition.Rarity)}]");
        }
    }

    private void RefreshDefinitions()
    {
        if (_api is not null)
            _definitions = AchievementCatalog.Build(_api);
    }

    private static string PageId(AchievementCategory category)
        => RootPageId + "." + category.ToString().ToLowerInvariant();

    private static string CategoryName(AchievementCategory category)
        => category switch
        {
            AchievementCategory.Combat => "Бой",
            AchievementCategory.Progression => "Прогресс",
            AchievementCategory.Teamwork => "Команда",
            AchievementCategory.Abilities => "Способности",
            AchievementCategory.Mastery => "Mastery рас",
            _ => category.ToString()
        };

    private static MenuNotificationStyle NotificationStyle(AchievementRarity rarity)
        => rarity switch
        {
            AchievementRarity.Common => MenuNotificationStyle.Common,
            AchievementRarity.Rare => MenuNotificationStyle.Rare,
            AchievementRarity.Epic => MenuNotificationStyle.Epic,
            AchievementRarity.Legendary => MenuNotificationStyle.Legendary,
            AchievementRarity.Secret => MenuNotificationStyle.Secret,
            _ => MenuNotificationStyle.Common
        };

    private static string RarityName(AchievementRarity rarity)
        => rarity switch
        {
            AchievementRarity.Common => "ОБЫЧНОЕ",
            AchievementRarity.Rare => "РЕДКОЕ",
            AchievementRarity.Epic => "ЭПИЧЕСКОЕ",
            AchievementRarity.Legendary => "ЛЕГЕНДАРНОЕ",
            AchievementRarity.Secret => "СЕКРЕТНОЕ",
            _ => rarity.ToString().ToUpperInvariant()
        };
}
