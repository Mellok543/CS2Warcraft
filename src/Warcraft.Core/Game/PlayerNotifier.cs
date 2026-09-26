using CounterStrikeSharp.API;
using Warcraft.Api.Events;

namespace Warcraft.Core.Game;

/// <summary>Chat feedback for XP gains and level-ups.</summary>
internal sealed class PlayerNotifier : IDisposable
{
    private readonly IDisposable _subscription;

    public PlayerNotifier(IWarcraftEventBus events)
        => _subscription = events.Subscribe<PlayerXpGainedEvent>(OnXpGained);

    public void Dispose() => _subscription.Dispose();

    private static void OnXpGained(PlayerXpGainedEvent gained)
    {
        if (gained.Amount <= 0)
            return;

        var player = Utilities.GetPlayerFromSteamId(gained.SteamId);
        if (player is not { IsValid: true })
            return;

        var reason = gained.Reason.StartsWith("admin:", StringComparison.Ordinal) ? "администратор" : gained.Reason;
        player.PrintToChat($" [Warcraft] +{gained.Amount} XP ({reason})");

        if (gained.LeveledUp)
        {
            player.PrintToChat(
                $" [Warcraft] Новый уровень расы: {gained.CurrentLevel}! Откройте !wc -> «Прокачка способностей».");
        }
    }
}
