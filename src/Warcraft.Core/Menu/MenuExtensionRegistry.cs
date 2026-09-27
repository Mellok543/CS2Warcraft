using Warcraft.Api.Menu;

namespace Warcraft.Core.Menu;

internal sealed class MenuExtensionRegistry : IMenuExtensionsApi
{
    private readonly Dictionary<string, MenuEntryRegistration> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, MenuPageRegistration> _pages =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<Action<MenuOpenRequest>> _openHandlers = [];
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
                !string.Equals(existing.OwnerModule, ownerModule, StringComparison.OrdinalIgnoreCase))
                return false;

            return _entries.Remove(entryId);
        }
    }

    public IReadOnlyCollection<MenuEntryDescriptor> GetEntries(string section, ulong steamId)
    {
        MenuEntryRegistration[] snapshot;

        lock (_sync)
        {
            snapshot = _entries.Values
                .Where(x => string.Equals(x.Section, section, StringComparison.OrdinalIgnoreCase))
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

    public bool RegisterPage(MenuPageRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (string.IsNullOrWhiteSpace(registration.Id) ||
            string.IsNullOrWhiteSpace(registration.OwnerModule))
            return false;

        lock (_sync)
            return _pages.TryAdd(registration.Id, registration);
    }

    public bool UnregisterPage(string pageId, string ownerModule)
    {
        lock (_sync)
        {
            if (!_pages.TryGetValue(pageId, out var existing) ||
                !string.Equals(existing.OwnerModule, ownerModule, StringComparison.OrdinalIgnoreCase))
                return false;

            return _pages.Remove(pageId);
        }
    }

    public MenuPageDescriptor? GetPage(string pageId, ulong steamId)
    {
        MenuPageRegistration? registration;

        lock (_sync)
            _pages.TryGetValue(pageId, out registration);

        return registration?.Build(steamId);
    }

    public IDisposable SubscribeOpenRequests(Action<MenuOpenRequest> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
            _openHandlers.Add(handler);

        return new Subscription(this, handler);
    }

    public bool RequestOpenPage(string pageId, ulong steamId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
            return false;

        Action<MenuOpenRequest>[] handlers;

        lock (_sync)
        {
            if (!_pages.ContainsKey(pageId))
                return false;

            handlers = _openHandlers.ToArray();
        }

        var request = new MenuOpenRequest(pageId, steamId);
        foreach (var handler in handlers)
            handler(request);

        return handlers.Length > 0;
    }

    private void Unsubscribe(Action<MenuOpenRequest> handler)
    {
        lock (_sync)
            _openHandlers.Remove(handler);
    }

    private sealed class Subscription(
        MenuExtensionRegistry owner,
        Action<MenuOpenRequest> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Unsubscribe(handler);
        }
    }
}
