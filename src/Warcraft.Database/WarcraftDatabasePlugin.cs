using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Shared;
using Warcraft.Api.Modules;

namespace Warcraft.Database;

[MinimumApiVersion(80)]
public sealed class WarcraftDatabasePlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Database";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "MySQL persistence provider for CS2Warcraft.";

    private IWarcraftApi? _api;
    private CancellationTokenSource? _lifetime;
    private Task? _initializationTask;

    public override void Load(bool hotReload)
    {
        _lifetime = new CancellationTokenSource();
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

        _initializationTask = InitializeAsync(_lifetime?.Token ?? CancellationToken.None);
    }

    public override void Unload(bool hotReload)
    {
        _lifetime?.Cancel();

        if (_api is not null)
        {
            _api.Persistence.UnregisterProvider(MySqlStorageProvider.Name);
            _api.Modules.Unregister("warcraft.database");
        }

        _lifetime?.Dispose();
        _lifetime = null;
        _api = null;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = DatabaseConfig.LoadOrCreate();

            if (!config.Enabled)
            {
                Logger.LogWarning(
                    "Warcraft.Database is disabled. Configure {ConfigPath} and set enabled=true.",
                    DatabaseConfig.ConfigPath);
                return;
            }

            var provider = new MySqlStorageProvider(config);
            await provider.EnsureSchemaAsync(cancellationToken);

            if (_api is null)
                return;

            if (!_api.Persistence.RegisterProvider(provider))
            {
                Logger.LogError(
                    "A Warcraft persistence provider is already registered.");
                return;
            }

            _api.Modules.Register(new ModuleRegistration(
                "warcraft.database",
                ModuleVersion,
                "MySQL persistence provider"));

            Logger.LogInformation(
                "Warcraft.Database connected to MySQL and registered provider {Provider}.",
                provider.ProviderName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.LogError(
                exception,
                "Warcraft.Database initialization failed.");
        }
    }
}
