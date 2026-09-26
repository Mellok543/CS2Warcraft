using CounterStrikeSharp.API.Core;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Base for passives that keep a pawn attribute at a configured value while the
/// ability is usable (so dynamic conditions are honoured) and restore the
/// default once it is not. Re-applied on every Core <see cref="GameTickEvent"/>
/// because the engine resets these attributes (spawn, damage slowdown, etc.).
/// </summary>
internal abstract class PawnAttributeAbility : AbilityHandler
{
    private readonly HashSet<ulong> _applied = [];

    protected override AbilityKind Kind => AbilityKind.Passive;

    protected override void Subscribe(IWarcraftEventBus events)
    {
        Track(events.Subscribe<GameTickEvent>(_ => RefreshAll()));
        Track(events.Subscribe<PlayerDeathEvent>(e => _applied.Remove(e.SteamId)));
        Track(events.Subscribe<PlayerSpawnEvent>(e => _applied.Remove(e.SteamId)));
    }

    /// <summary>Reads the target value from race config; null disables the effect.</summary>
    protected abstract float? ReadValue(PlayerAbilitySnapshot ability);

    protected abstract void Apply(CCSPlayerPawn pawn, float value);

    protected abstract void Reset(CCSPlayerPawn pawn);

    protected override void OnDisposed()
    {
        foreach (var player in GamePlayers.AllAliveHumans())
        {
            if (_applied.Contains(player.Controller.SteamID))
                Reset(player.Pawn);
        }

        _applied.Clear();
    }

    private void RefreshAll()
    {
        foreach (var player in GamePlayers.AllAliveHumans())
            Refresh(player);
    }

    private void Refresh(in LivePlayer player)
    {
        var steamId = player.Controller.SteamID;
        var ability = GetUsable(steamId);
        var value = ability is null ? null : ReadValue(ability);

        if (value is { } target)
        {
            Apply(player.Pawn, target);
            _applied.Add(steamId);
            return;
        }

        if (_applied.Remove(steamId))
            Reset(player.Pawn);
    }
}
