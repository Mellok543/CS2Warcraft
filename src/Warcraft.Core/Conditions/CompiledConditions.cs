namespace Warcraft.Core.Conditions;

/// <summary>Immutable, pre-parsed condition set of one race ability.</summary>
internal sealed class CompiledConditions
{
    private readonly IAbilityCondition[] _conditions;

    public CompiledConditions(IReadOnlyCollection<IAbilityCondition> conditions)
        => _conditions = conditions.ToArray();

    public static CompiledConditions Empty { get; } = new([]);

    public bool IsEmpty => _conditions.Length == 0;

    /// <summary>Player-facing summary, null when there are no conditions.</summary>
    public string? Description
        => IsEmpty ? null : string.Join("; ", _conditions.Select(x => x.Description));

    public bool IsSatisfied(in PlayerCombatState state)
    {
        foreach (var condition in _conditions)
        {
            if (!condition.IsSatisfied(state))
                return false;
        }

        return true;
    }
}
