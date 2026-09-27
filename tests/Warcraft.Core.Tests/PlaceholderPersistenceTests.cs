using Warcraft.Api.Persistence;

namespace Warcraft.Core.Tests;

/// <summary>
/// A connecting player gets a placeholder runtime state while storage loads
/// asynchronously. Saving that placeholder would reset XP and prune every stored
/// race, ability and achievement, so it must never be persisted.
/// </summary>
public sealed class PlaceholderPersistenceTests
{
    private const ulong Player = 31;

    [Fact]
    public void PlaceholderIsNeverPersisted()
    {
        var core = new TestCore();
        core.Players.Upsert(Player, "p");
        core.Players.GetRequired(Player).GlobalXp = 25; // XP earned before the load finished

        Assert.Null(core.Players.GetPersistenceSnapshot(Player));
        Assert.Empty(core.Players.GetPersistenceSnapshots());
    }

    [Fact]
    public void RestoredPlayerIsPersisted()
    {
        var core = new TestCore();
        core.Players.Upsert(Player, "p");

        core.Players.RestoreIfLoaded(
            new PlayerPersistenceDto { SteamId = Player, Name = "p", GlobalXp = 900 },
            "p");

        Assert.Equal(900, core.Players.GetPersistenceSnapshot(Player)!.GlobalXp);
        Assert.Single(core.Players.GetPersistenceSnapshots());
    }

    [Fact]
    public void NewPlayerConfirmedByStorageIsPersisted()
    {
        var core = new TestCore();
        core.Players.Upsert(Player, "p");

        Assert.True(core.Players.MarkLoaded(Player));
        Assert.NotNull(core.Players.GetPersistenceSnapshot(Player));
        Assert.False(core.Players.MarkLoaded(Player + 1));
    }
}
