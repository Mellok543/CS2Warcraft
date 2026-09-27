using System.Globalization;
using System.Numerics;
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
    private const string PageId = "warcraft.vip.page";

    public override string ModuleName => "Warcraft.Vip";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "VIP modifiers (XP multiplier, VIP races, shop discount) registered through Warcraft.Core.";

    private IWarcraftApi? _api;
    private VipModifierProvider? _provider;
    private VipConfig? _config;
    private IDisposable? _jumpSubscription;
    private IDisposable? _spawnSubscription;
    private IDisposable? _roundSubscription;
    private readonly Dictionary<ulong, double> _nextBhopAt = [];
    private readonly HashSet<ulong> _moneyGrantedThisRound = [];

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
        _config = config;
        _provider = new VipModifierProvider(config);

        if (!_api.Modifiers.RegisterProvider(_provider))
        {
            Logger.LogError("Modifier provider {Provider} is already registered.", VipModifierProvider.Id);
            _provider = null;
            return;
        }

        _jumpSubscription = _api.Events.Subscribe<Warcraft.Api.Events.PlayerJumpEvent>(OnPlayerJump);
        _spawnSubscription = _api.Events.Subscribe<Warcraft.Api.Events.PlayerSpawnEvent>(OnPlayerSpawn);
        _roundSubscription = _api.Events.Subscribe<Warcraft.Api.Events.RoundStartEvent>(_ => _moneyGrantedThisRound.Clear());

        _api.Modules.Register(new ModuleRegistration(ModuleId, ModuleVersion, "VIP modifiers"));
        _api.Menu.RegisterPage(new MenuPageRegistration(PageId, ModuleId, BuildVipPage));
        _api.Menu.Register(new MenuEntryRegistration(MenuEntryId, ModuleId, "root", "VIP", 50, OpenVip));

        Logger.LogInformation("Warcraft.Vip loaded. VIP flag: {Permission}", config.Permission);
    }

    public override void Unload(bool hotReload)
    {
        _jumpSubscription?.Dispose();
        _spawnSubscription?.Dispose();
        _roundSubscription?.Dispose();
        _jumpSubscription = null;
        _spawnSubscription = null;
        _roundSubscription = null;
        _nextBhopAt.Clear();
        _moneyGrantedThisRound.Clear();
        if (_api is not null)
        {
            _api.Menu.Unregister(MenuEntryId, ModuleId);
            _api.Menu.UnregisterPage(PageId, ModuleId);
            _api.Modifiers.UnregisterProvider(VipModifierProvider.Id);
            _api.Modules.Unregister(ModuleId);
        }

        _api = null;
        _provider = null;
        _config = null;
    }

    private void OnVipCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is { IsValid: true, IsBot: false })
            OpenVip(player.SteamID);
    }

    private void OpenVip(ulong steamId)
        => _api?.Menu.RequestOpenPage(PageId, steamId);

    private MenuPageDescriptor? BuildVipPage(ulong steamId)
    {
        var provider = _provider;
        if (provider is null)
            return null;

        var perks = provider.VipModifiers;
        var isVip = provider.IsVip(steamId);

        var items = new List<MenuPageItemDescriptor>
        {
            Info(
                isVip ? "✓ VIP-статус активен" : "VIP-статус не активен",
                isVip ? "VIP-бонусы применяются" : "Ниже показаны доступные VIP-бонусы"),
            Info(
                "Опыт: x" + perks.XpMultiplier.ToString("0.##", CultureInfo.InvariantCulture),
                "Множитель получаемого опыта")
        };

        if (perks.BonusSkillPointsPerLevel > 0)
            items.Add(Info("+" + perks.BonusSkillPointsPerLevel + " очк. навыков за уровень", "Дополнительные очки при повышении уровня"));

        if (perks.CanAccessVipRaces)
            items.Add(Info("Доступ к VIP-расам", "Открывает специальные VIP-расы"));

        if (perks.ShopDiscount > 0)
            items.Add(Info("Скидка в магазине: " + (perks.ShopDiscount * 100).ToString("0") + "%", "Скидка применяется к ценам Warcraft Shop"));

        if (_config?.BonusBuyMoney > 0)
            items.Add(Info("+$" + _config.BonusBuyMoney + " к закупке", "Начисляется один раз за раунд при спавне"));

        if (_config?.BhopEnabled == true)
            items.Add(Info(
                "Bhop-буст: раз в " + _config.BhopCooldownSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " сек.",
                "Небольшой горизонтальный импульс на прыжке"));

        return new MenuPageDescriptor(
            PageId,
            "VIP",
            isVip ? "СТАТУС: АКТИВЕН" : "СТАТУС: НЕ АКТИВЕН",
            items,
            "root");
    }

    private void OnPlayerSpawn(Warcraft.Api.Events.PlayerSpawnEvent spawned)
    {
        var provider = _provider;
        var config = _config;
        if (provider is null || config is null || config.BonusBuyMoney <= 0 ||
            !provider.IsVip(spawned.SteamId) || !_moneyGrantedThisRound.Add(spawned.SteamId))
            return;

        var player = Utilities.GetPlayerFromSteamId(spawned.SteamId);
        var money = player?.InGameMoneyServices;
        if (player is not { IsValid: true } || money is null)
            return;

        money.Account = Math.Min(16000, money.Account + config.BonusBuyMoney);
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
    }

    private void OnPlayerJump(Warcraft.Api.Events.PlayerJumpEvent jumped)
    {
        var provider = _provider;
        var config = _config;
        if (provider is null || config is null || !config.BhopEnabled || !provider.IsVip(jumped.SteamId))
            return;

        var now = Server.CurrentTime;
        if (_nextBhopAt.GetValueOrDefault(jumped.SteamId) > now)
            return;

        var player = Utilities.GetPlayerFromSteamId(jumped.SteamId);
        var pawn = player?.PlayerPawn.Value;
        var current = pawn?.AbsVelocity;
        if (player is not { IsValid: true, PawnIsAlive: true } || pawn is not { IsValid: true } || current is null)
            return;

        var speed = MathF.Sqrt(current.X * current.X + current.Y * current.Y);
        if (speed < 1f)
            return;

        var targetSpeed = Math.Min(
            speed * (float)Math.Max(1.0, config.BhopHorizontalMultiplier),
            (float)Math.Max(1.0, config.BhopMaxHorizontalSpeed));
        var scale = targetSpeed / speed;

        pawn.Teleport(velocity: new Vector3(current.X * scale, current.Y * scale, current.Z));
        _nextBhopAt[jumped.SteamId] = now + Math.Max(0.1, config.BhopCooldownSeconds);
    }

    private static MenuPageItemDescriptor Info(string text, string reason)
        => new(text, _ => { }, false, reason);
}
