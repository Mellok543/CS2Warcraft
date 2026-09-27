using Warcraft.Api.Events;

namespace Warcraft.Achievements;

internal enum AchievementCategory
{
    Combat,
    Progression,
    Teamwork,
    Abilities,
    Mastery
}

internal enum AchievementRarity
{
    Common,
    Rare,
    Epic,
    Legendary,
    Secret
}

internal enum AchievementMetric
{
    Kills,
    Headshots,
    KillStreak,
    RoundKills,
    FirstBlood,
    GlobalXp,
    TotalRaceLevels,
    RacesAtLevel10,
    RoundsPlayed,
    RoundsWon,
    Assists,
    BombPlants,
    BombDefuses,
    AbilityUses,
    UltimateUses,
    RaceLevel,

    /// <summary>Driven by <see cref="Warcraft.Api.Events.AbilityTelemetryEvent"/>; see <see cref="AbilityTelemetryRule"/>.</summary>
    AbilityTelemetry
}

internal enum TelemetryAggregate
{
    /// <summary>Progress accumulates telemetry amounts (damage, healing, kills, uses).</summary>
    Sum,

    /// <summary>Progress is the best single telemetry amount (e.g. targets hit by one cast).</summary>
    Max
}

/// <summary>Which ability telemetry feeds an achievement. Data, not code: no per-ability switch.</summary>
internal sealed record AbilityTelemetryRule(
    IReadOnlySet<string> AbilityIds,
    Warcraft.Api.Events.AbilityTelemetryKind Kind,
    TelemetryAggregate Aggregate = TelemetryAggregate.Sum)
{
    public bool Matches(Warcraft.Api.Events.AbilityTelemetryEvent telemetry)
        => telemetry.Kind == Kind && AbilityIds.Contains(telemetry.AbilityId);
}

internal sealed record AchievementDefinition(
    string Id,
    string Name,
    string Description,
    AchievementCategory Category,
    AchievementRarity Rarity,
    AchievementMetric Metric,
    long Target,
    bool Secret = false,
    string? RaceId = null,
    AbilityTelemetryRule? Telemetry = null);

