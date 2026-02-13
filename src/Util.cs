using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace Funnies;

public static class Util
{

    public static string GetPlayerModel(CCSPlayerController player)
    {
        // This hurts
        return player.Pawn.Value!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName;
    }

    public static bool IsPlayerValid([NotNullWhen(true)] CCSPlayerController? plr) => plr != null &&
               plr.IsValid &&
               plr.PlayerPawn != null &&
               plr.PlayerPawn.IsValid &&
               plr.Connected == PlayerConnectedState.PlayerConnected &&
               !plr.IsHLTV;

    public static List<CCSPlayerController> GetValidPlayers() => [.. Utilities.GetPlayers().Where(IsPlayerValid)];
    public static List<CCSPlayerController> GetBots() => [.. GetValidPlayers().Where(plr => plr.IsBot)];
    public static List<CCSPlayerController> GetRealPlayers() => [.. GetValidPlayers().Where(plr => !plr.IsBot)];

    public static float Map(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        float normalized = (value - fromMin) / (fromMax - fromMin);
        return toMin + normalized * (toMax - toMin);
    }

    public static CCSPlayerController? GetPlayerByName(string name)
    {
        return GetValidPlayers().FirstOrDefault(x => x!.PlayerName == name, null);
    }

    public static bool IsSpecialPlayer(CCSPlayerController player)
    {
        return Globals.Wallhackers.Contains(player.Slot) || Globals.InvisiblePlayers.ContainsKey(player);
    }

    public static void ServerPrintToChat(CCSPlayerController player, string message)
    {
        player.PrintToChat($" {ChatColors.Green}[SERVER]{ChatColors.White} {message}");
    }

    public static List<CGameSceneNode> GetChildrenRecursive(CGameSceneNode gameSceneNode)
    {
        var children = new List<CGameSceneNode>();
        var stack = new Stack<CGameSceneNode>();

        for (var child = gameSceneNode.Child; child != null; child = child.NextSibling)
            stack.Push(child);

        // Traverse without mutating the collection being iterated.
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            children.Add(node);

            for (var child = node.Child; child != null; child = child.NextSibling)
                stack.Push(child);
        }

        return children;
    }
}
