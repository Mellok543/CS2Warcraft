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

    public static ShopEffectRegistry CreateDefault(TemporaryBhopService bhop)
        => new([
            new HealEffect(),
            new ArmorEffect(),
            new GiveItemEffect(),
            new XpEffect(),
            new XpBoostEffect(),
            new TemporaryBhopEffect(bhop),
            new CosmeticUnlockEffect()
        ]);

    public IEnumerable<string> Types => _effects.Keys.Order();

    public IShopEffect? Get(string type) => _effects.GetValueOrDefault(type);
}

internal static class EffectJson
{
    public static int Int(JsonElement effect, string property, int fallback = 0)
        => effect.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    public static string? RequirePositive(JsonElement effect, string property)
        => Int(effect, property) > 0 ? null : $"'{property}' must be a positive integer.";

    public static double Double(JsonElement effect, string property, double fallback = 0)
        => effect.TryGetProperty(property, out var value) && value.TryGetDouble(out var result) ? result : fallback;
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


/// <summary><c>{ "type": "xp_boost", "multiplier": 2.0, "durationSeconds": 900 }</c></summary>
internal sealed class XpBoostEffect : IShopEffect
{
    private const string Source = "warcraft.shop.xp_boost";

    public string Type => "xp_boost";

    public string? Validate(JsonElement effect)
    {
        var multiplier = EffectJson.Double(effect, "multiplier");
        var duration = EffectJson.Int(effect, "durationSeconds");
        if (multiplier <= 1.0)
            return "'multiplier' must be greater than 1.";
        return duration > 0 ? null : "'durationSeconds' must be a positive integer.";
    }

    public string? Apply(ShopContext context, JsonElement effect)
    {
        var multiplier = EffectJson.Double(effect, "multiplier");
        var duration = TimeSpan.FromSeconds(EffectJson.Int(effect, "durationSeconds"));
        context.Api.Modifiers.AddTemporaryXpMultiplier(context.Player.SteamID, multiplier, duration, Source);
        return null;
    }
}


/// <summary>Temporary shop bhop. Config: durationSeconds, multiplier, maxSpeed, cooldownSeconds.</summary>
internal sealed class TemporaryBhopEffect(TemporaryBhopService service) : IShopEffect
{
    public string Type => "bhop_boost";

    public string? Validate(JsonElement effect)
    {
        if (EffectJson.Double(effect, "durationSeconds") <= 0)
            return "'durationSeconds' must be positive.";
        if (EffectJson.Double(effect, "multiplier", 1.12) < 1)
            return "'multiplier' must be >= 1.";
        if (EffectJson.Double(effect, "maxSpeed", 420) <= 0)
            return "'maxSpeed' must be positive.";
        return null;
    }

    public string? Apply(ShopContext context, JsonElement effect)
    {
        service.Activate(
            context.Player.SteamID,
            Server.CurrentTime,
            EffectJson.Double(effect, "durationSeconds"),
            EffectJson.Double(effect, "multiplier", 1.12),
            EffectJson.Double(effect, "maxSpeed", 420),
            EffectJson.Double(effect, "cooldownSeconds", 0.1));
        return null;
    }
}

/// <summary>Unlocks a persistent cosmetic and equips it immediately.</summary>
internal sealed class CosmeticUnlockEffect : IShopEffect
{
    public string Type => "cosmetic_unlock";

    public string? Validate(JsonElement effect)
    {
        var id = effect.TryGetProperty("cosmeticId", out var idValue) ? idValue.GetString() : null;
        var slot = effect.TryGetProperty("slot", out var slotValue) ? slotValue.GetString() : null;
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(slot)
            ? "'cosmeticId' and 'slot' are required."
            : null;
    }

    public string? Apply(ShopContext context, JsonElement effect)
    {
        var cosmeticId = effect.GetProperty("cosmeticId").GetString()!;
        var slot = effect.GetProperty("slot").GetString()!;

        if (context.Api.Cosmetics.Owns(context.Player.SteamID, cosmeticId))
            return "Этот косметический предмет уже получен.";

        var unlock = context.Api.Cosmetics.Unlock(context.Player.SteamID, cosmeticId, "shop");
        if (!unlock.Success)
            return unlock.Message;

        var equip = context.Api.Cosmetics.Equip(context.Player.SteamID, slot, cosmeticId, "shop");
        return equip.Success ? null : equip.Message;
    }
}
