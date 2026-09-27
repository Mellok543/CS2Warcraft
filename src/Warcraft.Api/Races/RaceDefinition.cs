using System.Text.Json;

namespace Warcraft.Api.Races;

public sealed record RaceDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int MaxLevel { get; init; } = 10;
    public bool VipOnly { get; init; }
    public IReadOnlyList<RaceAbilityDefinition> Abilities { get; init; } = [];
    public RaceAbilityDefinition? Ultimate { get; init; }

    /// <summary>Unlock conditions; null or empty means the race is open from the start.</summary>
    public RaceRequirements? Requirements { get; init; }
}

/// <summary>All listed requirements must be met to select the race for the first time.</summary>
public sealed record RaceRequirements
{
    /// <summary>Sum of the player's levels over all races.</summary>
    public int TotalLevel { get; init; }

    /// <summary>Lifetime XP of the player.</summary>
    public long GlobalXp { get; init; }

    /// <summary>Minimum recorded server playtime, in whole hours.</summary>
    public int PlaytimeHours { get; init; }

    /// <summary>Minimum level per race id, e.g. { "orc": 5 }.</summary>
    public IReadOnlyDictionary<string, int> Races { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => TotalLevel <= 0 && GlobalXp <= 0 && PlaytimeHours <= 0 && Races.Count == 0;
}

public sealed record RaceAbilityDefinition
{
    public required string Id { get; init; }
    public string? Name { get; init; }

    /// <summary>
    /// Optional race-specific description template; overrides the handler default.
    /// Placeholders: <c>{key}</c>, <c>{key%}</c> (x100 as percent), <c>{key|default}</c>,
    /// resolved from <see cref="Config"/> at the relevant ability level.
    /// </summary>
    public string? Description { get; init; }

    public int MaxLevel { get; init; } = 1;
    public int UnlockLevel { get; init; } = 1;
    public JsonElement Config { get; init; }
    public JsonElement Conditions { get; init; }
}
