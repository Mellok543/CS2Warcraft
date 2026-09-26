namespace Warcraft.Core.Conditions;

/// <summary>Immutable, pre-parsed condition set of one race ability.</summary>
internal sealed class CompiledConditions
{
    private readonly IAbilityCondition[] _conditions;

    public CompiledConditions(IReadOnlyCollection<IAbilityCondition> conditions)
        => _conditions = conditions.ToArray();

    public static CompiledConditions Empty { get; } = new([]);

    public bool IsEmpty => _conditions.Length == 0;

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
