#if DEBUG
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;

namespace Funnies.Commands;

public class CommandDebug
{
    public static void OnDebugCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!AdminManager.PlayerHasPermissions(caller, Globals.Config.AdminPermission)) return;
        if (!Util.IsPlayerValid(caller) || caller.PlayerPawn.Value?.AbsOrigin is not { } position)
        {
            command.ReplyToCommand("This debug command requires an in-game player.");
            return;
        }
        if (command.ArgString == "bot")
        {
            var botPawn = Util.GetBots().FirstOrDefault()?.PlayerPawn.Value;
            if (botPawn == null) { command.ReplyToCommand("No bot available."); return; }
            botPawn.Teleport(position, botPawn.AbsRotation, botPawn.AbsVelocity);
        }
        else if (command.ArgString == "bomb")
        {
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
            if (gameRules == null) return;
            var plantedC4 = Utilities.CreateEntityByName<CPlantedC4>("planted_c4");
            if (plantedC4 == null) return;
            plantedC4.Teleport(position, null, null);
            plantedC4.HasExploded = false;

            plantedC4.BombSite = 0;
            plantedC4.BombTicking = true;
            plantedC4.CannotBeDefused = false;

            plantedC4.DispatchSpawn();

            gameRules.BombPlanted = true;
            gameRules.BombDefused = false;
            var eventPtr = NativeAPI.CreateEvent("bomb_planted", true);
            NativeAPI.SetEventPlayerController(eventPtr, "userid", caller.Handle);
            NativeAPI.SetEventInt(eventPtr, "site", 0);

            NativeAPI.FireEvent(eventPtr, false);
        }
        else command.ReplyToCommand("Usage: css_debug <bot|bomb>");
    }
}
#endif
