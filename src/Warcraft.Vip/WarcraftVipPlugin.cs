using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Menu;
using Warcraft.Api.Modules;
using Warcraft.Shared;

namespace Warcraft.Vip;

[MinimumApiVersion(80)]
public sealed class WarcraftVipPlugin : BasePlugin
{
    private const string ModuleId = "warcraft.vip";
    private const string MenuEntryId = "warcraft.vip.info";

    public override string ModuleName => "Warcraft.Vip";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "VIP modifiers (XP multiplier, VIP races, shop discount) registered through Warcraft.Core.";

    private IWarcraftApi? _api;
    private VipModifierProvider? _provider;

    public override void Load(bool hotReload)
    {
        AddCommand("css_vip", "Show Warcraft VIP status", OnVipCommand);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = WarcraftCoreCapability.TryGet();
        if (_api is null)
        {
            Logger.LogError(WarcraftCoreCapability.UnavailableMessage, WarcraftCapabilityNames.CoreApi);
            return;
        }

        var config = VipConfig.LoadOrCreate();
        _provider = new VipModifierProvider(config);

        if (!_api.Modifiers.RegisterProvider(_provider))
        {
            Logger.LogError("Modifier provider {Provider} is already registered.", VipModifierProvider.Id);
            _provider = null;
            return;
        }

        _api.Modules.Register(new ModuleRegistration(ModuleId, ModuleVersion, "VIP modifiers"));
        _api.Menu.Register(new MenuEntryRegistration(
            MenuEntryId,
            ModuleId,
            "root",
            "VIP",
            50,
            ShowInfo));

        Logger.LogInformation("Warcraft.Vip loaded. VIP flag: {Permission}", config.Permission);
    }

    public override void Unload(bool hotReload)
    {
        if (_api is not null)
        {
            _api.Menu.Unregister(MenuEntryId, ModuleId);
            _api.Modifiers.UnregisterProvider(VipModifierProvider.Id);
            _api.Modules.Unregister(ModuleId);
        }

        _api = null;
        _provider = null;
    }

    private void OnVipCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is { IsValid: true, IsBot: false })
            ShowInfo(player.SteamID);
    }

    private void ShowInfo(ulong steamId)
    {
        var player = Utilities.GetPlayerFromSteamId(steamId);
        if (player is not { IsValid: true } || _provider is null)
            return;

        var perks = _provider.VipModifiers;
        var isVip = _provider.IsVip(steamId);

        player.PrintToChat(isVip
            ? " [Warcraft] У вас VIP-статус. Бонусы:"
            : " [Warcraft] VIP-статуса нет. VIP получает:");
        player.PrintToChat($" [Warcraft] - опыт x{perks.XpMultiplier.ToString("0.##", CultureInfo.InvariantCulture)}");

        if (perks.BonusSkillPointsPerLevel > 0)
            player.PrintToChat($" [Warcraft] - +{perks.BonusSkillPointsPerLevel} очк. навыков за уровень");

        if (perks.CanAccessVipRaces)
            player.PrintToChat(" [Warcraft] - доступ к VIP-расам");

        if (perks.ShopDiscount > 0)
            player.PrintToChat($" [Warcraft] - скидка в магазине {perks.ShopDiscount * 100:0}%");
    }
}
