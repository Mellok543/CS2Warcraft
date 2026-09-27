using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Warcraft.Api.Menu;

namespace Warcraft.Menu;

internal sealed class WarcraftMenuService
{
    private const int VisibleOptionCount = 7;

    private readonly Dictionary<int, ActiveMenuState> _activeMenus = [];
    private readonly Dictionary<int, long> _notificationVersions = [];
    private readonly PanoramaMenuRenderer _renderer;

    public WarcraftMenuService(Action<string>? log = null)
    {
        _renderer = new PanoramaMenuRenderer(
            "panorama/layout/custom_game/warcraft_menu.xml",
            log);
    }

    public void Start(BasePlugin plugin, bool hotReload) => _renderer.Start(plugin, hotReload);

    public void Stop()
    {
        foreach (var state in _activeMenus.Values.ToArray())
        {
            if (state.Player.IsValid)
                _renderer.Hide(state.Player);
        }

        _activeMenus.Clear();
        _notificationVersions.Clear();
        _renderer.Stop();
    }

    public void Open(
        CCSPlayerController player,
        string title,
        string subtitle,
        IReadOnlyList<WarcraftHudMenuOption> options)
    {
        if (!IsHuman(player))
            return;

        var items = options.ToArray();
        var state = new ActiveMenuState
        {
            Player = player,
            Title = title,
            Subtitle = subtitle,
            Options = items,
            SelectedIndex = FindFirstSelectable(items)
        };

        _activeMenus[player.Slot] = state;

        if (!_renderer.EnsureReady())
        {
            _activeMenus.Remove(player.Slot);
            return;
        }

        _renderer.Show(player);
        Render(state);
    }

    public void Close(CCSPlayerController player)
    {
        _activeMenus.Remove(player.Slot);
        _renderer.Hide(player);
    }

    public void HandleButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (!_activeMenus.TryGetValue(player.Slot, out var state))
            return;

        if (pressed.HasFlag(PlayerButtons.Reload))
        {
            Close(player);
            return;
        }

        if (state.Options.Count == 0)
            return;

