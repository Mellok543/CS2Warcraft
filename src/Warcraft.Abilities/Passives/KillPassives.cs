using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Passives;

/// <summary>Passive: heals the killer after each enemy kill. Config: amount.</summary>
internal sealed class KillHealAbility : AbilityHandler
{
    public override string Id => "kill_heal";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "За убийство восстанавливает {amount} HP.";
    protected override string DisplayName => "Пожирание";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerKillEvent>(OnKill));

    private void OnKill(PlayerKillEvent kill)
    {
        if (kill.TeamKill || GetUsable(kill.KillerSteamId) is not { } ability)
            return;

        if (GamePlayers.FindAlive(kill.KillerSteamId) is { } killer)
            PlayerHealth.Heal(killer.Pawn, AbilityConfigReader.GetLevelInt(ability, "amount"));
    }
}

/// <summary>Passive: extra money for each enemy kill. Config: money, maxMoney (default 16000).</summary>
internal sealed class PlunderAbility : AbilityHandler
{
    public override string Id => "plunder";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "За убийство даёт ещё ${money}.";
    protected override string DisplayName => "Грабёж";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerKillEvent>(OnKill));

    private void OnKill(PlayerKillEvent kill)
    {
        if (kill.TeamKill || GetUsable(kill.KillerSteamId) is not { } ability)
            return;

        var player = Utilities.GetPlayerFromSteamId(kill.KillerSteamId);
        var money = player?.InGameMoneyServices;
        if (player is not { IsValid: true } || money is null)
            return;

        var max = AbilityConfigReader.GetLevelInt(ability, "maxMoney", 16000);
        money.Account = Math.Min(max, money.Account + Math.Max(0, AbilityConfigReader.GetLevelInt(ability, "money")));
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
    }
}

/// <summary>
/// Passive: chance to respawn shortly after death, once per round.
/// Config: chance, delay (seconds, default 2).
/// </summary>
internal sealed class ReincarnationAbility(IGameScheduler scheduler) : AbilityHandler
{
    private const byte TeamTerrorist = 2;
    private const byte TeamCounterTerrorist = 3;

    private readonly HashSet<ulong> _usedThisRound = [];
    private bool _roundActive = true;

    public override string Id => "reincarnation";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "С шансом {chance%} воскрешает через {delay|2} с после смерти (раз в раунд).";
    protected override string DisplayName => "Перерождение";

    protected override void Subscribe(IWarcraftEventBus events)
    {
        Track(events.Subscribe<PlayerDeathEvent>(OnDeath));
        Track(events.Subscribe<RoundStartEvent>(_ =>
        {
            _usedThisRound.Clear();
            _roundActive = true;
        }));
        Track(events.Subscribe<RoundEndEvent>(_ => _roundActive = false));
    }

    private void OnDeath(PlayerDeathEvent death)
    {
        if (!_roundActive || _usedThisRound.Contains(death.SteamId) || GetUsable(death.SteamId) is not { } ability)
            return;

        if (Random.Shared.NextDouble() >= Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "chance"), 0, 1))
            return;

        _usedThisRound.Add(death.SteamId);
        var delay = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "delay", 2.0), 0.1, 10.0);
        var steamId = death.SteamId;

        Utilities.GetPlayerFromSteamId(steamId)?.PrintToChat($" [Warcraft] Перерождение через {delay:0.#} с...");

        scheduler.Schedule(delay, () =>
        {
            var player = Utilities.GetPlayerFromSteamId(steamId);
            if (!_roundActive ||
                player is not { IsValid: true } ||
                player.PawnIsAlive ||
                player.TeamNum is not (TeamTerrorist or TeamCounterTerrorist))
            {
                return;
            }

            player.Respawn();
            player.PrintToChat(" [Warcraft] Вы переродились!");
        });
    }
}