internal static class AchievementCatalog
{
    public static IReadOnlyList<AchievementDefinition> Build(Warcraft.Api.IWarcraftApi api)
    {
        var definitions = new List<AchievementDefinition>
        {
            A("combat.first_kill", "Первый трофей", "Совершить первое убийство.", AchievementCategory.Combat, AchievementRarity.Common, AchievementMetric.Kills, 1),
            A("combat.kills_10", "Разминка окончена", "Совершить 10 убийств.", AchievementCategory.Combat, AchievementRarity.Common, AchievementMetric.Kills, 10),
            A("combat.kills_50", "Охотник", "Совершить 50 убийств.", AchievementCategory.Combat, AchievementRarity.Rare, AchievementMetric.Kills, 50),
            A("combat.kills_100", "Воин", "Совершить 100 убийств.", AchievementCategory.Combat, AchievementRarity.Rare, AchievementMetric.Kills, 100),
            A("combat.kills_250", "Палач", "Совершить 250 убийств.", AchievementCategory.Combat, AchievementRarity.Epic, AchievementMetric.Kills, 250),
            A("combat.kills_1000", "Легенда поля боя", "Совершить 1000 убийств.", AchievementCategory.Combat, AchievementRarity.Legendary, AchievementMetric.Kills, 1000),

            A("combat.hs_1", "Точно в цель", "Совершить первое убийство в голову.", AchievementCategory.Combat, AchievementRarity.Common, AchievementMetric.Headshots, 1),
            A("combat.hs_50", "Снайперская привычка", "Совершить 50 убийств в голову.", AchievementCategory.Combat, AchievementRarity.Rare, AchievementMetric.Headshots, 50),
            A("combat.hs_250", "Без права на ошибку", "Совершить 250 убийств в голову.", AchievementCategory.Combat, AchievementRarity.Epic, AchievementMetric.Headshots, 250),

            A("combat.streak_3", "На ходу", "Совершить 3 убийства за одну жизнь.", AchievementCategory.Combat, AchievementRarity.Common, AchievementMetric.KillStreak, 3),
            A("combat.streak_5", "Не остановить", "Совершить 5 убийств за одну жизнь.", AchievementCategory.Combat, AchievementRarity.Rare, AchievementMetric.KillStreak, 5),
            A("combat.streak_10", "Бессмертный охотник", "Совершить 10 убийств за одну жизнь.", AchievementCategory.Combat, AchievementRarity.Legendary, AchievementMetric.KillStreak, 10, true),

            A("combat.round_3", "Тройной удар", "Совершить 3 убийства за один раунд.", AchievementCategory.Combat, AchievementRarity.Rare, AchievementMetric.RoundKills, 3),
            A("combat.round_5", "Резня", "Совершить 5 убийств за один раунд.", AchievementCategory.Combat, AchievementRarity.Epic, AchievementMetric.RoundKills, 5),
            A("combat.first_blood", "Первая кровь", "Совершить первое убийство в раунде.", AchievementCategory.Combat, AchievementRarity.Rare, AchievementMetric.FirstBlood, 1),

            A("progress.xp_1000", "Первые знания", "Набрать 1 000 общего XP.", AchievementCategory.Progression, AchievementRarity.Common, AchievementMetric.GlobalXp, 1000),
            A("progress.xp_10000", "Закалённый герой", "Набрать 10 000 общего XP.", AchievementCategory.Progression, AchievementRarity.Rare, AchievementMetric.GlobalXp, 10000),
            A("progress.xp_100000", "Хранитель опыта", "Набрать 100 000 общего XP.", AchievementCategory.Progression, AchievementRarity.Legendary, AchievementMetric.GlobalXp, 100000),

            A("progress.levels_25", "Путь исследователя", "Набрать суммарно 25 уровней рас.", AchievementCategory.Progression, AchievementRarity.Common, AchievementMetric.TotalRaceLevels, 25),
            A("progress.levels_100", "Знаток рас", "Набрать суммарно 100 уровней рас.", AchievementCategory.Progression, AchievementRarity.Epic, AchievementMetric.TotalRaceLevels, 100),
            A("progress.levels_250", "Мастер Warcraft", "Набрать суммарно 250 уровней рас.", AchievementCategory.Progression, AchievementRarity.Legendary, AchievementMetric.TotalRaceLevels, 250),

            A("progress.mastered_1", "Первая специализация", "Довести одну расу до 10 уровня.", AchievementCategory.Progression, AchievementRarity.Rare, AchievementMetric.RacesAtLevel10, 1),
            A("progress.mastered_5", "Коллекционер знаний", "Довести 5 рас до 10 уровня.", AchievementCategory.Progression, AchievementRarity.Epic, AchievementMetric.RacesAtLevel10, 5),
            A("progress.mastered_10", "Архимастер", "Довести 10 рас до 10 уровня.", AchievementCategory.Progression, AchievementRarity.Legendary, AchievementMetric.RacesAtLevel10, 10),

            A("progress.rounds_50", "Завсегдатай", "Сыграть 50 раундов.", AchievementCategory.Progression, AchievementRarity.Common, AchievementMetric.RoundsPlayed, 50),
            A("progress.rounds_250", "Ветеран", "Сыграть 250 раундов.", AchievementCategory.Progression, AchievementRarity.Rare, AchievementMetric.RoundsPlayed, 250),
            A("progress.rounds_1000", "Старожил", "Сыграть 1000 раундов.", AchievementCategory.Progression, AchievementRarity.Legendary, AchievementMetric.RoundsPlayed, 1000),

            A("progress.win_1", "Первая победа", "Выиграть первый раунд.", AchievementCategory.Progression, AchievementRarity.Common, AchievementMetric.RoundsWon, 1),
            A("progress.win_25", "Победный ритм", "Выиграть 25 раундов.", AchievementCategory.Progression, AchievementRarity.Common, AchievementMetric.RoundsWon, 25),
            A("progress.win_100", "Завоеватель", "Выиграть 100 раундов.", AchievementCategory.Progression, AchievementRarity.Epic, AchievementMetric.RoundsWon, 100),
            A("progress.win_250", "Триумфатор", "Выиграть 250 раундов.", AchievementCategory.Progression, AchievementRarity.Legendary, AchievementMetric.RoundsWon, 250),

            A("team.assists_10", "Надёжный союзник", "Сделать 10 assists.", AchievementCategory.Teamwork, AchievementRarity.Common, AchievementMetric.Assists, 10),
            A("team.assists_100", "Правая рука", "Сделать 100 assists.", AchievementCategory.Teamwork, AchievementRarity.Epic, AchievementMetric.Assists, 100),
            A("team.plant_1", "Посылка доставлена", "Установить бомбу.", AchievementCategory.Teamwork, AchievementRarity.Common, AchievementMetric.BombPlants, 1),
            A("team.plant_25", "Подрывник", "Установить бомбу 25 раз.", AchievementCategory.Teamwork, AchievementRarity.Rare, AchievementMetric.BombPlants, 25),
            A("team.defuse_1", "Не сегодня", "Обезвредить бомбу.", AchievementCategory.Teamwork, AchievementRarity.Common, AchievementMetric.BombDefuses, 1),
            A("team.defuse_25", "Сапёр", "Обезвредить бомбу 25 раз.", AchievementCategory.Teamwork, AchievementRarity.Rare, AchievementMetric.BombDefuses, 25),

            A("ability.first", "Пробуждение силы", "Успешно применить способность.", AchievementCategory.Abilities, AchievementRarity.Common, AchievementMetric.AbilityUses, 1),
            A("ability.uses_250", "Повелитель навыков", "Успешно применить способности 250 раз.", AchievementCategory.Abilities, AchievementRarity.Epic, AchievementMetric.AbilityUses, 250),
            A("ability.ultimate_1", "Высшая сила", "Впервые успешно применить ultimate.", AchievementCategory.Abilities, AchievementRarity.Rare, AchievementMetric.UltimateUses, 1),
            A("ability.ultimate_100", "Абсолютная мощь", "Успешно применить ultimate 100 раз.", AchievementCategory.Abilities, AchievementRarity.Legendary, AchievementMetric.UltimateUses, 100),

            T("mechanic.chain_three", "Цепная реакция", "Одной Цепной молнией поразить минимум 3 цели.", AchievementCategory.Abilities, AchievementRarity.Rare, 3, AbilityTelemetryKind.TargetsHit, TelemetryAggregate.Max, "chain_lightning"),
            T("mechanic.chain_kills_25", "Громовержец", "Убить 25 врагов Цепной молнией.", AchievementCategory.Abilities, AchievementRarity.Epic, 25, AbilityTelemetryKind.Kill, TelemetryAggregate.Sum, "chain_lightning"),
            T("mechanic.reflect_damage_1000", "Возмездие", "Отразить суммарно 1 000 урона.", AchievementCategory.Abilities, AchievementRarity.Rare, 1000, AbilityTelemetryKind.DamageDealt, TelemetryAggregate.Sum, "reflect_damage"),
            Secret(T("mechanic.reflect_kill", "Сам себя наказал", "Убить врага отражённым уроном.", AchievementCategory.Abilities, AchievementRarity.Epic, 1, AbilityTelemetryKind.Kill, TelemetryAggregate.Sum, "reflect_damage")),
            T("mechanic.vamp_heal_5000", "Кровопийца", "Восстановить 5 000 HP вампиризмом.", AchievementCategory.Abilities, AchievementRarity.Epic, 5000, AbilityTelemetryKind.Healing, TelemetryAggregate.Sum, "vampirism"),
            T("mechanic.reduction_10000", "Крепче стали", "Предотвратить 10 000 урона пассивным снижением.", AchievementCategory.Abilities, AchievementRarity.Epic, 10000, AbilityTelemetryKind.DamagePrevented, TelemetryAggregate.Sum, "damage_reduction"),
            T("mechanic.divine_1000", "Под защитой света", "Поглотить 1 000 урона Божественным щитом.", AchievementCategory.Abilities, AchievementRarity.Rare, 1000, AbilityTelemetryKind.DamagePrevented, TelemetryAggregate.Sum, "divine_shield"),
            T("mechanic.second_wind_25", "Второе дыхание", "25 раз пережить критический момент благодаря Второму дыханию.", AchievementCategory.Abilities, AchievementRarity.Epic, 25, AbilityTelemetryKind.Triggered, TelemetryAggregate.Sum, "second_wind"),
            T("mechanic.life_drain_heal_2000", "Пожиратель жизни", "Восстановить 2 000 HP Похищением жизни.", AchievementCategory.Abilities, AchievementRarity.Rare, 2000, AbilityTelemetryKind.Healing, TelemetryAggregate.Sum, "life_drain"),
            T("mechanic.totems_100", "Тотемист", "Установить 100 тотемов.", AchievementCategory.Abilities, AchievementRarity.Rare, 100, AbilityTelemetryKind.TotemPlaced, TelemetryAggregate.Sum, "healing_totem", "flame_totem", "frost_totem", "war_totem", "shield_totem"),
            T("mechanic.healing_totem_5000", "Дух целителя", "Восстановить союзникам 5 000 HP Тотемом исцеления.", AchievementCategory.Teamwork, AchievementRarity.Epic, 5000, AbilityTelemetryKind.Healing, TelemetryAggregate.Sum, "healing_totem"),
            T("mechanic.flame_totem_5000", "Огненный идол", "Нанести 5 000 урона Тотемом пламени.", AchievementCategory.Abilities, AchievementRarity.Epic, 5000, AbilityTelemetryKind.DamageDealt, TelemetryAggregate.Sum, "flame_totem"),
            T("mechanic.flame_totem_kills_25", "Жертвенный костёр", "Убить 25 врагов Тотемом пламени.", AchievementCategory.Abilities, AchievementRarity.Legendary, 25, AbilityTelemetryKind.Kill, TelemetryAggregate.Sum, "flame_totem"),
            T("mechanic.shield_totem_5000", "Хранитель племени", "Предотвратить 5 000 урона союзникам Тотемом защиты.", AchievementCategory.Teamwork, AchievementRarity.Epic, 5000, AbilityTelemetryKind.DamagePrevented, TelemetryAggregate.Sum, "shield_totem"),
            T("mechanic.devotion_10000", "Аура защитника", "Предотвратить союзникам 10 000 урона Аурой преданности.", AchievementCategory.Teamwork, AchievementRarity.Legendary, 10000, AbilityTelemetryKind.DamagePrevented, TelemetryAggregate.Sum, "devotion_aura"),
            T("mechanic.vampiric_aura_5000", "Кровавая поддержка", "Восстановить союзникам 5 000 HP Вампирской аурой.", AchievementCategory.Teamwork, AchievementRarity.Epic, 5000, AbilityTelemetryKind.Healing, TelemetryAggregate.Sum, "vampiric_aura")
        };

        foreach (var race in api.Races.GetAll().OrderBy(x => x.Name))
        {
            definitions.Add(new AchievementDefinition(
                $"mastery.{race.Id}",
                $"Мастер: {race.Name}",
                $"Достичь максимального уровня расы {race.Name}.",
                AchievementCategory.Mastery,
                race.VipOnly ? AchievementRarity.Legendary : AchievementRarity.Epic,
                AchievementMetric.RaceLevel,
                race.MaxLevel,
                false,
                race.Id));
        }

        return definitions;
    }

    private static AchievementDefinition A(
        string id,
        string name,
        string description,
        AchievementCategory category,
        AchievementRarity rarity,
        AchievementMetric metric,
        long target,
        bool secret = false)
        => new(id, name, description, category, rarity, metric, target, secret);

    private static AchievementDefinition T(
        string id,
        string name,
        string description,
        AchievementCategory category,
        AchievementRarity rarity,
        long target,
        AbilityTelemetryKind kind,
        TelemetryAggregate aggregate,
        params string[] abilityIds)
        => new(
            id,
            name,
            description,
            category,
            rarity,
            AchievementMetric.AbilityTelemetry,
            target,
            Telemetry: new AbilityTelemetryRule(
                abilityIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
                kind,
                aggregate));

    private static AchievementDefinition Secret(AchievementDefinition definition)
        => definition with { Secret = true };
}
