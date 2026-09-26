namespace Warcraft.Api.Menu;

public interface IMenuExtensionsApi
{
    bool Register(MenuEntryRegistration registration);
    bool Unregister(string entryId, string ownerModule);
    IReadOnlyCollection<MenuEntryDescriptor> GetEntries(string section, ulong steamId);
    bool Invoke(string entryId, ulong steamId);
}

public sealed record MenuEntryRegistration(
    string Id,
    string OwnerModule,
    string Section,
    string DisplayName,
    int Order,
    Action<ulong> OnSelected,
    Func<ulong, bool>? IsEnabled = null);

public sealed record MenuEntryDescriptor(
    string Id,
    string DisplayName,
    int Order,
    bool Enabled);
