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
    private const string CurrencyShopPageId = "warcraft.achievements.shop";

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

        _api.Menu.RegisterPage(new MenuPageRegistration(
            CurrencyShopPageId,
            ModuleId,
            BuildCurrencyShopPage));

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
        {
            _tracker.EvaluateSnapshot(player.SteamId);
            GrantAchievementCosmetics(player.SteamId);
        }

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
            _api.Menu.UnregisterPage(CurrencyShopPageId, ModuleId);

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
        GrantAchievementCosmetics(steamId);
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
            .ToList();

        items.Add(new MenuPageItemDescriptor(
            $"Магазин достижений • {api.Achievements.GetCurrency(steamId)} ✦",
            id => api.Menu.RequestOpenPage(CurrencyShopPageId, id)));

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
                : definition.Description +
                  (definition.CosmeticRewardName is { } rewardName
                      ? $" • Награда: {rewardName}"
                      : string.Empty);

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

        var reward = CurrencyReward(definition.Rarity);
        if (reward > 0)
            _api?.Achievements.AddCurrency(unlocked.SteamId, reward, definition.Id);

        if (definition.CosmeticRewardId is { } cosmeticId)
            _api?.Cosmetics.Unlock(unlocked.SteamId, cosmeticId, $"achievement:{definition.Id}");

        var shown = _api?.Menu.RequestNotification(new MenuNotificationRequest(
            unlocked.SteamId,
            $"ДОСТИЖЕНИЕ • {RarityName(definition.Rarity)}",
            definition.Name,
            definition.Description +
            (reward > 0 ? $"  •  +{reward} ✦" : string.Empty) +
            (definition.CosmeticRewardName is { } cosmeticReward ? $"  •  Получено: {cosmeticReward}" : string.Empty),
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

    private MenuPageDescriptor? BuildCurrencyShopPage(ulong steamId)
    {
        var api = _api;
        if (api is null)
            return null;

        var balance = api.Achievements.GetCurrency(steamId);
        var items = AchievementShopItems.Select(item =>
        {
            var owned = api.Cosmetics.Owns(steamId, item.CosmeticId);
            var enough = balance >= item.Price;

            return new MenuPageItemDescriptor(
                owned ? $"✓ {item.Name} — получено" : $"{item.Name} — {item.Price} ✦",
                buyerSteamId => BuyAchievementCosmetic(buyerSteamId, item),
                !owned && enough,
                owned
                    ? "Этот предмет уже получен"
                    : !enough ? $"Нужно {item.Price} ✦" : item.Description);
        }).ToArray();

        return new MenuPageDescriptor(
            CurrencyShopPageId,
            "МАГАЗИН ДОСТИЖЕНИЙ",
            $"БАЛАНС: {balance} ✦",
            items,
            RootPageId);
    }

    private void BuyAchievementCosmetic(ulong steamId, AchievementShopItem item)
    {
        var api = _api;
        if (api is null || api.Cosmetics.Owns(steamId, item.CosmeticId))
            return;

        if (!api.Achievements.SpendCurrency(steamId, item.Price, $"achievement-shop:{item.CosmeticId}"))
            return;

        var unlock = api.Cosmetics.Unlock(steamId, item.CosmeticId, "achievement-shop");
        if (!unlock.Success)
        {
            api.Achievements.AddCurrency(steamId, item.Price, $"achievement-shop-refund:{item.CosmeticId}");
            return;
        }

        api.Cosmetics.Equip(steamId, item.Slot, item.CosmeticId, "achievement-shop");
        api.Menu.RequestOpenPage(CurrencyShopPageId, steamId);
    }

    private void GrantAchievementCosmetics(ulong steamId)
    {
        var api = _api;
        if (api is null)
            return;

        var progress = api.Achievements.GetAll(steamId);
        foreach (var definition in _definitions)
        {
            if (definition.CosmeticRewardId is not { } cosmeticId ||
                !progress.TryGetValue(definition.Id, out var state) ||
                !state.Unlocked)
            {
                continue;
            }

            api.Cosmetics.Unlock(steamId, cosmeticId, $"achievement-backfill:{definition.Id}");
        }
    }

    private static readonly AchievementShopItem[] AchievementShopItems =
    [
        new("backpack_loot_sack", "Мешок добычи", "backpack", 70, "Постоянный рюкзак."),
        new("backpack_quiver", "Колчан", "backpack", 90, "Постоянный рюкзак."),
        new("hat_viking", "Шлем викинга", "hat", 75, "Постоянная шапка."),
        new("hat_wizard", "Шляпа мага", "hat", 100, "Постоянная шапка."),
        new("mask_kitsune", "Маска кицунэ", "mask", 80, "Постоянная маска."),
        new("mask_plague", "Маска чумного доктора", "mask", 100, "Постоянная маска."),
        new("pet_capybara", "Питомец: Капибара", "pet", 85, "Постоянный питомец."),
        new("pet_owl", "Питомец: Сова", "pet", 110, "Постоянный питомец.")
    ];

    private sealed record AchievementShopItem(
        string CosmeticId,
        string Name,
        string Slot,
        long Price,
        string Description);

    private static long CurrencyReward(AchievementRarity rarity)
        => rarity switch
        {
            AchievementRarity.Common => 5,
            AchievementRarity.Rare => 10,
            AchievementRarity.Epic => 25,
            AchievementRarity.Legendary => 50,
            AchievementRarity.Secret => 40,
            _ => 0
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
