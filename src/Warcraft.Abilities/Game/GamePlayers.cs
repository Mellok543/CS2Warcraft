using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Warcraft.Abilities.Game;

internal readonly record struct LivePlayer(
    CCSPlayerController Controller,
    CCSPlayerPawn Pawn)
{
    public byte Team => Controller.TeamNum;

    public Vector3 Position
    {
        get
        {
            var origin = Pawn.AbsOrigin;
            return origin is null ? Vector3.Zero : new Vector3(origin.X, origin.Y, origin.Z);
        }
    }
}

/// <summary>Game-thread helpers for locating player entities by SteamID.</summary>
internal static class GamePlayers
{
    private const byte TeamTerrorist = 2;
    private const byte TeamCounterTerrorist = 3;

    public static LivePlayer? FindAlive(ulong steamId)
    {
        var controller = Utilities.GetPlayerFromSteamId(steamId);
        return controller is null ? null : AsAlive(controller);
    }

    /// <summary>All alive players (humans and bots) standing in a playing team.</summary>
    public static IEnumerable<LivePlayer> AllAlive()
    {
        foreach (var controller in Utilities.GetPlayers())
        {
            var live = AsAlive(controller);
            if (live is { } player && player.Team is TeamTerrorist or TeamCounterTerrorist)
                yield return player;
        }
    }

    public static bool AreEnemies(in LivePlayer first, in LivePlayer second)
        => first.Team != second.Team &&
           first.Team is TeamTerrorist or TeamCounterTerrorist &&
           second.Team is TeamTerrorist or TeamCounterTerrorist;

    private static LivePlayer? AsAlive(CCSPlayerController controller)
    {
        if (!controller.IsValid || !controller.PawnIsAlive)
            return null;

        var pawn = controller.PlayerPawn.Value;
        return pawn is { IsValid: true, Health: > 0 }
            ? new LivePlayer(controller, pawn)
            : null;
    }
}
