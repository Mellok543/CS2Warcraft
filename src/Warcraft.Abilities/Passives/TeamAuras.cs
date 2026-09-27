using System.Drawing;
using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;

namespace Warcraft.Abilities.Passives;

/// <summary>
/// Base for auras that keep a buff on everyone in range. Pulses twice per second
/// and grants the effect slightly longer, so it fades right after leaving the aura.
/// </summary>
internal abstract class ContinuousAura : AuraAbility
{
    protected const double Linger = 0.75;

    protected override double DefaultInterval => 0.5;

    protected static float Radius(PlayerAbilitySnapshot ability, double fallback = 400)
        => (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(ability, "radius", fallback));
}

/// <summary>Aura: nearby enemies move slower. Config: slow (movement multiplier, e.g. 0.75), radius.</summary>
internal sealed class SlowAuraAbility(MovementController movement) : ContinuousAura
{
    public override string Id => "slow_aura";
    protected override Color AuraColor => Color.FromArgb(200, 110, 200, 255);
    protected override FxColor FxColor => FxColor.Frost;
    protected override string Description => "Аура: враги в радиусе {radius} двигаются со скоростью x{slow}.";
    protected override string DisplayName => "Аура холода";

    protected override void Pulse(LivePlayer owner, PlayerAbilitySnapshot ability)
    {
        var slow = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "slow", 0.8), 0.1, 1.0);
        var until = Server.CurrentTime + Linger;

        foreach (var enemy in GamePlayers.EnemiesAround(owner, owner.Position, Radius(ability, 300)))
            movement.Stun(enemy, slow, until);
    }
}

/// <summary>Aura: you and nearby teammates move faster. Config: multiplier, radius.</summary>
internal sealed class SpeedAuraAbility(MovementController movement) : ContinuousAura
{
    public override string Id => "speed_aura";
    protected override Color AuraColor => Color.FromArgb(200, 240, 240, 140);
    protected override FxColor FxColor => FxColor.Storm;
    protected override string Description => "Аура: союзники в радиусе {radius} быстрее в x{multiplier}.";
    protected override string DisplayName => "Аура скорости";

    protected override void Pulse(LivePlayer owner, PlayerAbilitySnapshot ability)
    {
        var multiplier = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "multiplier", 1.1), 1.0, 2.0);
        var until = Server.CurrentTime + Linger;

        foreach (var ally in GamePlayers.AlliesAround(owner, owner.Position, Radius(ability)))
            movement.Boost(ally.Controller.Slot, multiplier, until);
    }
}

/// <summary>Base for auras that grant a <see cref="TeamBuffs"/> buff to teammates in range.</summary>
internal abstract class BuffAura(TeamBuffs buffs, BuffKind kind) : ContinuousAura
{
    protected override void Pulse(LivePlayer owner, PlayerAbilitySnapshot ability)
    {
        var value = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(ability, "percent"));
        var now = Server.CurrentTime;

        foreach (var ally in GamePlayers.AlliesAround(owner, owner.Position, Radius(ability)))
        {
            if (!ally.Controller.IsBot)
                buffs.Grant(
                    ally.Controller.SteamID,
                    kind,
                    value,
                    now + Linger,
                    now,
                    owner.Controller.SteamID,
                    Id);
        }
    }
}

/// <summary>Aura: teammates in range deal more damage. Config: percent, radius.</summary>
internal sealed class CommandAuraAbility(TeamBuffs buffs) : BuffAura(buffs, BuffKind.DamageBonus)
{
    public override string Id => "command_aura";
    protected override Color AuraColor => Color.FromArgb(200, 230, 50, 50);
    protected override FxColor FxColor => FxColor.War;
    protected override string Description => "Аура: союзники в радиусе {radius} наносят на {percent%} больше урона.";
    protected override string DisplayName => "Командная аура";
}

/// <summary>Aura: teammates in range take less damage. Config: percent, radius.</summary>
internal sealed class DevotionAuraAbility(TeamBuffs buffs) : BuffAura(buffs, BuffKind.DamageReduction)
{
    public override string Id => "devotion_aura";
    protected override Color AuraColor => Color.FromArgb(200, 245, 210, 80);
    protected override FxColor FxColor => FxColor.Holy;
    protected override string Description => "Аура: союзники в радиусе {radius} получают на {percent%} меньше урона.";
    protected override string DisplayName => "Аура преданности";
}

/// <summary>Aura: teammates in range heal for a share of damage dealt. Config: percent, radius.</summary>
internal sealed class VampiricAuraAbility(TeamBuffs buffs) : BuffAura(buffs, BuffKind.Lifesteal)
{
    public override string Id => "vampiric_aura";
    protected override Color AuraColor => Color.FromArgb(220, 170, 0, 45);
    protected override FxColor FxColor => FxColor.Blood;
    protected override string Description => "Аура: союзники в радиусе {radius} лечатся на {percent%} от урона.";
    protected override string DisplayName => "Вампирская аура";
}
