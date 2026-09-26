using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Warcraft.Core.Abilities;

namespace Warcraft.Core.Game;

/// <summary>
/// Player input for active abilities. Players bind keys to the console commands,
/// e.g. <c>bind x css_ultimate</c> and <c>bind c "css_ability 1"</c>, or type
/// <c>!ultimate</c> / <c>!ability 1</c> in chat. Core only validates and publishes;
/// mechanics live in Warcraft.Abilities.
/// </summary>
internal sealed class AbilityInputCommands(AbilityActivationService activation)
{
    public void Register(BasePlugin plugin)
    {
        plugin.AddCommand("css_ultimate", "Use the Warcraft race ultimate", OnUltimate);
        plugin.AddCommand("css_ability", "Use a Warcraft active ability: css_ability [slot]", OnAbility);
    }

    private void OnUltimate(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanActivate(player))
            return;

        Report(player!, activation.ActivateUltimate(player!.SteamID));
    }

    private void OnAbility(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanActivate(player))
            return;

        var slot = 1;
        if (command.ArgCount >= 2 && (!int.TryParse(command.GetArg(1), out slot) || slot < 1))
        {
            player!.PrintToChat(" [Warcraft] Использование: !ability [номер слота]");
            return;
        }

        Report(player!, activation.ActivateAbility(player!.SteamID, slot));
    }

    private static bool CanActivate(CCSPlayerController? player)
    {
        if (player is not { IsValid: true, IsBot: false } || player.SteamID == 0)
            return false;

        if (player.PawnIsAlive)
            return true;

        player.PrintToChat(" [Warcraft] Способности доступны только живым игрокам.");
        return false;
    }

    private static void Report(CCSPlayerController player, AbilityActivationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
            player.PrintToChat($" [Warcraft] {result.Message}");
    }
}
