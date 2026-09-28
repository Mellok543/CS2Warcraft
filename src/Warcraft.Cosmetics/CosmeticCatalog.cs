namespace Warcraft.Cosmetics;

internal sealed record CosmeticDefinition(
    string Id,
    string Name,
    string Slot,
    string Model);

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
