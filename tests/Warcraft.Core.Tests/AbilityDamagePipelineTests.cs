using Warcraft.Api.Events;
using Warcraft.Core.Events;
using Warcraft.Core.Game;

namespace Warcraft.Core.Tests;

public sealed class AbilityDamagePipelineTests
{
    private const ulong Attacker = 1;
    private const ulong Victim = 2;

    [Fact]
    public void DefensiveModifiersSeeAbilityDamageAsAbilityKind()
    {
        var bus = new WarcraftEventBus(e => throw e);
        var pipeline = new AbilityDamagePipeline(bus);
        DamagePreEvent? seen = null;
        bus.Subscribe<DamagePreEvent>(e =>
        {
            seen = e;
            e.Damage *= 0.5f; // e.g. a 50% shield
        });

        var dealt = pipeline.Resolve(Attacker, Victim, 40, "chain_lightning");

        Assert.Equal(20, dealt);
        Assert.NotNull(seen);
        Assert.True(seen.IsAbilityDamage);
        Assert.Equal("chain_lightning", seen.AbilityId);
        Assert.Equal(Attacker, seen.AttackerSteamId);
    }

    [Fact]
    public void AbilityDamageNeverPublishesDamagePost()
    {
        var bus = new WarcraftEventBus(e => throw e);
        var pipeline = new AbilityDamagePipeline(bus);
        var posts = 0;
        bus.Subscribe<DamagePostEvent>(_ => posts++);

        pipeline.Resolve(Attacker, Victim, 25, "reflect_damage");

        // Reflect, vampirism, poison and other on-hit effects listen to DamagePost,
        // so ability damage cannot start a reflect -> ability damage -> reflect chain.
        Assert.Equal(0, posts);
    }

    [Fact]
    public void NestedAbilityDamageFromDamagePreIsBounded()
    {
        var bus = new WarcraftEventBus(e => throw e);
        var pipeline = new AbilityDamagePipeline(bus);
        var published = 0;
        var nestedResult = -1;

        bus.Subscribe<DamagePreEvent>(e =>
        {
            published++;
            // A misbehaving handler that deals ability damage while handling ability damage.
            nestedResult = pipeline.Resolve(e.VictimSteamId, e.AttackerSteamId, 10, "reflect_damage");
        });

        pipeline.Resolve(Attacker, Victim, 30, "smite");

        Assert.Equal(1, published);
        Assert.Equal(10, nestedResult);
    }

    [Fact]
    public void BotsAndNonPositiveAmountsBypassModifiers()
    {
        var bus = new WarcraftEventBus(e => throw e);
        var pipeline = new AbilityDamagePipeline(bus);
        var published = 0;
        bus.Subscribe<DamagePreEvent>(e =>
        {
            published++;
            e.Damage = -5;
        });

        Assert.Equal(30, pipeline.Resolve(Attacker, null, 30, "smite"));
        Assert.Equal(0, pipeline.Resolve(Attacker, Victim, 0, "smite"));
        Assert.Equal(0, published);
        Assert.Equal(0, pipeline.Resolve(Attacker, Victim, 30, "smite"));
    }
}
