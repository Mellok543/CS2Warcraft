using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;

namespace Warcraft.Menu;

internal sealed class PanoramaMenuRenderer(
    string layoutResource,
    Action<string>? log = null)
{
    private const string RootPanelId = "wc_menu_root";
    private const string NotificationPanelId = "wc_notification_root";
    private readonly HashSet<int> _visiblePlayers = [];
    private CCSCustomHudLayout? _entity;

    public bool IsReady => _entity is { IsValid: true };

    public void Start(BasePlugin plugin, bool hotReload)
    {
        plugin.RegisterListener<Listeners.OnMapStart>(_ => Server.NextWorldUpdate(Spawn));
        if (hotReload)
            Server.NextWorldUpdate(Spawn);
    }

    public void Stop()
    {
        try
        {
            foreach (var slot in _visiblePlayers.ToArray())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                if (player is { IsValid: true })
                    Hide(player);
            }

            if (_entity is { IsValid: true })
                _entity.Remove();
        }
        catch (Exception exception)
        {
            log?.Invoke($"Warcraft.Menu HUD cleanup skipped: {exception.Message}");
        }

        _visiblePlayers.Clear();
        _entity = null;
    }

    public bool EnsureReady()
    {
        if (IsReady)
            return true;

        Spawn();
        return IsReady;
    }

    public void Show(CCSPlayerController player)
    {
        if (!IsReady || !player.IsValid || player.IsBot)
            return;

        _visiblePlayers.Add(player.Slot);
        SetClass(player, RootPanelId, "shown", true);
    }

    public void Hide(CCSPlayerController player)
    {
        if (!IsReady || !player.IsValid)
            return;

        SetClass(player, RootPanelId, "shown", false);
        _visiblePlayers.Remove(player.Slot);
    }

    public void ForgetPlayer(int slot) => _visiblePlayers.Remove(slot);

    public void ShowNotification(
        CCSPlayerController player,
        string heading,
        string title,
        string description,
        string styleClass)
    {
        if (!EnsureReady() || !player.IsValid || player.IsBot)
            return;

        SetText(player, "wc_notification_heading", heading);
        SetText(player, "wc_notification_title", title);
        SetText(player, "wc_notification_description", description);

        foreach (var style in new[] { "common", "rare", "epic", "legendary", "secret" })
            SetClass(player, NotificationPanelId, style, string.Equals(style, styleClass, StringComparison.OrdinalIgnoreCase));

        SetClass(player, NotificationPanelId, "shown", true);
    }

    public void HideNotification(CCSPlayerController player)
    {
        if (!IsReady || !player.IsValid)
            return;

        SetClass(player, NotificationPanelId, "shown", false);
    }

    public void SetText(CCSPlayerController player, string panelId, string value, string variable = "text")
    {
        if (!IsReady || !player.IsValid)
            return;

        _entity!.SetDialogVariableStringForPlayer(player, panelId, variable, value ?? string.Empty);
    }

    public void SetClass(CCSPlayerController player, string panelId, string className, bool enabled)
    {
        if (!IsReady || !player.IsValid)
            return;

        _entity!.SetHasClassForPlayer(player, panelId, className, enabled);
    }

    private void Spawn()
    {
        if (IsReady)
            return;

        try
        {
            var entity = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
            if (entity is null || !entity.IsValid)
            {
                log?.Invoke("Warcraft.Menu: failed to create custom_hud_layout.");
                return;
            }

            entity.StrLayout = layoutResource;
            entity.DispatchSpawn();
            _entity = entity;
            log?.Invoke($"Warcraft.Menu: Panorama HUD ready: {layoutResource}");
        }
        catch (Exception exception)
        {
            log?.Invoke($"Warcraft.Menu: HUD spawn failed: {exception.Message}");
        }
    }
}
