using System.Text.Json;
using Warcraft.Core.Conditions.Builtin;

namespace Warcraft.Core.Conditions;

/// <summary>
/// Compiles JSON <c>conditions</c> objects into <see cref="CompiledConditions"/>.
/// Immutable after construction, therefore thread-safe.
/// </summary>
internal sealed class AbilityConditionRegistry
{
    private readonly IReadOnlyDictionary<string, IAbilityConditionFactory> _factories;

    public AbilityConditionRegistry(IEnumerable<IAbilityConditionFactory> factories)
        => _factories = factories.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

    public static AbilityConditionRegistry CreateDefault()
        => new(
        [
            new WeaponTypesConditionFactory(),
            new MinHealthPercentConditionFactory(),
            new MaxHealthPercentConditionFactory(),
            new RequiresGroundedConditionFactory()
        ]);

    public CompiledConditions Compile(JsonElement conditions, string owner, List<string> errors)
    {
        if (conditions.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return CompiledConditions.Empty;

        if (conditions.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"'{owner}' conditions must be a JSON object.");
            return CompiledConditions.Empty;
        }

        var compiled = new List<IAbilityCondition>();

        foreach (var property in conditions.EnumerateObject())
        {
            if (!_factories.TryGetValue(property.Name, out var factory))
            {
                errors.Add(
                    $"'{owner}' uses unknown condition '{property.Name}'. " +
                    $"Known: {string.Join(", ", _factories.Keys.Order())}.");
                continue;
            }

            var result = factory.Parse(property.Value);
            if (result.Condition is null)
            {
                errors.Add($"'{owner}' condition '{property.Name}': {result.Error}");
                continue;
            }

            compiled.Add(result.Condition);
        }

        return compiled.Count == 0 ? CompiledConditions.Empty : new CompiledConditions(compiled);
    }
}
