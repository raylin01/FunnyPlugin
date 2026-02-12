using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Funnies.Modules;

namespace Funnies.Commands;

public static class CommandEconomy
{
    public static void OnSpecialMoneyCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!HasAccess(caller)) return;

        var args = SplitArguments(command.ArgString);
        if (args.Count == 0 || args[0].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            PrintSpecialMoneyConfig(caller);
            return;
        }

        if (args[0].Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 2 || !TryParseBool(args[1], out var enabled))
            {
                Reply(caller, "Usage: !specialmoney enabled <0|1|true|false>");
                return;
            }

            Globals.Config.SpecialPlayerRoundMoneyEnabled = enabled;
            Reply(caller, $"Special money rule enabled: {enabled}");
            PersistConfigReply(caller);
            return;
        }

        if (args[0].Equals("amount", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 2 || !int.TryParse(args[1], out var amount))
            {
                Reply(caller, "Usage: !specialmoney amount <money>");
                return;
            }

            Globals.Config.SpecialPlayerRoundMoneyAmount = Math.Max(0, amount);
            Reply(caller, $"Special money amount set to: {Globals.Config.SpecialPlayerRoundMoneyAmount}");
            PersistConfigReply(caller);
            return;
        }

        if (args[0].Equals("rounds", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 5 ||
                !int.TryParse(args[1], out var startFirst) ||
                !int.TryParse(args[2], out var endFirst) ||
                !int.TryParse(args[3], out var startSecond) ||
                !int.TryParse(args[4], out var endSecond))
            {
                Reply(caller, "Usage: !specialmoney rounds <start1> <end1> <start2> <end2>");
                return;
            }

            Globals.Config.SpecialPlayerMoneyRoundStartFirstHalf = Math.Max(1, startFirst);
            Globals.Config.SpecialPlayerMoneyRoundEndFirstHalf = Math.Max(1, endFirst);
            Globals.Config.SpecialPlayerMoneyRoundStartSecondHalf = Math.Max(1, startSecond);
            Globals.Config.SpecialPlayerMoneyRoundEndSecondHalf = Math.Max(1, endSecond);

            Reply(caller, $"Special money rounds set to {Globals.Config.SpecialPlayerMoneyRoundStartFirstHalf}-{Globals.Config.SpecialPlayerMoneyRoundEndFirstHalf} and {Globals.Config.SpecialPlayerMoneyRoundStartSecondHalf}-{Globals.Config.SpecialPlayerMoneyRoundEndSecondHalf}");
            PersistConfigReply(caller);
            return;
        }

        Reply(caller, "Usage: !specialmoney <show|enabled|amount|rounds>");
    }

    public static void OnNadeLimitCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!HasAccess(caller)) return;

        var args = SplitArguments(command.ArgString);
        if (args.Count == 0 || args[0].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            PrintNadeConfig(caller);
            return;
        }

        if (args[0].Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 2 || !TryParseBool(args[1], out var enabled))
            {
                Reply(caller, "Usage: !nadelimit enabled <0|1|true|false>");
                return;
            }

            Globals.Config.LimitNonSpecialGrenadeBuys = enabled;
            Reply(caller, $"Non-special grenade buy limit enabled: {enabled}");
            PersistConfigReply(caller);
            return;
        }

        if (args[0].Equals("limit", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 2 || !int.TryParse(args[1], out var maxNades))
            {
                Reply(caller, "Usage: !nadelimit limit <count>");
                return;
            }

            Globals.Config.NonSpecialGrenadeBuyLimit = Math.Max(0, maxNades);
            Reply(caller, $"Non-special grenade buy limit set to: {Globals.Config.NonSpecialGrenadeBuyLimit}");
            PersistConfigReply(caller);
            return;
        }

        Reply(caller, "Usage: !nadelimit <show|enabled|limit>");
    }

    private static List<string> SplitArguments(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return [];
        return args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static void PrintSpecialMoneyConfig(CCSPlayerController? caller)
    {
        Reply(caller, $"Special money enabled: {Globals.Config.SpecialPlayerRoundMoneyEnabled}");
        Reply(caller, $"Special money amount: {Globals.Config.SpecialPlayerRoundMoneyAmount}");
        Reply(caller, $"Special money rounds: {Globals.Config.SpecialPlayerMoneyRoundStartFirstHalf}-{Globals.Config.SpecialPlayerMoneyRoundEndFirstHalf}, {Globals.Config.SpecialPlayerMoneyRoundStartSecondHalf}-{Globals.Config.SpecialPlayerMoneyRoundEndSecondHalf}");
        Reply(caller, $"Current round tracker: {Economy.CurrentRound}");
    }

    private static void PrintNadeConfig(CCSPlayerController? caller)
    {
        Reply(caller, $"Non-special nade limit enabled: {Globals.Config.LimitNonSpecialGrenadeBuys}");
        Reply(caller, $"Non-special max nades per round: {Globals.Config.NonSpecialGrenadeBuyLimit}");
    }

    private static bool HasAccess(CCSPlayerController? caller)
    {
        return caller == null || AdminManager.PlayerHasPermissions(caller, Globals.Config.AdminPermission);
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
