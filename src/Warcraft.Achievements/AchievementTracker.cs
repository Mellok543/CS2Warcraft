using Warcraft.Api;
using Warcraft.Api.Events;

namespace Warcraft.Achievements;

internal sealed class AchievementTracker : IDisposable
{
    private readonly IWarcraftApi _api;
    private readonly Func<IReadOnlyList<AchievementDefinition>> _definitions;
    private readonly Dictionary<ulong, int> _killStreaks = [];
    private readonly Dictionary<ulong, int> _roundKills = [];
    private readonly IDisposable[] _subscriptions;
    private bool _roundHasKill;

    public AchievementTracker(
        IWarcraftApi api,
        Func<IReadOnlyList<AchievementDefinition>> definitions)
    {
        _api = api;
        _definitions = definitions;

        _subscriptions =
        [
            api.Events.Subscribe<PlayerKillEvent>(OnKill),
            api.Events.Subscribe<PlayerDeathEvent>(OnDeath),
            api.Events.Subscribe<PlayerRoundResultEvent>(OnRoundResult),
            api.Events.Subscribe<PlayerAssistEvent>(OnAssist),
            api.Events.Subscribe<BombPlantedEvent>(x => Increment(x.SteamId, AchievementMetric.BombPlants)),
            api.Events.Subscribe<BombDefusedEvent>(x => Increment(x.SteamId, AchievementMetric.BombDefuses)),
            api.Events.Subscribe<PlayerXpGainedEvent>(x => EvaluateSnapshot(x.SteamId)),
            api.Events.Subscribe<AbilityActivatedEvent>(OnAbilityActivated),
            api.Events.Subscribe<AbilityTelemetryEvent>(OnAbilityTelemetry),
            api.Events.Subscribe<RoundStartEvent>(_ => OnRoundStart())
        ];
    }

    public void EvaluateSnapshot(ulong steamId)
    {
        var state = _api.Players.Get(steamId);
        if (state is null)
            return;

        SetMetric(steamId, AchievementMetric.Kills, state.Stats.Kills);
        SetMetric(steamId, AchievementMetric.Headshots, state.Stats.Headshots);
        SetMetric(steamId, AchievementMetric.RoundsPlayed, state.Stats.RoundsPlayed);
        SetMetric(steamId, AchievementMetric.RoundsWon, state.Stats.RoundsWon);
        SetMetric(steamId, AchievementMetric.GlobalXp, state.GlobalXp);

        var totalLevels = state.Races.Values.Sum(x => (long)x.Level);
        var racesAt10 = state.Races.Values.Count(x => x.Level >= 10);

        SetMetric(steamId, AchievementMetric.TotalRaceLevels, totalLevels);
        SetMetric(steamId, AchievementMetric.RacesAtLevel10, racesAt10);

        foreach (var definition in _definitions().Where(x => x.Metric == AchievementMetric.RaceLevel && x.RaceId is not null))
        {
            var level = state.Races.TryGetValue(definition.RaceId!, out var race)
                ? race.Level
                : 0;

            _api.Achievements.SetProgress(
                steamId,
                definition.Id,
                level,
                definition.Target,
                "race-mastery");
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();

        _killStreaks.Clear();
        _roundKills.Clear();
    }

    private void OnKill(PlayerKillEvent kill)
    {
        if (kill.TeamKill)
            return;

        var streak = _killStreaks.GetValueOrDefault(kill.KillerSteamId) + 1;
        _killStreaks[kill.KillerSteamId] = streak;

        var roundKills = _roundKills.GetValueOrDefault(kill.KillerSteamId) + 1;
        _roundKills[kill.KillerSteamId] = roundKills;

        SetMetric(kill.KillerSteamId, AchievementMetric.KillStreak, streak);
        SetMetric(kill.KillerSteamId, AchievementMetric.RoundKills, roundKills);

        if (!_roundHasKill)
        {
            _roundHasKill = true;
            SetMetric(kill.KillerSteamId, AchievementMetric.FirstBlood, 1);
        }

        EvaluateSnapshot(kill.KillerSteamId);
    }

    private void OnDeath(PlayerDeathEvent death)
        => _killStreaks.Remove(death.SteamId);

    private void OnRoundResult(PlayerRoundResultEvent result)
        => EvaluateSnapshot(result.SteamId);

    private void OnAssist(PlayerAssistEvent assist)
        => Increment(assist.AssisterSteamId, AchievementMetric.Assists);

    private void OnAbilityActivated(AbilityActivatedEvent activation)
    {
        Increment(activation.SteamId, AchievementMetric.AbilityUses);

        if (activation.IsUltimate)
            Increment(activation.SteamId, AchievementMetric.UltimateUses);
    }

    /// <summary>Feeds every achievement whose telemetry rule matches; no per-ability code.</summary>
    private void OnAbilityTelemetry(AbilityTelemetryEvent telemetry)
    {
        if (telemetry.Amount <= 0)
            return;

        foreach (var definition in _definitions())
        {
            if (definition.Telemetry is not { } rule || !rule.Matches(telemetry))
                continue;

            if (rule.Aggregate == TelemetryAggregate.Max)
            {
                _api.Achievements.SetProgress(
                    telemetry.SteamId,
                    definition.Id,
                    telemetry.Amount,
                    definition.Target,
                    "ability-telemetry");
            }
            else
            {
                _api.Achievements.AddProgress(
                    telemetry.SteamId,
                    definition.Id,
                    telemetry.Amount,
                    definition.Target,
                    "ability-telemetry");
            }
        }
    }

    private void OnRoundStart()
    {
        _roundHasKill = false;
        _roundKills.Clear();
    }

    private void Increment(ulong steamId, AchievementMetric metric)
    {
        foreach (var definition in _definitions().Where(x => x.Metric == metric))
        {
            _api.Achievements.AddProgress(
                steamId,
                definition.Id,
                1,
                definition.Target,
                metric.ToString());
        }
    }

    private void SetMetric(ulong steamId, AchievementMetric metric, long value)
    {
        foreach (var definition in _definitions().Where(x => x.Metric == metric))
        {
            _api.Achievements.SetProgress(
                steamId,
                definition.Id,
                value,
                definition.Target,
                metric.ToString());
        }
    }
}
