using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Passive field-of-view modifier for selected races.
/// Config: fov. Values are clamped to a comfortable gameplay range.
/// </summary>
internal sealed class FovAbility : AbilityHandler
{
    private const uint DefaultFov = 90;
    private readonly HashSet<ulong> _applied = [];

    public override string Id => "fov";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "Увеличивает поле зрения до {fov}.";
    protected override string DisplayName => "Обзор";

    protected override void Subscribe(IWarcraftEventBus events)
    {
        Track(events.Subscribe<GameTickEvent>(_ => RefreshAll()));
        Track(events.Subscribe<PlayerDeathEvent>(e => _applied.Remove(e.SteamId)));
    }

    protected override void OnDisposed()
    {
        foreach (var player in GamePlayers.AllAliveHumans())
        {
            if (_applied.Contains(player.Controller.SteamID))
                SetFov(player, DefaultFov);
        }

        _applied.Clear();
    }

    private void RefreshAll()
    {
        foreach (var player in GamePlayers.AllAliveHumans())
        {
            var steamId = player.Controller.SteamID;
            var ability = GetUsable(steamId);

            if (ability is null)
            {
                if (_applied.Remove(steamId))
                    SetFov(player, DefaultFov);

                continue;
            }

            var fov = (uint)Math.Clamp(
                AbilityConfigReader.GetLevelInt(ability, "fov", (int)DefaultFov),
                90,
                120);

            SetFov(player, fov);
            _applied.Add(steamId);
        }
    }

    private static void SetFov(in LivePlayer player, uint fov)
    {
        if (player.Controller.DesiredFOV == fov)
            return;

        player.Controller.DesiredFOV = fov;
        Utilities.SetStateChanged(
            player.Controller,
            "CBasePlayerController",
            "m_iDesiredFOV");
    }
}
