using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Passives;

/// <summary>Passive: armor (and optionally a helmet) on spawn. Config: armor, helmet (1 = yes).</summary>
internal sealed class SpawnArmorAbility(IGameScheduler scheduler) : AbilityHandler
{
    public override string Id => "spawn_armor";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "При возрождении даёт {armor} брони.";
    protected override string DisplayName => "Закалка";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerSpawnEvent>(OnSpawn));

    private void OnSpawn(PlayerSpawnEvent spawn)
    {
        if (GetUsable(spawn.SteamId) is not { } ability)
            return;

        var armor = Math.Clamp(AbilityConfigReader.GetLevelInt(ability, "armor"), 0, 100);
        var helmet = AbilityConfigReader.GetLevelInt(ability, "helmet") > 0;
        var steamId = spawn.SteamId;

        // Let the spawn loadout settle first.
        scheduler.Schedule(0.1f, () =>
        {
            if (GamePlayers.FindAlive(steamId) is not { } player)
                return;

            var pawn = player.Pawn;
            if (pawn.ArmorValue < armor)
            {
                pawn.ArmorValue = armor;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
            }

            if (helmet && pawn.ItemServices is { } services)
            {
                new CCSPlayer_ItemServices(services.Handle).HasHelmet = true;
                Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pItemServices");
            }
        });
    }
}

/// <summary>
/// Passive: gives items on spawn. Config: items — ["weapon_flashbang", ...] for all
/// levels or [["weapon_flashbang"], ["weapon_flashbang", "weapon_hegrenade"]] per level.
/// </summary>
internal sealed class SpawnItemsAbility(IGameScheduler scheduler) : AbilityHandler
{
    public override string Id => "spawn_items";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "При возрождении выдаёт дополнительные предметы.";
    protected override string DisplayName => "Снаряжение";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerSpawnEvent>(OnSpawn));

    private void OnSpawn(PlayerSpawnEvent spawn)
    {
        if (GetUsable(spawn.SteamId) is not { } ability)
            return;

        var items = AbilityConfigReader.GetLevelStrings(ability, "items")
            .Where(x => x.StartsWith("weapon_", StringComparison.Ordinal) || x.StartsWith("item_", StringComparison.Ordinal))
            .ToArray();

        if (items.Length == 0)
            return;

        var steamId = spawn.SteamId;
        scheduler.Schedule(0.2f, () =>
        {
            if (GamePlayers.FindAlive(steamId) is not { } player)
                return;

            foreach (var item in items)
                player.Controller.GiveNamedItem(item);
        });
    }
}
