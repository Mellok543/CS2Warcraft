using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Shared;
using Warcraft.Api.Events;
using Warcraft.Api.Modules;

namespace Warcraft.Races;

[MinimumApiVersion(80)]
public sealed class WarcraftRacesPlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Races";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "JSON race catalog loader and hot reload for CS2Warcraft.";

    private readonly RaceConfigLoader _loader = new();

    private IWarcraftApi? _api;
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private IDisposable? _reloadSubscription;
    private IDisposable? _stateSubscription;
    private IDisposable? _spawnSubscription;
    private readonly object _reloadSync = new();
    private bool _unloaded;

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
            "warcraft.races",
            ModuleVersion,
            "JSON race catalog loader"));

        _loader.EnsureDefaultConfigs(ModuleDirectory);
        ReloadCatalog("plugin-load");

        _reloadSubscription = _api.Events.Subscribe<RaceReloadRequestedEvent>(
            request => ReloadCatalog(request.RequestedBy));
        _stateSubscription = _api.Events.Subscribe<PlayerStateChangedEvent>(
            changed => UpdateScoreboardTag(changed.SteamId));
        _spawnSubscription = _api.Events.Subscribe<PlayerSpawnEvent>(
            spawned => UpdateScoreboardTag(spawned.SteamId));

        foreach (var player in _api.Players.GetLoadedPlayers())
            UpdateScoreboardTag(player.SteamId);

        StartWatcher();

        Logger.LogInformation(
            "Warcraft.Races watching {RaceDirectory}.",
            _loader.RaceDirectory);
    }

    public override void Unload(bool hotReload)
    {
        lock (_reloadSync)
        {
            // A watcher callback racing with unload must not create a new timer afterwards.
            _unloaded = true;
            _reloadTimer?.Dispose();
            _reloadTimer = null;
        }

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnRaceFileChanged;
            _watcher.Created -= OnRaceFileChanged;
            _watcher.Deleted -= OnRaceFileChanged;
            _watcher.Renamed -= OnRaceFileChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        _reloadSubscription?.Dispose();
        _stateSubscription?.Dispose();
        _spawnSubscription?.Dispose();
        _reloadSubscription = null;
        _stateSubscription = null;
        _spawnSubscription = null;

        foreach (var player in Utilities.GetPlayers())
        {
            if (player is { IsValid: true, IsBot: false } && player.SteamID != 0)
            {
                player.Clan = string.Empty;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
            }
        }

        _api?.Modules.Unregister("warcraft.races");
        _api = null;
    }

    private void UpdateScoreboardTag(ulong steamId)
    {
        var api = _api;
        var player = Utilities.GetPlayerFromSteamId(steamId);
        if (api is null || player is not { IsValid: true, IsBot: false })
            return;

        var state = api.Players.Get(steamId);
        var tag = state?.ActiveRaceId is { } raceId
            ? api.Races.Get(raceId)?.Name ?? raceId
            : string.Empty;

        // Clan is the native scoreboard tag column; keep names untouched.
        if (player.Clan == tag)
            return;

        player.Clan = tag;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
    }

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(_loader.RaceDirectory, "*.json")
        {
            IncludeSubdirectories = false,
            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.CreationTime |
                NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnRaceFileChanged;
        _watcher.Created += OnRaceFileChanged;
        _watcher.Deleted += OnRaceFileChanged;
        _watcher.Renamed += OnRaceFileChanged;
    }

    private void OnRaceFileChanged(object sender, FileSystemEventArgs args)
    {
        lock (_reloadSync)
        {
            if (_unloaded)
                return;

            _reloadTimer?.Dispose();
            // Timer callbacks run on the thread pool; Core APIs and events belong on the game thread.
            _reloadTimer = new Timer(
                _ => Server.NextFrame(() => ReloadCatalog("filesystem")),
                null,
                TimeSpan.FromMilliseconds(500),
                Timeout.InfiniteTimeSpan);
        }
    }

    private void ReloadCatalog(string source)
    {
        var api = _api;
        if (api is null)
            return;

        var loaded = _loader.LoadAll();

        if (loaded.Errors.Count > 0)
        {
            PublishResult(
                api,
                false,
                api.Races.GetAll().Count,
                loaded.Errors,
                source);
            return;
        }

        var result = api.Races.ReplaceCatalog(loaded.Races, source);

        PublishResult(
            api,
            result.Success,
            result.RaceCount,
            result.Errors,
            source);
    }

    private void LogCatalogHealth()
    {
        var health = _api?.Diagnostics.GetRaceCatalogHealth();
        if (health is null)
            return;

        Logger.LogInformation(
            "Race catalog health: {Total} races, starter [{Starter}], locked {Locked}, VIP [{Vip}].",
            health.TotalRaces,
            string.Join(", ", health.StarterRaces),
            health.LockedRaces.Count,
            string.Join(", ", health.VipRaces));

        foreach (var issue in health.UnresolvedHandlers)
        {
            Logger.LogWarning(
                "Race '{Race}' uses {Kind} '{Ability}' but no handler is registered (missing Warcraft.Abilities?).",
                issue.RaceId,
                issue.IsUltimate ? "ultimate" : "ability",
                issue.AbilityId);
        }

        foreach (var issue in health.PassiveUltimates)
        {
            Logger.LogWarning(
                "Race '{Race}' uses passive mechanic '{Ability}' as ultimate; it can never be activated.",
                issue.RaceId,
                issue.AbilityId);
        }
    }

    private void PublishResult(
        IWarcraftApi api,
        bool success,
        int raceCount,
        IReadOnlyList<string> errors,
        string source)
    {
        api.Events.Publish(new RaceCatalogReloadedEvent(
            success,
            raceCount,
            errors,
            source));

        if (success)
        {
            Logger.LogInformation(
                "Race catalog reloaded from {Source}. Races: {RaceCount}.",
                source,
                raceCount);

            // Ability handlers may register after us during startup: report on the next frame.
            Server.NextFrame(LogCatalogHealth);
            return;
        }

        foreach (var error in errors)
            Logger.LogError("Race reload error: {Error}", error);
    }
}
