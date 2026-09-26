namespace Warcraft.Api.Players;

public interface IPlayersApi
{
    PlayerStateSnapshot? Get(ulong steamId);
    IReadOnlyCollection<PlayerStateSnapshot> GetLoadedPlayers();
}
