using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>Passive: raises max health on spawn. Config: health.</summary>
internal sealed class BonusHealthAbility : AbilityHandler
{
    private const int BaseHealth = 100;

    public override string Id => "bonus_health";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "+{health} к максимальному здоровью при возрождении.";
    protected override string DisplayName => "Бонус здоровья";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerSpawnEvent>(OnPlayerSpawn));

    private void OnPlayerSpawn(PlayerSpawnEvent @event)
    {
        var ability = GetUsable(@event.SteamId);
        if (ability is null)
            return;

        var bonusHealth = Math.Max(0, AbilityConfigReader.GetLevelInt(ability, "health"));
        if (bonusHealth == 0)
            return;

        if (GamePlayers.FindAlive(@event.SteamId) is { } player)
            PlayerHealth.SetMaxAndCurrent(player.Pawn, BaseHealth + bonusHealth);
    }
}
