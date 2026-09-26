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
    public const string BombPlantReason = "установка бомбы";
    public const string BombDefuseReason = "разминирование бомбы";

    private readonly PlayerStateStore _players;
    private readonly IProgressApi _progress;
    private readonly CoreConfig _config;
    private readonly IDisposable[] _subscriptions;

    public XpRewardService(PlayerStateStore players, IProgressApi progress, IWarcraftEventBus events, CoreConfig config)
    {
        _players = players;
        _progress = progress;
        _config = config;
        _subscriptions =
        [
            events.Subscribe<PlayerKillEvent>(OnKill),
            events.Subscribe<PlayerAssistEvent>(e => Grant(e.AssisterSteamId, _config.AssistXp, AssistReason)),
            events.Subscribe<PlayerRoundResultEvent>(e =>
            {
                if (e.Won)
                    Grant(e.SteamId, _config.RoundWinXp, RoundWinReason);
            }),
            events.Subscribe<BombPlantedEvent>(e => Grant(e.SteamId, _config.BombPlantXp, BombPlantReason)),
            events.Subscribe<BombDefusedEvent>(e => Grant(e.SteamId, _config.BombDefuseXp, BombDefuseReason))
        ];
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
    }

    private void OnKill(PlayerKillEvent kill)
    {
        if (kill.TeamKill)
            return;

        Grant(
            kill.KillerSteamId,
            _config.KillXp + (kill.Headshot ? _config.HeadshotBonusXp : 0),
            kill.Headshot ? KillReason + " в голову" : KillReason);
    }

    private void Grant(ulong steamId, int amount, string reason)
    {
        if (amount <= 0 || _players.TryGetRuntime(steamId) is null)
            return;

        _progress.AddXp(steamId, amount, reason);
    }
}