        if (pressed.HasFlag(PlayerButtons.Forward))
        {
            MoveSelection(state, -1);
            Render(state);
        }
        else if (pressed.HasFlag(PlayerButtons.Back))
        {
            MoveSelection(state, 1);
            Render(state);
        }
        else if (pressed.HasFlag(PlayerButtons.Moveleft))
        {
            MovePage(state, -1);
            Render(state);
        }
        else if (pressed.HasFlag(PlayerButtons.Moveright))
        {
            MovePage(state, 1);
            Render(state);
        }
        else if (pressed.HasFlag(PlayerButtons.Use))
        {
            SelectCurrent(state);
        }
    }

    public void HandleClientDisconnect(int playerSlot)
    {
        _activeMenus.Remove(playerSlot);
        _notificationVersions.Remove(playerSlot);
        _renderer.ForgetPlayer(playerSlot);
    }

    public void ShowNotification(
        CCSPlayerController player,
        string heading,
        string title,
        string description,
        MenuNotificationStyle style,
        float durationSeconds)
    {
        if (!IsHuman(player))
            return;

        var version = _notificationVersions.GetValueOrDefault(player.Slot) + 1;
        _notificationVersions[player.Slot] = version;

        _renderer.ShowNotification(
            player,
            heading,
            title,
            description,
            style.ToString().ToLowerInvariant());

        _ = HideNotificationLaterAsync(player.Slot, version, Math.Clamp(durationSeconds, 1f, 12f));
    }

    private async Task HideNotificationLaterAsync(int slot, long version, float durationSeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(durationSeconds));

        Server.NextFrame(() =>
        {
            if (_notificationVersions.GetValueOrDefault(slot) != version)
                return;

            _notificationVersions.Remove(slot);
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is { IsValid: true })
                _renderer.HideNotification(player);
        });
    }

    private void SelectCurrent(ActiveMenuState state)
    {
        if (state.SelectedIndex < 0 || state.SelectedIndex >= state.Options.Count)
            return;

        var option = state.Options[state.SelectedIndex];
        if (option.IsDisabled)
        {
            Render(state);
            return;
        }

        option.OnSelect(state.Player);

        if (_activeMenus.TryGetValue(state.Player.Slot, out var current) &&
            ReferenceEquals(current, state))
        {
            Close(state.Player);
        }
    }

    private void Render(ActiveMenuState state)
    {
        if (!state.Player.IsValid || !_renderer.IsReady)
            return;

        _renderer.SetText(state.Player, "wc_menu_title", state.Title);
        _renderer.SetText(state.Player, "wc_menu_subtitle", state.Subtitle);

        var selectedForWindow = Math.Max(0, state.SelectedIndex);
        var firstIndex = Math.Clamp(
            selectedForWindow - VisibleOptionCount / 2,
            0,
            Math.Max(0, state.Options.Count - VisibleOptionCount));

        for (var row = 0; row < VisibleOptionCount; row++)
        {
            var rowId = $"wc_menu_row_{row}";
            var textId = $"wc_menu_row_{row}_text";
            var optionIndex = firstIndex + row;

            if (optionIndex >= state.Options.Count)
            {
                _renderer.SetText(state.Player, textId, string.Empty);
                _renderer.SetClass(state.Player, rowId, "hidden", true);
                _renderer.SetClass(state.Player, rowId, "selected", false);
                _renderer.SetClass(state.Player, rowId, "disabled", false);
                continue;
            }

            var option = state.Options[optionIndex];
            _renderer.SetText(state.Player, textId, option.Text);
            _renderer.SetClass(state.Player, rowId, "hidden", false);
            _renderer.SetClass(state.Player, rowId, "selected", optionIndex == state.SelectedIndex);
            _renderer.SetClass(state.Player, rowId, "disabled", option.IsDisabled);
        }

        var page = state.Options.Count > VisibleOptionCount && state.SelectedIndex >= 0
            ? $"{state.SelectedIndex + 1}/{state.Options.Count}"
            : string.Empty;
        _renderer.SetText(state.Player, "wc_menu_page", page);

        var status = "W/S — выбор   A/D — страница   E — открыть   R — закрыть";
        if (state.SelectedIndex >= 0 && state.SelectedIndex < state.Options.Count)
        {
            var selected = state.Options[state.SelectedIndex];
            if (selected.IsDisabled)
            {
                status = string.IsNullOrWhiteSpace(selected.DisabledReason)
                    ? "Этот пункт сейчас недоступен"
                    : selected.DisabledReason!;
            }
        }

        _renderer.SetText(state.Player, "wc_menu_status", status);
    }

    private static int FindFirstSelectable(IReadOnlyList<WarcraftHudMenuOption> options)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (!options[i].IsDisabled)
                return i;
        }

        return options.Count > 0 ? 0 : -1;
    }

    private static void MoveSelection(ActiveMenuState state, int delta)
    {
        if (state.Options.Count == 0)
            return;

        var start = state.SelectedIndex < 0 ? 0 : state.SelectedIndex;
        var index = start;

        for (var i = 0; i < state.Options.Count; i++)
        {
            index = (index + delta + state.Options.Count) % state.Options.Count;
            if (!state.Options[index].IsDisabled)
            {
                state.SelectedIndex = index;
                return;
            }
        }

        state.SelectedIndex = start;
    }

    private static void MovePage(ActiveMenuState state, int direction)
    {
        if (state.Options.Count == 0)
            return;

        var current = Math.Max(0, state.SelectedIndex);
        var target = Math.Clamp(current + direction * VisibleOptionCount, 0, state.Options.Count - 1);

        if (!state.Options[target].IsDisabled)
        {
            state.SelectedIndex = target;
            return;
        }

        var step = direction >= 0 ? 1 : -1;
        for (var i = target; i >= 0 && i < state.Options.Count; i += step)
        {
            if (!state.Options[i].IsDisabled)
            {
                state.SelectedIndex = i;
                return;
            }
        }
    }

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
