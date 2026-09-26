using Warcraft.Api.Races;
using Warcraft.Core.Conditions;

namespace Warcraft.Core.Races;

internal sealed record CompiledAbility(
    RaceAbilityDefinition Definition,
    bool IsUltimate,
    CompiledConditions Conditions)
{
    public string Id => Definition.Id;
}

internal sealed class CompiledRace
{
    private readonly IReadOnlyDictionary<string, CompiledAbility> _byId;

    public CompiledRace(RaceDefinition definition, IReadOnlyList<CompiledAbility> abilities)
    {
        Definition = definition;
        Abilities = abilities;
        Ultimate = abilities.FirstOrDefault(x => x.IsUltimate);
        _byId = abilities.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public RaceDefinition Definition { get; }

    /// <summary>Regular abilities in JSON order, followed by the ultimate.</summary>
    public IReadOnlyList<CompiledAbility> Abilities { get; }

    public CompiledAbility? Ultimate { get; }

    public CompiledAbility? Find(string abilityId)
        => _byId.GetValueOrDefault(abilityId);
}

/// <summary>Immutable validated race catalog; replaced atomically as a whole.</summary>
internal sealed class CompiledRaceCatalog(IReadOnlyDictionary<string, CompiledRace> races)
{
    public static CompiledRaceCatalog Empty { get; } =
        new(new Dictionary<string, CompiledRace>(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, CompiledRace> Races { get; } = races;
}
