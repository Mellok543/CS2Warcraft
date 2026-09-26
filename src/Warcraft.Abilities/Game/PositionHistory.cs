using System.Numerics;

namespace Warcraft.Abilities.Game;

/// <summary>
/// Recent positions of alive human players, sampled on every Core game tick.
/// Used by <c>recall</c>; the history of a player is dropped when they die.
/// </summary>
internal sealed class PositionHistory
{
    private const double MaxSeconds = 10;

    private readonly Dictionary<int, Queue<Sample>> _samples = [];

    public void Record(double now)
    {
        var alive = new HashSet<int>();

        foreach (var player in GamePlayers.AllAliveHumans())
        {
            var slot = player.Controller.Slot;
            alive.Add(slot);

            if (!_samples.TryGetValue(slot, out var queue))
            {
                queue = new Queue<Sample>();
                _samples[slot] = queue;
            }

            queue.Enqueue(new Sample(now, player.Position));
            while (queue.Count > 0 && now - queue.Peek().Time > MaxSeconds)
                queue.Dequeue();
        }

        foreach (var slot in _samples.Keys.Where(x => !alive.Contains(x)).ToArray())
            _samples.Remove(slot);
    }

    /// <summary>The oldest known position that is at least <paramref name="seconds"/> old (or the oldest available).</summary>
    public Vector3? GetAgo(int slot, double seconds, double now)
    {
        if (!_samples.TryGetValue(slot, out var queue) || queue.Count == 0)
            return null;

        Sample? best = null;
        foreach (var sample in queue)
        {
            if (now - sample.Time >= seconds)
                best = sample;
        }

        return (best ?? queue.Peek()).Position;
    }

    public void Clear() => _samples.Clear();

    private readonly record struct Sample(double Time, Vector3 Position);
}
