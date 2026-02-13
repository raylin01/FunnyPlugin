using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace Funnies.Commands;

public static class CommandVisuals
{
    public static void OnSkinsCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!HasAccess(caller)) return;

        var args = SplitArguments(command.ArgString);
        if (args.Count == 0 || args[0].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            Reply(caller, $"Server-wide skin suppression: {Globals.Config.DisableSkinsServerWide}");
            return;
        }

        if (args[0].Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 2 || !TryParseBool(args[1], out var enabled))
            {
                Reply(caller, "Usage: !skins enabled <0|1|true|false>");
                return;
            }

            Globals.Config.DisableSkinsServerWide = enabled;
            Reply(caller, $"Server-wide skin suppression set to: {enabled}");
            PersistConfigReply(caller);
            return;
        }

        Reply(caller, "Usage: !skins <show|enabled>");
    }

    private static bool HasAccess(CCSPlayerController? caller)
    {
        return caller == null || AdminManager.PlayerHasPermissions(caller, Globals.Config.AdminPermission);
    }

    private static List<string> SplitArguments(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return [];
        return args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static bool TryParseBool(string value, out bool result)
    {
        result = false;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "1" or "true" or "on" or "yes")
        {
            result = true;
            return true;
        }

        if (normalized is "0" or "false" or "off" or "no")
        {
            result = false;
            return true;
        }

        return false;
    }

    private static void Reply(CCSPlayerController? caller, string message)
    {
        if (Util.IsPlayerValid(caller))
        {
            Util.ServerPrintToChat(caller!, message);
            return;
        }

        Console.WriteLine($"[Funnies] {message}");
    }

    private static void PersistConfigReply(CCSPlayerController? caller)
    {
        if (ConfigPersistence.TryPersist(out var details))
        {
            Reply(caller, "Config persisted to JSON.");
            Console.WriteLine($"[Funnies] {details}");
            return;
        }

        Reply(caller, $"Failed to persist config: {details}");
        Console.WriteLine($"[Funnies] Failed to persist config: {details}");
    }
}
