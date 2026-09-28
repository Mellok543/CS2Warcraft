using Warcraft.Api.Persistence;

namespace Warcraft.Core.Persistence;

internal sealed class PersistenceCoordinator : IPersistenceApi
{
    private IWarcraftStorageProvider? _provider;
    private readonly object _sync = new();

    internal event Action? FirstProviderRegistered;

    private bool _hasEverRegisteredProvider;

    public bool HasProvider
    {
        get
        {
            lock (_sync)
                return _provider is not null;
        }
    }

    public string? ProviderName
    {
        get
        {
            lock (_sync)
                return _provider?.ProviderName;
        }
    }

    public bool RegisterProvider(IWarcraftStorageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var raiseFirstRegistration = false;

        lock (_sync)
        {
            if (_provider is not null)
                return false;

            _provider = provider;

            if (!_hasEverRegisteredProvider)
            {
                _hasEverRegisteredProvider = true;
                raiseFirstRegistration = true;
            }
        }

        if (raiseFirstRegistration)
            FirstProviderRegistered?.Invoke();

        return true;
    }

    public bool UnregisterProvider(string providerName)
    {
        lock (_sync)
        {
            if (_provider is null ||
                !string.Equals(
                    _provider.ProviderName,
                    providerName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _provider = null;
            return true;
        }
    }

    public ValueTask<PlayerPersistenceDto?> LoadPlayerAsync(
        ulong steamId,
        CancellationToken cancellationToken = default)
        => GetProvider().LoadPlayerAsync(steamId, cancellationToken);

    public ValueTask SavePlayerAsync(
        PlayerPersistenceDto player,
        CancellationToken cancellationToken = default)
        => GetProvider().SavePlayerAsync(player, cancellationToken);

    public ValueTask<IReadOnlyList<PlayerLeaderboardEntry>> LoadLevelLeaderboardAsync(
        int limit,
        CancellationToken cancellationToken = default)
        => GetProvider().LoadLevelLeaderboardAsync(Math.Clamp(limit, 1, 100), cancellationToken);

    public ValueTask<IReadOnlyList<AdminPersistenceEntry>> LoadAdminsAsync(
        CancellationToken cancellationToken = default)
        => GetProvider().LoadAdminsAsync(cancellationToken);

    public ValueTask UpsertAdminAsync(
        AdminPersistenceEntry admin,
        CancellationToken cancellationToken = default)
        => GetProvider().UpsertAdminAsync(admin, cancellationToken);

    public ValueTask DeleteAdminAsync(
        ulong steamId,
        CancellationToken cancellationToken = default)
        => GetProvider().DeleteAdminAsync(steamId, cancellationToken);

    private IWarcraftStorageProvider GetProvider()
    {
        lock (_sync)
        {
            return _provider
                ?? throw new InvalidOperationException(
                    "No Warcraft persistence provider has been registered.");
        }
    }
}
