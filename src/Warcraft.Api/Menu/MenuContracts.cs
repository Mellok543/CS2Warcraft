namespace Warcraft.Api.Menu;

public interface IMenuExtensionsApi
{
    bool Register(MenuEntryRegistration registration);
    bool Unregister(string entryId, string ownerModule);
    IReadOnlyCollection<MenuEntryDescriptor> GetEntries(string section, ulong steamId);
    bool Invoke(string entryId, ulong steamId);

    bool RegisterPage(MenuPageRegistration registration);
    bool UnregisterPage(string pageId, string ownerModule);
    MenuPageDescriptor? GetPage(string pageId, ulong steamId);

    IDisposable SubscribeOpenRequests(Action<MenuOpenRequest> handler);
    bool RequestOpenPage(string pageId, ulong steamId);

    IDisposable SubscribeNotifications(Action<MenuNotificationRequest> handler);
    bool RequestNotification(MenuNotificationRequest notification);
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

public sealed record MenuPageRegistration(
    string Id,
    string OwnerModule,
    Func<ulong, MenuPageDescriptor?> Build);

public sealed record MenuPageDescriptor(
    string Id,
    string Title,
    string Subtitle,
    IReadOnlyList<MenuPageItemDescriptor> Items,
    string? ParentPageId = "root");

public sealed record MenuPageItemDescriptor(
    string Text,
    Action<ulong> OnSelected,
    bool Enabled = true,
    string? DisabledReason = null);

public sealed record MenuOpenRequest(
    string PageId,
    ulong SteamId);


public enum MenuNotificationStyle
{
    Common,
    Rare,
    Epic,
    Legendary,
    Secret
}

public sealed record MenuNotificationRequest(
    ulong SteamId,
    string Heading,
    string Title,
    string Description,
    MenuNotificationStyle Style = MenuNotificationStyle.Common,
    float DurationSeconds = 4.5f);
