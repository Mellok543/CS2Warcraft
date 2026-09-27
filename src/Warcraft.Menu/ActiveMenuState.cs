using CounterStrikeSharp.API.Core;

namespace Warcraft.Menu;

internal sealed class ActiveMenuState
{
    public required CCSPlayerController Player { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required IReadOnlyList<WarcraftHudMenuOption> Options { get; init; }
    public int SelectedIndex { get; set; }
}

internal sealed record WarcraftHudMenuOption(
    string Text,
    Action<CCSPlayerController> OnSelect,
    bool IsDisabled = false,
    string? DisabledReason = null);
