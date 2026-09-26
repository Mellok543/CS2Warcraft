using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Warcraft.Api;

namespace Warcraft.Shop.Effects;

internal sealed record ShopContext(
    IWarcraftApi Api,
    CCSPlayerController Player,
    CCSPlayerPawn? Pawn);

/// <summary>One effect type usable by any shop item (<c>"effect": { "type": ... }</c>).</summary>
internal interface IShopEffect
{
    string Type { get; }

    /// <summary>Returns an error for invalid effect parameters, otherwise null.</summary>
    string? Validate(JsonElement effect);

    /// <summary>Applies the effect; returns an error message when it cannot be applied (no charge).</summary>
    string? Apply(ShopContext context, JsonElement effect);
}

internal sealed class ShopEffectRegistry(IEnumerable<IShopEffect> effects)
{
    private readonly IReadOnlyDictionary<string, IShopEffect> _effects =
        effects.ToDictionary(x => x.Type, StringComparer.OrdinalIgnoreCase);

    public static ShopEffectRegistry CreateDefault()
        => new([new HealEffect(), new ArmorEffect(), new GiveItemEffect(), new XpEffect()]);

    public IEnumerable<string> Types => _effects.Keys.Order();

    public IShopEffect? Get(string type) => _effects.GetValueOrDefault(type);
}

internal static class EffectJson
{
    public static int Int(JsonElement effect, string property, int fallback = 0)
        => effect.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    public static string? RequirePositive(JsonElement effect, string property)
        => Int(effect, property) > 0 ? null : $"'{property}' must be a positive integer.";
}

/// <summary><c>{ "type": "heal", "amount": 40 }</c> — up to max health.</summary>
internal sealed class HealEffect : IShopEffect
{
    public string Type => "heal";

    public string? Validate(JsonElement effect) => EffectJson.RequirePositive(effect, "amount");

    public string? Apply(ShopContext context, JsonElement effect)
    {
        var pawn = context.Pawn!;
        if (pawn.Health >= pawn.MaxHealth)
            return "У вас уже полное здоровье.";

        pawn.Health = Math.Min(pawn.MaxHealth, pawn.Health + EffectJson.Int(effect, "amount"));
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        return null;
    }
}

/// <summary><c>{ "type": "armor", "amount": 100, "helmet": true }</c></summary>
internal sealed class ArmorEffect : IShopEffect
{
    private const int MaxArmor = 100;

    public string Type => "armor";

    public string? Validate(JsonElement effect) => EffectJson.RequirePositive(effect, "amount");

    public string? Apply(ShopContext context, JsonElement effect)
    {
        var pawn = context.Pawn!;
        var helmet = effect.TryGetProperty("helmet", out var value) && value.ValueKind == JsonValueKind.True;
        var itemServices = pawn.ItemServices is { } services ? new CCSPlayer_ItemServices(services.Handle) : null;

        if (pawn.ArmorValue >= MaxArmor && (!helmet || itemServices?.HasHelmet == true))
            return "У вас уже полная броня.";

        pawn.ArmorValue = Math.Min(MaxArmor, pawn.ArmorValue + EffectJson.Int(effect, "amount"));
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");

        if (helmet && itemServices is not null)
        {
            itemServices.HasHelmet = true;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pItemServices");
        }

        return null;
    }
}

/// <summary><c>{ "type": "give_item", "item": "weapon_hegrenade" }</c></summary>
internal sealed class GiveItemEffect : IShopEffect
{
    public string Type => "give_item";

    public string? Validate(JsonElement effect)
    {
        var item = effect.TryGetProperty("item", out var value) ? value.GetString() : null;
        return item is not null && (item.StartsWith("weapon_", StringComparison.Ordinal) ||
                                    item.StartsWith("item_", StringComparison.Ordinal))
            ? null
            : "'item' must be a designer name such as weapon_hegrenade or item_assaultsuit.";
    }

    public string? Apply(ShopContext context, JsonElement effect)
    {
        context.Player.GiveNamedItem(effect.GetProperty("item").GetString()!);
        return null;
    }
}

/// <summary><c>{ "type": "xp", "amount": 50 }</c> — granted through Core (modifiers apply).</summary>
internal sealed class XpEffect : IShopEffect
{
    public string Type => "xp";

    public string? Validate(JsonElement effect) => EffectJson.RequirePositive(effect, "amount");

    public string? Apply(ShopContext context, JsonElement effect)
    {
        var result = context.Api.Progress.AddXp(context.Player.SteamID, EffectJson.Int(effect, "amount"), "магазин");
        return result.Success ? null : result.Message;
    }
}
