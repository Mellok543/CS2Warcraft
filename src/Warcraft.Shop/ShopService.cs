using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Warcraft.Api;
using Warcraft.Shop.Effects;

namespace Warcraft.Shop;

internal sealed record PurchaseResult(bool Success, string Message);

/// <summary>
/// Purchase rules: alive check, per-round limit, price with the Core-combined
/// shop discount (e.g. from VIP), in-game money. Game thread only.
/// </summary>
internal sealed class ShopService(IWarcraftApi api, ShopEffectRegistry effects)
{
    private readonly Dictionary<(ulong SteamId, string ItemId), int> _boughtThisRound = [];

    public IReadOnlyList<ShopItemDefinition> Items { get; private set; } = [];

    public void ReplaceItems(IReadOnlyList<ShopItemDefinition> items) => Items = items;

    public void ResetRound() => _boughtThisRound.Clear();

    public int GetPrice(ulong steamId, ShopItemDefinition item)
    {
        var discount = Math.Clamp(api.Modifiers.GetCombined(steamId).ShopDiscount, 0.0, 1.0);
        return (int)Math.Ceiling(item.Price * (1.0 - discount));
    }

    public int GetBoughtThisRound(ulong steamId, ShopItemDefinition item)
        => _boughtThisRound.GetValueOrDefault((steamId, item.Id));

    public PurchaseResult Buy(CCSPlayerController player, ShopItemDefinition item)
    {
        var steamId = player.SteamID;
        var pawn = player.PlayerPawn.Value;

        if (item.RequiresAlive && (!player.PawnIsAlive || pawn is not { IsValid: true }))
            return new(false, "Этот предмет можно купить только живым.");

        var bought = GetBoughtThisRound(steamId, item);
        if (item.MaxPerRound > 0 && bought >= item.MaxPerRound)
            return new(false, $"Лимит на раунд: {item.MaxPerRound}.");

        var money = player.InGameMoneyServices;
        var price = GetPrice(steamId, item);
        if (money is null || money.Account < price)
            return new(false, $"Недостаточно денег: нужно ${price}.");

        var effect = effects.Get(item.EffectType);
        if (effect is null)
            return new(false, "Предмет настроен неверно.");

        var error = effect.Apply(new ShopContext(api, player, pawn is { IsValid: true } ? pawn : null), item.Effect);
        if (error is not null)
            return new(false, error);

        money.Account -= price;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        _boughtThisRound[(steamId, item.Id)] = bought + 1;

        return new(true, $"Куплено: {item.Name} за ${price}.");
    }
}
