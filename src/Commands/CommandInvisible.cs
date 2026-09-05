using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace Funnies.Commands;

public class CommandInvisible
{
    public static void OnInvisibleCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!AdminManager.PlayerHasPermissions(caller, Globals.Config.AdminPermission)) return;

        var player = Util.GetPlayerByName(command.ArgString);

        if (player != null)
        {
            if (Globals.InvisiblePlayers.Remove(player))
            {
                command.ReplyToCommand($"Invisibility disabled: {player.PlayerName}");
            }
            else
            {
                Globals.InvisiblePlayers.Add(player, new());
                command.ReplyToCommand($"Invisibility enabled: {player.PlayerName}");
            }
        }
        else
        {
            command.ReplyToCommand($"Player {command.ArgString} not found");
        }
    }
}
