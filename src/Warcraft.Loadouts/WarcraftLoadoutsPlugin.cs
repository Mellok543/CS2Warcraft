using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Api.Modules;
using Warcraft.Shared;

namespace Warcraft.Loadouts;

[MinimumApiVersion(80)]
public sealed class WarcraftLoadoutsPlugin : BasePlugin
{
    private const string ModuleId = "warcraft.loadouts";
    private const string AdminPermission = "@warcraft/admin";

    public override string ModuleName => "Warcraft.Loadouts";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription => "Data-driven free weapon loadouts for selected races.";

    private IWarcraftApi? _api;
    private LoadoutConfig _config = new();
    private IDisposable? _spawnSubscription;

    public override void Load(bool hotReload)
    {
        Reload();
        AddCommand("css_wc_reload_loadouts", "Reload Warcraft race loadouts", OnReloadCommand);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = WarcraftCoreCapability.TryGet();
        if (_api is null)
        {
            Logger.LogError(WarcraftCoreCapability.UnavailableMessage, WarcraftCapabilityNames.CoreApi);
            return;
        }

        _spawnSubscription = _api.Events.Subscribe<PlayerSpawnEvent>(OnPlayerSpawn);
        _api.Modules.Register(new ModuleRegistration(ModuleId, ModuleVersion, "Race weapon loadouts"));
    }

    public override void Unload(bool hotReload)
    {
        _spawnSubscription?.Dispose();
        _spawnSubscription = null;

        _api?.Modules.Unregister(ModuleId);
        _api = null;
    }

    private void OnPlayerSpawn(PlayerSpawnEvent spawned)
    {
        var api = _api;
        var state = api?.Players.Get(spawned.SteamId);
        if (api is null || state?.ActiveRaceId is null ||
            !_config.Races.TryGetValue(state.ActiveRaceId, out var loadout) ||
            loadout.Items.Count == 0)
        {
            return;
        }

        var steamId = spawned.SteamId;
        AddTimer(
            0.25f,
            () => GiveLoadout(steamId, state.ActiveRaceId, loadout),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void GiveLoadout(ulong steamId, string expectedRaceId, RaceLoadout loadout)
    {
        var api = _api;
        var player = Utilities.GetPlayerFromSteamId(steamId);
        var current = api?.Players.Get(steamId);
        if (api is null || player is not { IsValid: true, IsBot: false, PawnIsAlive: true } ||
            !string.Equals(current?.ActiveRaceId, expectedRaceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var owned = player.PlayerPawn.Value?.WeaponServices?.MyWeapons
            .Select(x => x.Value)
            .Where(x => x is { IsValid: true })
            .Select(x => x!.DesignerName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        foreach (var item in loadout.Items)
        {
            if (!IsAllowedItem(item) || owned.Contains(item))
                continue;

            player.GiveNamedItem(item);
            owned.Add(item);
        }
    }

    private void OnReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null && !AdminManager.PlayerHasPermissions(player, AdminPermission))
        {
            command.ReplyToCommand("[Warcraft] Требуется право " + AdminPermission + ".");
            return;
        }

        Reload();
        command.ReplyToCommand("[Warcraft] Loadout-конфиг перезагружен.");
    }

    private void Reload()
    {
        _config = LoadoutConfig.LoadOrCreate(ModuleDirectory);

        var invalid = _config.Races
            .SelectMany(x => x.Value.Items.Select(item => (Race: x.Key, Item: item)))
            .Where(x => !IsAllowedItem(x.Item))
            .ToArray();

        foreach (var entry in invalid)
            Logger.LogWarning("Loadouts: invalid item {Item} for race {Race}.", entry.Item, entry.Race);

        Logger.LogInformation("Warcraft.Loadouts loaded {Count} configured races.", _config.Races.Count);
    }

    private static bool IsAllowedItem(string item)
        => !string.IsNullOrWhiteSpace(item) &&
           (item.StartsWith("weapon_", StringComparison.Ordinal) ||
            item.StartsWith("item_", StringComparison.Ordinal));
}
