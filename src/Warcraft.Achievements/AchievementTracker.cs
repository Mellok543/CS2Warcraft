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

    private void OnAbilityTelemetry(AbilityTelemetryEvent telemetry)
    {
        switch (telemetry.AbilityId.ToLowerInvariant())
        {
            case "chain_lightning":
                if (telemetry.Kind == AbilityTelemetryKind.TargetsHit)
                    SetSpecific(telemetry.SteamId, "mechanic.chain_three", telemetry.Amount);
                else if (telemetry.Kind == AbilityTelemetryKind.Kill)
                    AddSpecific(telemetry.SteamId, "mechanic.chain_kills_25", 1);
                break;

            case "reflect_damage":
                if (telemetry.Kind == AbilityTelemetryKind.DamageDealt)
                    AddSpecific(telemetry.SteamId, "mechanic.reflect_damage_1000", telemetry.Amount);
                else if (telemetry.Kind == AbilityTelemetryKind.Kill)
                    AddSpecific(telemetry.SteamId, "mechanic.reflect_kill", 1);
                break;

            case "vampirism":
                if (telemetry.Kind == AbilityTelemetryKind.Healing)
                    AddSpecific(telemetry.SteamId, "mechanic.vamp_heal_5000", telemetry.Amount);
                break;

            case "damage_reduction":
                if (telemetry.Kind == AbilityTelemetryKind.DamagePrevented)
                    AddSpecific(telemetry.SteamId, "mechanic.reduction_10000", telemetry.Amount);
                break;

            case "divine_shield":
                if (telemetry.Kind == AbilityTelemetryKind.DamagePrevented)
                    AddSpecific(telemetry.SteamId, "mechanic.divine_1000", telemetry.Amount);
                break;

            case "second_wind":
                if (telemetry.Kind == AbilityTelemetryKind.Triggered)
                    AddSpecific(telemetry.SteamId, "mechanic.second_wind_25", 1);
                break;

            case "life_drain":
                if (telemetry.Kind == AbilityTelemetryKind.Healing)
                    AddSpecific(telemetry.SteamId, "mechanic.life_drain_heal_2000", telemetry.Amount);
                break;

            case "healing_totem":
                if (telemetry.Kind == AbilityTelemetryKind.TotemPlaced)
                    AddSpecific(telemetry.SteamId, "mechanic.totems_100", 1);
                else if (telemetry.Kind == AbilityTelemetryKind.Healing)
                    AddSpecific(telemetry.SteamId, "mechanic.healing_totem_5000", telemetry.Amount);
                break;

            case "flame_totem":
                if (telemetry.Kind == AbilityTelemetryKind.TotemPlaced)
                    AddSpecific(telemetry.SteamId, "mechanic.totems_100", 1);
                else if (telemetry.Kind == AbilityTelemetryKind.DamageDealt)
                    AddSpecific(telemetry.SteamId, "mechanic.flame_totem_5000", telemetry.Amount);
                else if (telemetry.Kind == AbilityTelemetryKind.Kill)
                    AddSpecific(telemetry.SteamId, "mechanic.flame_totem_kills_25", 1);
                break;

            case "frost_totem":
            case "war_totem":
                if (telemetry.Kind == AbilityTelemetryKind.TotemPlaced)
                    AddSpecific(telemetry.SteamId, "mechanic.totems_100", 1);
                break;

            case "shield_totem":
                if (telemetry.Kind == AbilityTelemetryKind.TotemPlaced)
                    AddSpecific(telemetry.SteamId, "mechanic.totems_100", 1);
                else if (telemetry.Kind == AbilityTelemetryKind.DamagePrevented)
                    AddSpecific(telemetry.SteamId, "mechanic.shield_totem_5000", telemetry.Amount);
                break;

            case "devotion_aura":
                if (telemetry.Kind == AbilityTelemetryKind.DamagePrevented)
                    AddSpecific(telemetry.SteamId, "mechanic.devotion_10000", telemetry.Amount);
                break;

            case "vampiric_aura":
                if (telemetry.Kind == AbilityTelemetryKind.Healing)
                    AddSpecific(telemetry.SteamId, "mechanic.vampiric_aura_5000", telemetry.Amount);
                break;
        }
    }

    private void AddSpecific(ulong steamId, string achievementId, long amount)
    {
        var definition = _definitions().FirstOrDefault(x =>
            string.Equals(x.Id, achievementId, StringComparison.OrdinalIgnoreCase));
        if (definition is null || amount <= 0)
            return;

        _api.Achievements.AddProgress(
            steamId,
            definition.Id,
            amount,
            definition.Target,
            "ability-telemetry");
    }

    private void SetSpecific(ulong steamId, string achievementId, long progress)
    {
        var definition = _definitions().FirstOrDefault(x =>
            string.Equals(x.Id, achievementId, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
            return;

        _api.Achievements.SetProgress(
            steamId,
            definition.Id,
            progress,
            definition.Target,
            "ability-telemetry");
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
