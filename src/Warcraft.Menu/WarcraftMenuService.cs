using System.Text;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;

namespace Warcraft.Menu;

internal sealed class WarcraftMenuService(BasePlugin plugin, WarcraftMenuConfig config)
{
    private sealed class ActiveMenuState
    {
        public required WarcraftUiMenu Menu { get; init; }
        public int SelectedIndex { get; set; }
    }

    private readonly Dictionary<ulong, ActiveMenuState> _active = [];

    public WarcraftUiMenu Create(string title, string? subtitle = null)
        => new(title, subtitle);

    public void Open(CCSPlayerController player, WarcraftUiMenu menu)
    {
        if (!IsHuman(player))
            return;

        var selected = FindInitialSelection(menu);
        _active[player.SteamID] = new ActiveMenuState
        {
            Menu = menu,
            SelectedIndex = selected
        };

        Render(player);
    }

    public bool Move(CCSPlayerController? player, int delta)
    {
        if (!TryGetState(player, out var state, out var controller))
            return false;

        if (state.Menu.Options.Count == 0)
            return false;

        var next = state.SelectedIndex;
        for (var i = 0; i < state.Menu.Options.Count; i++)
        {
            next = ((next + delta) % state.Menu.Options.Count + state.Menu.Options.Count) %
                   state.Menu.Options.Count;

            if (!state.Menu.Options[next].Disabled)
            {
                state.SelectedIndex = next;
                Render(controller);
                return true;
            }
        }

        return false;
    }

    public bool Select(CCSPlayerController? player)
    {
        if (!TryGetState(player, out var state, out var controller))
            return false;

        if (state.Menu.Options.Count == 0 ||
            state.SelectedIndex < 0 ||
            state.SelectedIndex >= state.Menu.Options.Count)
        {
            return false;
        }

        var option = state.Menu.Options[state.SelectedIndex];
        if (option.Disabled)
        {
            Render(controller);
            return false;
        }

        _active.Remove(controller.SteamID);
        MenuManager.CloseActiveMenu(controller);
        option.Handler(controller);
        return true;
    }

    public bool Close(CCSPlayerController? player)
    {
        if (!IsHuman(player))
            return false;

        _active.Remove(player!.SteamID);
        MenuManager.CloseActiveMenu(player);
        return true;
    }

    public void Forget(ulong steamId)
        => _active.Remove(steamId);

    private void Render(CCSPlayerController player)
    {
        if (!_active.TryGetValue(player.SteamID, out var state))
            return;

        var title = state.Menu.Subtitle is { Length: > 0 }
            ? $"{state.Menu.Title} | {state.Menu.Subtitle}"
            : state.Menu.Title;

        var rendered = new CenterHtmlMenu(Encode($"◆ {title} ◆"), plugin)
        {
            ExitButton = true,
            PostSelectAction = PostSelectAction.Nothing
        };

        for (var i = 0; i < state.Menu.Options.Count; i++)
        {
            var option = state.Menu.Options[i];
            var prefix = option.Disabled
                ? "×"
                : i == state.SelectedIndex && config.UseBindNavigation
                    ? "▶"
                    : "•";

            rendered.AddMenuOption(
                Encode($"{prefix} {option.Text}"),
                (controller, _) =>
                {
                    if (option.Disabled)
                        return;

                    _active.Remove(controller.SteamID);
                    option.Handler(controller);
                },
                option.Disabled);
        }

        if (config.ShowNavigationHints && config.UseBindNavigation)
        {
            rendered.AddMenuOption(
                Encode("────────────"),
                (_, _) => { },
                true);
            rendered.AddMenuOption(
                Encode("↑/↓ навигация  •  ENTER выбор  •  BACKSPACE закрыть"),
                (_, _) => { },
                true);
        }

        MenuManager.OpenCenterHtmlMenu(plugin, player, rendered);
    }

    private static int FindInitialSelection(WarcraftUiMenu menu)
    {
        for (var i = 0; i < menu.Options.Count; i++)
        {
            if (!menu.Options[i].Disabled)
                return i;
        }

        return 0;
    }

    private bool TryGetState(
        CCSPlayerController? player,
        out ActiveMenuState state,
        out CCSPlayerController controller)
    {
        state = null!;
        controller = null!;

        if (!IsHuman(player))
            return false;

        controller = player!;
        return _active.TryGetValue(controller.SteamID, out state!);
    }

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;

    private static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length * 2);

        foreach (var ch in value)
        {
            if (ch <= 127)
            {
                builder.Append(ch switch
                {
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '&' => "&amp;",
                    '"' => "&quot;",
                    _ => ch
                });
                continue;
            }

            builder.Append("&#x");
            builder.Append(((int)ch).ToString("X"));
            builder.Append(';');
        }

        return builder.ToString();
    }
}

internal sealed class WarcraftUiMenu(string title, string? subtitle = null)
{
    private readonly List<WarcraftUiMenuOption> _options = [];

    public string Title { get; } = title;
    public string? Subtitle { get; } = subtitle;
    public IReadOnlyList<WarcraftUiMenuOption> Options => _options;

    public WarcraftUiMenu Add(
        string text,
        Action<CCSPlayerController> handler,
        bool disabled = false)
    {
        _options.Add(new WarcraftUiMenuOption(text, handler, disabled));
        return this;
    }

    public WarcraftUiMenu AddInfo(string text)
        => Add(text, _ => { }, true);
}

internal sealed record WarcraftUiMenuOption(
    string Text,
    Action<CCSPlayerController> Handler,
    bool Disabled);
