namespace Warcraft.Api.Cosmetics;

public interface ICosmeticsApi
{
    IReadOnlyCollection<string> GetOwned(ulong steamId);
    IReadOnlyDictionary<string, string> GetEquipped(ulong steamId);
    bool Owns(ulong steamId, string cosmeticId);
    CosmeticMutationResult Unlock(ulong steamId, string cosmeticId, string reason);
    CosmeticMutationResult Equip(ulong steamId, string slot, string cosmeticId, string reason);
    CosmeticMutationResult Unequip(ulong steamId, string slot, string reason);
}

public sealed record CosmeticMutationResult(bool Success, bool Changed, string Message);
