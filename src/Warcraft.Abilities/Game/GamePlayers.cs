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

    /// <summary>Alive human players — the only players that own Warcraft progress.</summary>
    public static IEnumerable<LivePlayer> AllAliveHumans()
        => AllAlive().Where(x => !x.Controller.IsBot && x.Controller.SteamID != 0);

    public static LivePlayer? FindAliveBySlot(int slot)
    {
        var controller = Utilities.GetPlayerFromSlot(slot);
        return controller is null ? null : AsAlive(controller);
    }

    public static bool AreEnemies(in LivePlayer first, in LivePlayer second)
        => first.Team != second.Team &&
           first.Team is TeamTerrorist or TeamCounterTerrorist &&
           second.Team is TeamTerrorist or TeamCounterTerrorist;

    public static IEnumerable<LivePlayer> EnemiesAround(LivePlayer owner, Vector3 center, float radius)
        => AllAlive().Where(x => AreEnemies(owner, x) && Vector3.Distance(center, x.Position) <= radius);

    /// <summary>Teammates in range, including the owner.</summary>
    public static IEnumerable<LivePlayer> AlliesAround(LivePlayer owner, Vector3 center, float radius)
        => AllAlive().Where(x => x.Team == owner.Team && Vector3.Distance(center, x.Position) <= radius);

    public static LivePlayer? NearestEnemy(LivePlayer owner, float range)
    {
        var origin = owner.Position;
        return EnemiesAround(owner, origin, range)
            .OrderBy(x => Vector3.DistanceSquared(origin, x.Position))
            .Cast<LivePlayer?>()
            .FirstOrDefault();
    }

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
