using CounterStrikeSharp.API;
using Warcraft.Api.Events;
using Warcraft.Api.Progression;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Progression;

/// <summary>
/// Grants XP for gameplay events. All amounts come from core.json; modifiers
/// (e.g. the VIP multiplier) are applied by <see cref="IProgressApi.AddXp"/>.
/// </summary>
internal sealed class XpRewardService : IDisposable
{
    public const string KillReason = "убийство";
    public const string AssistReason = "помощь в убийстве";
    public const string RoundWinReason = "победа в раунде";
    public const string DeathPenaltyReason = "смерть";
    public const string RoundLossPenaltyReason = "проигрыш в раунде";
    public const string KnifeKillReason = "убийство с ножа";
    public const string BombPlantReason = "установка бомбы";
    public const string BombDefuseReason = "разминирование бомбы";
    public const string PlaytimeReason = "время на сервере";
    public const string ComboKillReason = "серия убийств";

    private readonly PlayerStateStore _players;
    private readonly IProgressApi _progress;
    private readonly CoreConfig _config;
    private readonly IDisposable[] _subscriptions;
    private readonly Dictionary<ulong, double> _playtimeDueAt = [];
    private readonly Dictionary<ulong, KillComboState> _killCombos = [];

    public XpRewardService(PlayerStateStore players, IProgressApi progress, IWarcraftEventBus events, CoreConfig config)
    {
        _players = players;
        _progress = progress;
        _config = config;
        _subscriptions =
        [
            events.Subscribe<PlayerKillEvent>(OnKill),
            events.Subscribe<PlayerDeathEvent>(e =>
            {
                _killCombos.Remove(e.SteamId);
                Penalize(e.SteamId, _config.DeathPenaltyXp, DeathPenaltyReason);
            }),
            events.Subscribe<PlayerAssistEvent>(e => Grant(e.AssisterSteamId, _config.AssistXp, AssistReason)),
            events.Subscribe<PlayerRoundResultEvent>(e =>
            {
                if (e.Won)
                    Grant(e.SteamId, _config.RoundWinXp, RoundWinReason);
                else
                    Penalize(e.SteamId, _config.RoundLossPenaltyXp, RoundLossPenaltyReason);
            }),
            events.Subscribe<BombPlantedEvent>(e => Grant(e.SteamId, _config.BombPlantXp, BombPlantReason)),
            events.Subscribe<BombDefusedEvent>(e => Grant(e.SteamId, _config.BombDefuseXp, BombDefuseReason)),
            events.Subscribe<GameTickEvent>(OnGameTick)
        ];
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
    }

    private void OnGameTick(GameTickEvent tick)
    {
        if (_config.PlaytimeXp <= 0 || _config.PlaytimeXpIntervalSeconds <= 0)
            return;

        var loaded = _players.GetLoadedPlayers();
        var active = loaded.Select(x => x.SteamId).ToHashSet();

        foreach (var stale in _playtimeDueAt.Keys.Where(x => !active.Contains(x)).ToArray())
            _playtimeDueAt.Remove(stale);

        foreach (var player in loaded)
        {
            if (!_playtimeDueAt.TryGetValue(player.SteamId, out var due))
            {
                _playtimeDueAt[player.SteamId] = tick.ServerTime + _config.PlaytimeXpIntervalSeconds;
                continue;
            }

            if (tick.ServerTime < due)
                continue;

            _playtimeDueAt[player.SteamId] = tick.ServerTime + _config.PlaytimeXpIntervalSeconds;
            Grant(player.SteamId, _config.PlaytimeXp, PlaytimeReason);
        }
    }

    private void OnKill(PlayerKillEvent kill)
    {
        if (kill.TeamKill)
            return;

        var knife = IsKnife(kill.Weapon);
        var amount =
            _config.KillXp +
            (kill.Headshot ? _config.HeadshotBonusXp : 0) +
            (knife ? _config.KnifeKillBonusXp : 0);

        Grant(
            kill.KillerSteamId,
            amount,
            knife ? KnifeKillReason : kill.Headshot ? KillReason + " в голову" : KillReason);

        HandleKillCombo(kill.KillerSteamId);
    }

    private void HandleKillCombo(ulong steamId)
    {
        if (_config.ComboKillWindowSeconds <= 0 ||
            _config.ComboKillBonusXp is not { Length: > 0 })
        {
            return;
        }

        var now = Server.CurrentTime;
        var previous = _killCombos.GetValueOrDefault(steamId);
        var count = previous.Count > 0 &&
                    now - previous.LastKillAt <= _config.ComboKillWindowSeconds
            ? previous.Count + 1
            : 1;

        _killCombos[steamId] = new KillComboState(count, now);

        if (count < 2)
            return;

        var index = Math.Min(count - 1, _config.ComboKillBonusXp.Length - 1);
        var bonus = Math.Max(0, _config.ComboKillBonusXp[index]);
        if (bonus <= 0)
            return;

        Grant(steamId, bonus, $"{ComboKillReason} x{count}");
    }

    private void Penalize(ulong steamId, int amount, string reason)
    {
        if (amount <= 0 || _players.TryGetRuntime(steamId) is null)
            return;

        _progress.AddXp(steamId, -amount, reason);
    }

    private static bool IsKnife(string? weapon)
    {
        if (string.IsNullOrWhiteSpace(weapon))
            return false;

        return weapon.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
               weapon.Contains("bayonet", StringComparison.OrdinalIgnoreCase);
    }

    private void Grant(ulong steamId, int amount, string reason)
    {
        if (amount <= 0 || _players.TryGetRuntime(steamId) is null)
            return;

        _progress.AddXp(steamId, amount, reason);
    }
    private readonly record struct KillComboState(int Count, double LastKillAt);
}
