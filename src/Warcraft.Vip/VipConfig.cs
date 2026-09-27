using System.Text.Json;
using CounterStrikeSharp.API;

namespace Warcraft.Vip;

internal sealed record VipConfig
{
    /// <summary>CounterStrikeSharp admin flag that marks a VIP (configs/admins.json).</summary>
    public string Permission { get; init; } = "@warcraft/vip";

    public double XpMultiplier { get; init; } = 1.5;
    public int BonusSkillPointsPerLevel { get; init; }
    public bool CanAccessVipRaces { get; init; } = true;
    public int ExtraRaceSlots { get; init; }
    public double ShopDiscount { get; init; } = 0.2;
    public int BonusBuyMoney { get; init; } = 1000;
    public bool BhopEnabled { get; init; } = true;
    public double BhopCooldownSeconds { get; init; } = 2.0;
    public double BhopHorizontalMultiplier { get; init; } = 1.12;
    public double BhopMaxHorizontalSpeed { get; init; } = 420.0;

    public static string ConfigPath =>
        Path.Combine(Server.GameDirectory, "configs", "warcraft", "vip.json");

    public static VipConfig LoadOrCreate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        if (!File.Exists(ConfigPath))
        {
            var defaults = new VipConfig();
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaults, options));
            return defaults;
        }

        return JsonSerializer.Deserialize<VipConfig>(File.ReadAllText(ConfigPath), options)
               ?? throw new InvalidOperationException("vip.json is empty or invalid.");
    }
}
