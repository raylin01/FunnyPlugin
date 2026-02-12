using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace Funnies.Commands;

public static class CommandAk
{
    public static void OnAkCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Util.IsPlayerValid(caller)) return;

        if (!Util.IsSpecialPlayer(caller!))
        {
            Util.ServerPrintToChat(caller, "Only the wallhacker/invisible player can use !ak.");
            return;
        }

        caller.GiveNamedItem("weapon_ak47");
        Util.ServerPrintToChat(caller, "Given AK-47.");
    }
}
