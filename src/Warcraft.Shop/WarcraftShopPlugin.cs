using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Api.Menu;
using Warcraft.Api.Modules;
using Warcraft.Shared;
using Warcraft.Shop.Effects;

namespace Warcraft.Shop;

[MinimumApiVersion(80)]
public sealed class WarcraftShopPlugin : BasePlugin
{
    private const string ModuleId = "warcraft.shop";
    private const string MenuEntryId = "warcraft.shop.open";
    private const string AdminPermission = "@warcraft/admin";

    public override string ModuleName => "Warcraft.Shop";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription => "JSON-configured Warcraft item shop working through Warcraft.Core.";

    private readonly ShopEffectRegistry _effects = ShopEffectRegistry.CreateDefault();

    private IWarcraftApi? _api;
    private ShopService? _shop;
    private IDisposable? _roundStartSubscription;

    public override void Load(bool hotReload)
    {
        AddCommand("css_shop", "Open the Warcraft shop", OnShopCommand);
        AddCommand("css_wc_reload_shop", "Reload configs/warcraft/shop.json", OnReloadCommand);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = WarcraftCoreCapability.TryGet();
        if (_api is null)
        {
            Logger.LogError(WarcraftCoreCapability.UnavailableMessage, WarcraftCapabilityNames.CoreApi);
            return;
        }

        _shop = new ShopService(_api, _effects);
        new ShopCatalogLoader(_effects).EnsureDefault(ModuleDirectory);
        Reload();

        _roundStartSubscription = _api.Events.Subscribe<RoundStartEvent>(_ => _shop?.ResetRound());
        _api.Modules.Register(new ModuleRegistration(ModuleId, ModuleVersion, "Item shop"));
        _api.Menu.Register(new MenuEntryRegistration(MenuEntryId, ModuleId, "root", "Магазин", 40, OpenShop));
    }

    public override void Unload(bool hotReload)
    {
        _roundStartSubscription?.Dispose();
        _roundStartSubscription = null;

        if (_api is not null)
        {
            _api.Menu.Unregister(MenuEntryId, ModuleId);
            _api.Modules.Unregister(ModuleId);
        }

        _api = null;
        _shop = null;
    }

    private string Reload()
    {
        if (_shop is null)
            return "Магазин недоступен.";

        var loaded = new ShopCatalogLoader(_effects).Load();
        foreach (var error in loaded.Errors)
            Logger.LogError("Shop config error: {Error}", error);

        _shop.ReplaceItems(loaded.Items);
        Logger.LogInformation("Warcraft shop loaded {Count} items.", loaded.Items.Count);
        return $"Загружено предметов: {loaded.Items.Count}, ошибок: {loaded.Errors.Count}.";
    }

    private void OnShopCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is { IsValid: true, IsBot: false })
            OpenShop(player.SteamID);
    }

    private void OnReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null && !AdminManager.PlayerHasPermissions(player, AdminPermission))
        {
            command.ReplyToCommand($"[Warcraft] Требуется право {AdminPermission}.");
            return;
        }

        command.ReplyToCommand($"[Warcraft] {Reload()}");
    }

    private void OpenShop(ulong steamId)
    {
        var player = Utilities.GetPlayerFromSteamId(steamId);
        var shop = _shop;
        if (player is not { IsValid: true } || shop is null)
            return;

        var money = player.InGameMoneyServices?.Account ?? 0;
        var menu = new CenterHtmlMenu($"Магазин | ${money}", this) { ExitButton = true };

        if (shop.Items.Count == 0)
            menu.AddMenuOption("Магазин пуст", (_, _) => { }, true);

        foreach (var item in shop.Items)
        {
            var definition = item;
            var price = shop.GetPrice(steamId, definition);
            var limit = definition.MaxPerRound > 0
                ? $" ({shop.GetBoughtThisRound(steamId, definition)}/{definition.MaxPerRound})"
                : string.Empty;

            menu.AddMenuOption($"{definition.Name} — ${price}{limit}", (buyer, _) =>
            {
                if (definition.Description is { } description)
                    buyer.PrintToChat($" [Warcraft] {definition.Name}: {description}");

                var result = shop.Buy(buyer, definition);
                buyer.PrintToChat($" [Warcraft] {result.Message}");
                OpenShop(buyer.SteamID);
            });
        }

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
    }
}
