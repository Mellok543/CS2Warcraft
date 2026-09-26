using Warcraft.Api.Persistence;

namespace Warcraft.Core.Persistence;

internal sealed class PersistenceCoordinator : IPersistenceApi
{
    private IWarcraftStorageProvider? _provider;
    private readonly object _sync = new();

    public bool HasProvider
    {
        get { lock (_sync) return _provider is not null; }
    }

    public bool RegisterProvider(IWarcraftStorageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_sync)
        {
            if (_provider is not null)
                return false;

            _provider = provider;
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

    private IWarcraftStorageProvider GetProvider()
    {
        lock (_sync)
            return _provider ?? throw new InvalidOperationException(
                "No Warcraft persistence provider has been registered.");
    }
}
