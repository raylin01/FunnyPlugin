using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace Funnies.Commands;

public static class CommandMatch
{
    private static bool HasAccess(CCSPlayerController? caller)
    {
        return AdminManager.PlayerHasPermissions(caller, Globals.Config.RconPermission);
    }

    private static void Reply(CCSPlayerController? caller, string message)
    {
        if (Util.IsPlayerValid(caller))
            Util.ServerPrintToChat(caller!, message);
    }

    private static void RunCommands(params string[] commands)
    {
        foreach (var command in commands)
            Server.ExecuteCommand(command);
    }

    public static void OnStartCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!HasAccess(caller)) return;

        RunCommands(
            "mp_unpause_match",
            "mp_warmup_end",
            "mp_restartgame 1"
        );

        Reply(caller, "Live game started.");
    }

    public static void OnPauseCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!HasAccess(caller)) return;

        Server.ExecuteCommand("mp_pause_match");
        Reply(caller, "Match paused.");
    }

    public static void OnStopCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!HasAccess(caller)) return;

        RunCommands(
            "mp_do_warmup_period 1",
            "mp_warmup_pausetimer 1",
            "mp_warmup_start"
        );

        Reply(caller, "Returned to warmup.");
    }

    public static void OnRestartRoundCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!HasAccess(caller)) return;

        Server.ExecuteCommand("mp_restartgame 1");
        Reply(caller, "Round restarted.");
    }

    public static void OnMapCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!HasAccess(caller)) return;

        var map = command.ArgString.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(map))
        {
            Reply(caller, "Usage: !map <mapname>");
            return;
        }

        Server.ExecuteCommand($"changelevel {map}");
        Reply(caller, $"Changing map to {map}.");
    }
}
