using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

internal sealed class CriticalStrikeAbility : IAbilityHandler
{
    private IDisposable? _subscription;
    private IWarcraftApi? _api;

    public string Id => "critical_strike";

    public void Register(IWarcraftApi api)
    {
        _api = api;

        var result = api.Abilities.Register(new AbilityRegistration(
            Id,
            "warcraft.abilities",
            AbilityKind.Passive,
            "Randomly multiplies outgoing damage."));

        if (!result.Success)
            throw new InvalidOperationException(result.Message);

        _subscription = api.Events.Subscribe<DamagePreEvent>(OnDamagePre);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _api?.Abilities.Unregister(Id, "warcraft.abilities");
        _api = null;
    }

    private void OnDamagePre(DamagePreEvent @event)
    {
        if (_api is null || !@event.AttackerSteamId.HasValue)
            return;

        var ability = _api.Abilities.GetPlayerAbility(
            @event.AttackerSteamId.Value,
            Id);

        if (ability is null || ability.Level <= 0)
            return;

        var chance = Math.Clamp(
            AbilityConfigReader.GetLevelDouble(ability, "chance"),
            0.0,
            1.0);

        if (Random.Shared.NextDouble() > chance)
            return;

        var multiplier = Math.Max(
            1.0,
            AbilityConfigReader.GetLevelDouble(
                ability,
                "damageMultiplier",
                1.0));

        @event.Damage *= (float)multiplier;
    }
}
