using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Api.Modules;

namespace Warcraft.Races;

[MinimumApiVersion(80)]
public sealed class WarcraftRacesPlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Races";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "JSON race catalog loader and hot reload for CS2Warcraft.";

    private static PluginCapability<IWarcraftApi> CoreCapability { get; } =
        new(WarcraftCapabilityNames.CoreApi);

    private readonly RaceConfigLoader _loader = new();

    private IWarcraftApi? _api;
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private IDisposable? _reloadSubscription;
    private readonly object _reloadSync = new();

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = CoreCapability.Get();
        if (_api is null)
        {
            Logger.LogError(
                "Warcraft.Core capability '{Capability}' is unavailable.",
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

        StartWatcher();

        Logger.LogInformation(
            "Warcraft.Races watching {RaceDirectory}.",
            _loader.RaceDirectory);
    }

    public override void Unload(bool hotReload)
    {
        _watcher?.Dispose();
        _watcher = null;

        lock (_reloadSync)
        {
            _reloadTimer?.Dispose();
            _reloadTimer = null;
        }

        _reloadSubscription?.Dispose();
        _reloadSubscription = null;

        _api?.Modules.Unregister("warcraft.races");
        _api = null;
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
            return;
        }

        foreach (var error in errors)
            Logger.LogError("Race reload error: {Error}", error);
    }
}
