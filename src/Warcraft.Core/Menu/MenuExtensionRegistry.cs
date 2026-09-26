using Warcraft.Api.Menu;

namespace Warcraft.Core.Menu;

internal sealed class MenuExtensionRegistry : IMenuExtensionsApi
{
    private readonly Dictionary<string, MenuEntryRegistration> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();

    public bool Register(MenuEntryRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (string.IsNullOrWhiteSpace(registration.Id) ||
            string.IsNullOrWhiteSpace(registration.OwnerModule) ||
            string.IsNullOrWhiteSpace(registration.Section) ||
            string.IsNullOrWhiteSpace(registration.DisplayName))
        {
            return false;
        }

        lock (_sync)
            return _entries.TryAdd(registration.Id, registration);
    }

    public bool Unregister(string entryId, string ownerModule)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(entryId, out var existing) ||
                !string.Equals(
                    existing.OwnerModule,
                    ownerModule,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return _entries.Remove(entryId);
        }
    }

    public IReadOnlyCollection<MenuEntryDescriptor> GetEntries(
        string section,
        ulong steamId)
    {
        MenuEntryRegistration[] snapshot;

        lock (_sync)
        {
            snapshot = _entries.Values
                .Where(x => string.Equals(
                    x.Section,
                    section,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Order)
                .ThenBy(x => x.DisplayName)
                .ToArray();
        }

        return snapshot
            .Select(x => new MenuEntryDescriptor(
                x.Id,
                x.DisplayName,
                x.Order,
                x.IsEnabled?.Invoke(steamId) ?? true))
            .ToArray();
    }

    public bool Invoke(string entryId, ulong steamId)
    {
        MenuEntryRegistration? entry;

        lock (_sync)
            _entries.TryGetValue(entryId, out entry);

        if (entry is null || entry.IsEnabled?.Invoke(steamId) == false)
            return false;

        entry.OnSelected(steamId);
        return true;
    }
}
