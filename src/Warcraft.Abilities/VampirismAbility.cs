using CounterStrikeSharp.API;
using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

internal sealed class VampirismAbility : IAbilityHandler
{
    private IDisposable? _subscription;
    private IWarcraftApi? _api;

    public string Id => "vampirism";

    public void Register(IWarcraftApi api)
    {
        _api = api;

        var result = api.Abilities.Register(new AbilityRegistration(
            Id,
            "warcraft.abilities",
            AbilityKind.Passive,
            "Heals the attacker for a percentage of damage dealt."));

        if (!result.Success)
            throw new InvalidOperationException(result.Message);

        _subscription = api.Events.Subscribe<DamagePostEvent>(OnDamagePost);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _api?.Abilities.Unregister(Id, "warcraft.abilities");
        _api = null;
    }

    private void OnDamagePost(DamagePostEvent @event)
    {
        if (_api is null ||
            !@event.AttackerSteamId.HasValue ||
            @event.AttackerSteamId.Value == @event.VictimSteamId ||
            @event.FinalDamage <= 0)
        {
            return;
        }

        var ability = _api.Abilities.GetPlayerAbility(
            @event.AttackerSteamId.Value,
            Id);

        if (ability is null || ability.Level <= 0)
            return;

        var percent = Math.Clamp(
            AbilityConfigReader.GetLevelDouble(ability, "percent"),
            0.0,
            1.0);

        if (percent <= 0)
            return;

        var player = Utilities.GetPlayers()
            .FirstOrDefault(x =>
                x is { IsValid: true, IsBot: false } &&
                x.SteamID == @event.AttackerSteamId.Value);

        var pawn = player?.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid || pawn.Health <= 0)
            return;

        var heal = Math.Max(
            1,
            (int)Math.Round(@event.FinalDamage * percent));

        pawn.Health = Math.Min(pawn.MaxHealth, pawn.Health + heal);

        Utilities.SetStateChanged(
            pawn,
            "CBaseEntity",
            "m_iHealth");
    }
}
