namespace Warcraft.Cosmetics;

internal sealed record CosmeticDefinition(
    string Id,
    string Name,
    string Slot,
    string Model);

/// <summary>
/// Where a slot's model sits on a player. The model is placed relative to the eyes
/// (so equipping while crouched works), then parented to a player model attachment
/// with its offset kept, so it follows the head/back bone (crouch, look up/down).
/// </summary>
/// <param name="Attachment">Attachment present on every CS2 agent model.</param>
/// <param name="Forward">Units along the view direction.</param>
/// <param name="Right">Units to the player's right.</param>
/// <param name="Up">Height of the model's origin (bottom centre) relative to the eyes.</param>
/// <param name="Yaw">Extra yaw; models face forward, backpacks must face backwards.</param>
internal sealed record CosmeticMount(string Attachment, float Forward, float Right, float Up, float Yaw);

internal static class CosmeticCatalog
{
    public const string Hat = "hat";
    public const string Mask = "mask";
    public const string Backpack = "backpack";
    public const string Pet = "pet";

    public static IReadOnlyList<CosmeticDefinition> All { get; } =
    [
        new("backpack_capybara", "Рюкзак-капибара", Backpack, "models/warcraft/cosmetics/backpacks/backpack_capybara/backpack_capybara.vmdl"),
        new("backpack_jetpack", "Джетпак", Backpack, "models/warcraft/cosmetics/backpacks/backpack_jetpack/backpack_jetpack.vmdl"),
        new("backpack_loot_sack", "Мешок добычи", Backpack, "models/warcraft/cosmetics/backpacks/backpack_loot_sack/backpack_loot_sack.vmdl"),
        new("backpack_quiver", "Колчан", Backpack, "models/warcraft/cosmetics/backpacks/backpack_quiver/backpack_quiver.vmdl"),
        new("backpack_mimic", "Рюкзак-мимик", Backpack, "models/warcraft/cosmetics/backpacks/backpack_mimic/backpack_mimic.vmdl"),

        new("hat_cat_headset", "Кошачьи наушники", Hat, "models/warcraft/cosmetics/hats/hat_cat_headset/hat_cat_headset.vmdl"),
        new("hat_mushroom", "Грибная шапка", Hat, "models/warcraft/cosmetics/hats/hat_mushroom/hat_mushroom.vmdl"),
        new("hat_viking", "Шлем викинга", Hat, "models/warcraft/cosmetics/hats/hat_viking/hat_viking.vmdl"),
        new("hat_wizard", "Шляпа мага", Hat, "models/warcraft/cosmetics/hats/hat_wizard/hat_wizard.vmdl"),
        new("hat_crown", "Корона", Hat, "models/warcraft/cosmetics/hats/hat_crown/hat_crown.vmdl"),

        new("mask_cyber", "Кибермаска", Mask, "models/warcraft/cosmetics/masks/mask_cyber/mask_cyber.vmdl"),
        new("mask_orc", "Маска орка", Mask, "models/warcraft/cosmetics/masks/mask_orc/mask_orc.vmdl"),
        new("mask_kitsune", "Маска кицунэ", Mask, "models/warcraft/cosmetics/masks/mask_kitsune/mask_kitsune.vmdl"),
        new("mask_plague", "Маска чумного доктора", Mask, "models/warcraft/cosmetics/masks/mask_plague/mask_plague.vmdl"),
        new("mask_oni", "Маска Они", Mask, "models/warcraft/cosmetics/masks/mask_oni/mask_oni.vmdl"),

        new("pet_axolotl", "Аксолотль", Pet, "models/warcraft/cosmetics/pets/pet_axolotl/pet_axolotl.vmdl"),
        new("pet_cowboy_frog", "Лягушка-ковбой", Pet, "models/warcraft/cosmetics/pets/pet_cowboy_frog/pet_cowboy_frog.vmdl"),
        new("pet_capybara", "Капибара", Pet, "models/warcraft/cosmetics/pets/pet_capybara/pet_capybara.vmdl"),
        new("pet_owl", "Сова", Pet, "models/warcraft/cosmetics/pets/pet_owl/pet_owl.vmdl"),
        new("pet_dragon", "Дракон", Pet, "models/warcraft/cosmetics/pets/pet_dragon/pet_dragon.vmdl")
    ];

    // clip_limit sits on head_0, c4 on spine_3 (upper back) of all agent models.
    private static readonly CosmeticMount HatMount = new("clip_limit", 0f, 0f, 8f, 0f);
    private static readonly CosmeticMount MaskMount = new("clip_limit", 8f, 0f, -6f, 0f);
    private static readonly CosmeticMount BackpackMount = new("c4", -9f, 0f, -28f, 180f);
    private static readonly CosmeticMount PetMount = new("c4", -1f, 11f, -8f, 0f);

    public static CosmeticMount Mount(string slot) => slot switch
    {
        Hat => HatMount,
        Mask => MaskMount,
        Backpack => BackpackMount,
        _ => PetMount
    };

    public static CosmeticDefinition? Find(string id)
        => All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string SlotName(string slot) => slot switch
    {
        Hat => "Шапка",
        Mask => "Маска",
        Backpack => "Рюкзак",
        Pet => "Питомец",
        _ => slot
    };
}
