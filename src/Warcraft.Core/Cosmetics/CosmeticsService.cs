using Warcraft.Api.Cosmetics;
using Warcraft.Api.Events;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Cosmetics;

internal sealed class CosmeticsService(PlayerStateStore players, IWarcraftEventBus events) : ICosmeticsApi
{
    public IReadOnlyCollection<string> GetOwned(ulong steamId)
        => players.TryGetRuntime(steamId)?.OwnedCosmetics.ToArray() ?? [];

    public IReadOnlyDictionary<string, string> GetEquipped(ulong steamId)
        => players.TryGetRuntime(steamId) is { } player
            ? new Dictionary<string, string>(player.EquippedCosmetics, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool Owns(ulong steamId, string cosmeticId)
        => players.TryGetRuntime(steamId)?.OwnedCosmetics.Contains(cosmeticId) == true;

    public CosmeticMutationResult Unlock(ulong steamId, string cosmeticId, string reason)
    {
        if (string.IsNullOrWhiteSpace(cosmeticId))
            return new(false, false, "Некорректный ID косметики.");

        var player = players.TryGetRuntime(steamId);
        if (player is null)
            return new(false, false, "Игрок не загружен.");

        var changed = player.OwnedCosmetics.Add(cosmeticId);
        if (changed)
            events.Publish(new PlayerStateChangedEvent(steamId, $"cosmetic:unlock:{reason}"));

        return new(true, changed, changed ? "Косметика разблокирована." : "Косметика уже разблокирована.");
    }

    public CosmeticMutationResult Equip(ulong steamId, string slot, string cosmeticId, string reason)
    {
        if (string.IsNullOrWhiteSpace(slot) || string.IsNullOrWhiteSpace(cosmeticId))
            return new(false, false, "Некорректный слот или ID косметики.");

        var player = players.TryGetRuntime(steamId);
        if (player is null)
            return new(false, false, "Игрок не загружен.");

        if (!player.OwnedCosmetics.Contains(cosmeticId))
            return new(false, false, "Сначала разблокируйте этот предмет.");

        var changed = !player.EquippedCosmetics.TryGetValue(slot, out var current) ||
                      !string.Equals(current, cosmeticId, StringComparison.OrdinalIgnoreCase);

        player.EquippedCosmetics[slot] = cosmeticId;
        if (changed)
            events.Publish(new PlayerStateChangedEvent(steamId, $"cosmetic:equip:{reason}"));

        return new(true, changed, "Косметика экипирована.");
    }

    public CosmeticMutationResult Unequip(ulong steamId, string slot, string reason)
    {
        var player = players.TryGetRuntime(steamId);
        if (player is null)
            return new(false, false, "Игрок не загружен.");

        var changed = player.EquippedCosmetics.Remove(slot);
        if (changed)
            events.Publish(new PlayerStateChangedEvent(steamId, $"cosmetic:unequip:{reason}"));

        return new(true, changed, changed ? "Косметика снята." : "Слот уже пуст.");
    }
}
