using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace Warcraft.Abilities.Game;

/// <summary>Delayed game-thread callbacks for visual effects.</summary>
internal interface IGameScheduler
{
    void Schedule(float seconds, Action action);
}

internal sealed class PluginGameScheduler(BasePlugin plugin) : IGameScheduler
{
    public void Schedule(float seconds, Action action)
        => plugin.AddTimer(Math.Max(0.01f, seconds), action, TimerFlags.STOP_ON_MAPCHANGE);
}
