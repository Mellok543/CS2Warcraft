using CounterStrikeSharp.API;

namespace Warcraft.Core.Game;

/// <summary>Runs work on the game thread and returns its result to async callers.</summary>
internal interface IGameThreadDispatcher
{
    Task<T> InvokeAsync<T>(Func<T> work);
}

internal sealed class CssGameThreadDispatcher : IGameThreadDispatcher
{
    public Task<T> InvokeAsync<T>(Func<T> work) => Server.NextFrameAsync(work);
}
