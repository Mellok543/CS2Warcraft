using System.Globalization;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;
using Warcraft.Core.Races;

namespace Warcraft.Core.Abilities;

internal sealed record AbilityActivationResult(bool Success, string? Message);

/// <summary>
/// Central activation pipeline for active abilities and ultimates:
/// resolve ability → verify level/unlock/conditions → verify cooldown →
/// publish the pressed event to the owning handler → start cooldown on success.
/// </summary>
internal sealed class AbilityActivationService(
    AbilityResolver resolver,
    AbilityRegistrationStore registrations,
    CooldownService cooldowns,
    IWarcraftEventBus events)
{
    public AbilityActivationResult ActivateUltimate(ulong steamId)
    {
        var ultimate = resolver.GetUltimate(steamId);
        if (ultimate is null)
            return Fail("У вашей расы нет ультимейта.");

        return Activate(
            steamId,
            ultimate,
            snapshot => new UltimatePressedEvent { SteamId = steamId, Ability = snapshot });
    }

    public AbilityActivationResult ActivateAbility(ulong steamId, int slot)
    {
        var ability = resolver.GetActiveSlot(steamId, slot);
        if (ability is null)
            return Fail($"У вашей расы нет активной способности в слоте {slot}.");

        return Activate(
            steamId,
            ability,
            snapshot => new AbilityPressedEvent { SteamId = steamId, Ability = snapshot, Slot = slot });
    }

    private AbilityActivationResult Activate(
        ulong steamId,
        CompiledAbility ability,
        Func<PlayerAbilitySnapshot, AbilityActivationEvent> createEvent)
    {
        var name = resolver.GetDisplayName(ability);

        // Any activatable mechanic (Active or Ultimate) may sit in either slot.
        if (registrations.Get(ability.Id)?.Kind is null or AbilityKind.Passive)
            return Fail($"Способность «{name}» не реализована на сервере как активная.");

        var usability = resolver.Resolve(steamId, ability, ability.Id, out var snapshot);
        if (usability != AbilityUsability.Usable || snapshot is null)
            return Fail(Describe(usability, name, ability));

        var remaining = cooldowns.GetRemaining(steamId, ability.Id);
        if (remaining > TimeSpan.Zero)
            return Fail($"«{name}» перезаряжается: {FormatSeconds(remaining)} с.");

        var activation = createEvent(snapshot);
        events.Publish(activation);

        switch (activation.Outcome)
        {
            case AbilityActivationOutcome.Succeeded:
                var cooldown = cooldowns.TryStart(steamId, ability.Id, snapshot.Cooldown);
                events.Publish(new AbilityActivatedEvent(
                    steamId,
                    ability.Id,
                    ability.IsUltimate,
                    cooldown.ReadyAt));
                return new(true, activation.Message);

            case AbilityActivationOutcome.Failed:
                return Fail(activation.Message ?? $"«{name}» не сработала.");

            default:
                return Fail($"Способность «{name}» не обработана.");
        }
    }

    private static string Describe(AbilityUsability usability, string name, CompiledAbility ability)
        => usability switch
        {
            AbilityUsability.PlayerNotLoaded => "Ваш профиль ещё загружается.",
            AbilityUsability.NoActiveRace => "Сначала выберите расу.",
            AbilityUsability.NotInActiveRace => $"«{name}» отсутствует у активной расы.",
            AbilityUsability.NotLearned => $"«{name}» ещё не изучена.",
            AbilityUsability.Locked =>
                $"«{name}» откроется на уровне расы {ability.Definition.UnlockLevel}.",
            AbilityUsability.ConditionsNotMet => $"Условия для «{name}» не выполнены.",
            _ => $"«{name}» недоступна."
        };

    private static string FormatSeconds(TimeSpan value)
        => (Math.Ceiling(value.TotalSeconds * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);

    private static AbilityActivationResult Fail(string message) => new(false, message);
}
