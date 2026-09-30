using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Api.Menu;
using Warcraft.Api.Modules;
using Warcraft.Shared;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace Warcraft.Cosmetics;

[MinimumApiVersion(80)]
public sealed class WarcraftCosmeticsPlugin : BasePlugin
{
    private const string ModuleId = "warcraft.cosmetics";
    private const string MenuEntryId = "warcraft.cosmetics.open";
    private const string RootPageId = "warcraft.cosmetics.page";

    public override string ModuleName => "Warcraft.Cosmetics";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription => "Persistent cosmetic inventory, equipment and player-attached models.";

    private readonly Dictionary<(ulong SteamId, string Slot), AttachedCosmetic> _props = [];
    private IWarcraftApi? _api;
    private bool _modelsEnabled = true;
    private IDisposable? _spawnSubscription;
    private IDisposable? _deathSubscription;
    private IDisposable? _stateSubscription;

    public override void Load(bool hotReload)
    {
        _modelsEnabled = VisualsSettings.ModelsEnabled(Logger);

        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (!_modelsEnabled)
                return;

            foreach (var definition in CosmeticCatalog.All)
                manifest.AddResource(definition.Model);
        });

        RegisterListener<Listeners.OnClientDisconnect>(RemovePlayer);
        RegisterListener<Listeners.CheckTransmit>(HideFromOwnView);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = WarcraftCoreCapability.TryGet();
        if (_api is null)
        {
            Logger.LogError(WarcraftCoreCapability.UnavailableMessage, WarcraftCapabilityNames.CoreApi);
            return;
        }

        _spawnSubscription = _api.Events.Subscribe<PlayerSpawnEvent>(OnSpawn);
        _deathSubscription = _api.Events.Subscribe<PlayerDeathEvent>(death => RemovePlayerBySteamId(death.SteamId));
        _stateSubscription = _api.Events.Subscribe<PlayerStateChangedEvent>(changed =>
        {
            if (changed.Reason.StartsWith("cosmetic:", StringComparison.OrdinalIgnoreCase))
                Refresh(changed.SteamId);
        });

        _api.Menu.RegisterPage(new MenuPageRegistration(RootPageId, ModuleId, BuildOverviewPage));
        foreach (var slot in new[] { CosmeticCatalog.Hat, CosmeticCatalog.Mask, CosmeticCatalog.Backpack, CosmeticCatalog.Pet })
        {
            var captured = slot;
            _api.Menu.RegisterPage(new MenuPageRegistration(PageId(captured), ModuleId, steamId => BuildSlotPage(steamId, captured)));
        }

        _api.Menu.Register(new MenuEntryRegistration(
            MenuEntryId,
            ModuleId,
            "root",
            "Косметика",
            35,
            steamId => _api.Menu.RequestOpenPage(RootPageId, steamId)));

        _api.Modules.Register(new ModuleRegistration(ModuleId, ModuleVersion, $"{CosmeticCatalog.All.Count} cosmetics"));

        if (hotReload)
        {
            foreach (var player in _api.Players.GetLoadedPlayers())
                Refresh(player.SteamId);
        }
    }

    public override void Unload(bool hotReload)
    {
        _spawnSubscription?.Dispose();
        _deathSubscription?.Dispose();
        _stateSubscription?.Dispose();

        if (_api is not null)
        {
            _api.Menu.Unregister(MenuEntryId, ModuleId);
            _api.Menu.UnregisterPage(RootPageId, ModuleId);
            foreach (var slot in new[] { CosmeticCatalog.Hat, CosmeticCatalog.Mask, CosmeticCatalog.Backpack, CosmeticCatalog.Pet })
                _api.Menu.UnregisterPage(PageId(slot), ModuleId);
            _api.Modules.Unregister(ModuleId);
        }

        foreach (var attached in _props.Values.ToArray())
            Remove(attached.Prop);
        _props.Clear();
        _api = null;
    }

    private void OnSpawn(PlayerSpawnEvent spawned)
    {
        var steamId = spawned.SteamId;
        AddTimer(0.25f, () => Refresh(steamId), TimerFlags.STOP_ON_MAPCHANGE);
    }

    private MenuPageDescriptor? BuildOverviewPage(ulong steamId)
    {
        var api = _api;
        if (api is null)
            return null;

        var equipped = api.Cosmetics.GetEquipped(steamId);
        var items = new List<MenuPageItemDescriptor>();

        foreach (var slot in new[] { CosmeticCatalog.Hat, CosmeticCatalog.Mask, CosmeticCatalog.Backpack, CosmeticCatalog.Pet })
        {
            var current = equipped.TryGetValue(slot, out var id)
                ? CosmeticCatalog.Find(id)?.Name ?? id
                : "не выбрано";

            var captured = slot;
            items.Add(new MenuPageItemDescriptor(
                $"{CosmeticCatalog.SlotName(slot)}: {current}",
                id2 => api.Menu.RequestOpenPage(PageId(captured), id2)));
        }

        return new MenuPageDescriptor(
            RootPageId,
            "КОСМЕТИКА",
            $"ПОЛУЧЕНО: {api.Cosmetics.GetOwned(steamId).Count}/{CosmeticCatalog.All.Count}",
            items,
            "root");
    }

    private MenuPageDescriptor? BuildSlotPage(ulong steamId, string slot)
    {
        var api = _api;
        if (api is null)
            return null;

        var owned = api.Cosmetics.GetOwned(steamId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var equipped = api.Cosmetics.GetEquipped(steamId);
        equipped.TryGetValue(slot, out var equippedId);

        var items = new List<MenuPageItemDescriptor>
        {
            new(
                string.IsNullOrWhiteSpace(equippedId) ? "✓ Ничего" : "Снять предмет",
                id =>
                {
                    api.Cosmetics.Unequip(id, slot, "menu");
                    api.Menu.RequestOpenPage(PageId(slot), id);
                })
        };

        foreach (var cosmetic in CosmeticCatalog.All.Where(x => x.Slot == slot))
        {
            var item = cosmetic;
            var isOwned = owned.Contains(item.Id);
            var isEquipped = string.Equals(equippedId, item.Id, StringComparison.OrdinalIgnoreCase);

            items.Add(new MenuPageItemDescriptor(
                $"{(isEquipped ? "✓ " : string.Empty)}{item.Name}{(!isOwned ? " [ЗАКРЫТО]" : string.Empty)}",
                id =>
                {
                    api.Cosmetics.Equip(id, slot, item.Id, "menu");
                    api.Menu.RequestOpenPage(PageId(slot), id);
                },
                isOwned,
                !isOwned ? "Получите предмет в магазине или за достижение" : null));
        }

        return new MenuPageDescriptor(
            PageId(slot),
            CosmeticCatalog.SlotName(slot).ToUpperInvariant(),
            "ВЫБЕРИТЕ ПРЕДМЕТ",
            items,
            RootPageId);
    }

    private void Refresh(ulong steamId)
    {
        RemovePlayerBySteamId(steamId);

        var api = _api;
        var player = Utilities.GetPlayerFromSteamId(steamId);
        var pawn = player?.PlayerPawn.Value;
        if (!_modelsEnabled ||
            api is null ||
            player is not { IsValid: true, IsBot: false, PawnIsAlive: true } ||
            pawn is not { IsValid: true })
        {
            return;
        }

        foreach (var (slot, cosmeticId) in api.Cosmetics.GetEquipped(steamId))
        {
            var definition = CosmeticCatalog.Find(cosmeticId);
            if (definition is null || !string.Equals(definition.Slot, slot, StringComparison.OrdinalIgnoreCase))
                continue;

            var prop = Attach(definition, pawn);
            if (prop is not null)
                _props[(steamId, slot)] = new AttachedCosmetic(prop, pawn.Index);
        }
    }

    private static CDynamicProp? Attach(CosmeticDefinition definition, CCSPlayerPawn pawn)
    {
        var origin = pawn.AbsOrigin;
        if (origin is null)
            return null;

        var mount = CosmeticCatalog.Mount(definition.Slot);
        var yaw = pawn.EyeAngles.Y;
        var radians = yaw * MathF.PI / 180f;
        var forward = new Vector3(MathF.Cos(radians), MathF.Sin(radians), 0);
        var right = new Vector3(MathF.Sin(radians), -MathF.Cos(radians), 0);

        // The view offset shrinks while crouching; fall back to the standing eye height before it is networked.
        var eyeHeight = pawn.ViewOffset.Z > 1f ? pawn.ViewOffset.Z : StandingEyeHeight;
        var position = new Vector3(origin.X, origin.Y, origin.Z + eyeHeight + mount.Up) +
                       forward * mount.Forward +
                       right * mount.Right;

        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic_override");
        if (prop is null)
            return null;

        prop.Collision.SolidType = SolidType_t.SOLID_NONE;
        prop.Render = Color.White;
        prop.DispatchSpawn();
        prop.SetModel(definition.Model);
        prop.Teleport(
            new Vector(position.X, position.Y, position.Z),
            new QAngle(0, yaw + mount.Yaw, 0),
            new Vector(0, 0, 0));

        // Follow the bone, keeping the placement above; without the attachment the prop
        // simply stays parented to the pawn.
        prop.AcceptInput("SetParent", pawn, prop, "!activator");
        prop.AcceptInput("SetParentAttachmentMaintainOffset", pawn, prop, mount.Attachment);
        return prop;
    }

    /// <summary>
    /// Players never see their own cosmetics (a mask would cover the camera), neither
    /// do spectators watching them in first person.
    /// </summary>
    private void HideFromOwnView(CCheckTransmitInfoList infoList)
    {
        if (_props.Count == 0)
            return;

        foreach (var (info, player) in infoList)
        {
            if (player is not { IsValid: true } || ViewedPawnIndex(player) is not { } viewed)
                continue;

            foreach (var ((steamId, _), attached) in _props)
            {
                if (!attached.Prop.IsValid)
                    continue;

                // Hide cosmetics both from the owner's first-person view and from
                // everyone else while the owner's invisibility ability is active.
                if (attached.OwnerPawnIndex == viewed ||
                    _api?.Abilities.GetUsableAbility(steamId, "invisibility") is not null)
                {
                    info.TransmitEntities.Remove(attached.Prop);
                }
            }
        }
    }

    private static uint? ViewedPawnIndex(CCSPlayerController player)
    {
        if (player.PawnIsAlive && player.PlayerPawn.Value is { IsValid: true } own)
            return own.Index;

        var services = player.Pawn.Value?.ObserverServices;
        if (services is null || services.ObserverMode != (byte)ObserverMode_t.OBS_MODE_IN_EYE)
            return null;

        var target = services.ObserverTarget;
        return target.IsValid ? target.Index : null;
    }

    private void RemovePlayer(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is not null && player.SteamID != 0)
            RemovePlayerBySteamId(player.SteamID);
    }

    private void RemovePlayerBySteamId(ulong steamId)
    {
        foreach (var key in _props.Keys.Where(x => x.SteamId == steamId).ToArray())
        {
            if (_props.Remove(key, out var attached))
                Remove(attached.Prop);
        }
    }

    private static void Remove(CDynamicProp? prop)
    {
        if (prop is { IsValid: true })
            prop.Remove();
    }

    private static string PageId(string slot) => RootPageId + "." + slot;

    private const float StandingEyeHeight = 64f;

    private sealed record AttachedCosmetic(CDynamicProp Prop, uint OwnerPawnIndex);
}
