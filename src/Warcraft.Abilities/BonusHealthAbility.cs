using CounterStrikeSharp.API;
using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

internal sealed class BonusHealthAbility : IAbilityHandler
{
    private IDisposable? _subscription;
    private IWarcraftApi? _api;

    public string Id => "bonus_health";

    public void Register(IWarcraftApi api)
    {
        _api = api;

        var result = api.Abilities.Register(new AbilityRegistration(
            Id,
            "warcraft.abilities",
            AbilityKind.Passive,
            "Adds maximum health on spawn."));

        if (!result.Success)
            throw new InvalidOperationException(result.Message);

        _subscription = api.Events.Subscribe<PlayerSpawnEvent>(OnPlayerSpawn);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _api?.Abilities.Unregister(Id, "warcraft.abilities");
        _api = null;
    }

    private void OnPlayerSpawn(PlayerSpawnEvent @event)
    {
        if (_api is null)
            return;

        var ability = _api.Abilities.GetUsableAbility(@event.SteamId, Id);
        if (ability is null || ability.Level <= 0)
            return;

        var bonusHealth = Math.Max(
            0,
            AbilityConfigReader.GetLevelInt(ability, "health"));

        if (bonusHealth == 0)
            return;

        var player = Utilities.GetPlayers()
            .FirstOrDefault(x =>
                x is { IsValid: true, IsBot: false } &&
                x.SteamID == @event.SteamId);

        var pawn = player?.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
            return;

        var targetHealth = 100 + bonusHealth;
        pawn.MaxHealth = targetHealth;
        pawn.Health = targetHealth;

        Utilities.SetStateChanged(
            pawn,
            "CBaseEntity",
            "m_iMaxHealth");

        Utilities.SetStateChanged(
            pawn,
            "CBaseEntity",
            "m_iHealth");
    }
}
