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
}

public sealed record RaceAbilityDefinition
{
    public required string Id { get; init; }
    public int MaxLevel { get; init; } = 1;
    public int UnlockLevel { get; init; } = 1;
    public JsonElement Config { get; init; }
    public JsonElement Conditions { get; init; }
}
