using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using Funnies.Commands;

namespace Funnies.Modules;

public class Wallhack
{
    public static void OnPlayerTransmit(CCheckTransmitInfo info, CCSPlayerController player)
    {
        foreach (var entity in Globals.GlowData)
        {
            if (Globals.Wallhackers.Contains(player!))
            {
                if (!Util.IsPlayerValid(entity.Key) || !Util.IsPlayerValid(player)) continue;

                if (entity.Key.Team != player!.Team && player!.Team != CsTeam.Spectator && entity.Key.Team != CsTeam.Spectator)
                {
                    info.TransmitEntities.Add(entity.Value.ModelRelay);
                    info.TransmitEntities.Add(entity.Value.GlowEnt);
                    continue;
                }
            }

            info.TransmitEntities.Remove(entity.Value.ModelRelay);
            info.TransmitEntities.Remove(entity.Value.GlowEnt);
        }
    }

    public static HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;
        if (player!.Team < CsTeam.Terrorist) return HookResult.Continue; // if player isnt on a team
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First();
        if (gameRules.GameRules!.WarmupPeriod) return HookResult.Continue;

        Glow(player!);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;
        if (!Globals.GlowData.TryGetValue(player!, out var glowData)) return HookResult.Continue;

        if (glowData.GlowEnt.IsValid)
            glowData.GlowEnt.Remove();
        if (glowData.ModelRelay.IsValid)
            glowData.ModelRelay.Remove();

        Globals.GlowData.Remove(player!);
        Globals.Wallhackers.Remove(player!);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;
        
        // Find player in GlowData by UserId to handle reference changes
        var glowEntry = Globals.GlowData.FirstOrDefault(kvp => 
            kvp.Key != null && 
            kvp.Key.IsValid && 
            kvp.Key.UserId == player!.UserId);
        
        if (glowEntry.Key == null) return HookResult.Continue;
        if (!glowEntry.Value.GlowEnt.IsValid) return HookResult.Continue;

        glowEntry.Value.GlowEnt.Glow.GlowRange = 0;
        glowEntry.Value.GlowEnt.DispatchSpawn();

        return HookResult.Continue;
    }

    public static HookResult OnPlayerChangeTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;
        
        // Find player in GlowData by UserId to handle reference changes
        var glowEntry = Globals.GlowData.FirstOrDefault(kvp => 
            kvp.Key != null && 
            kvp.Key.IsValid && 
            kvp.Key.UserId == player!.UserId);
        
        if (glowEntry.Key == null) return HookResult.Continue;
        if (!glowEntry.Value.GlowEnt.IsValid) return HookResult.Continue;
        if (!glowEntry.Value.ModelRelay.IsValid) return HookResult.Continue;

        Server.NextWorldUpdate(() => 
        {
            glowEntry.Value.GlowEnt.SetModel(Util.GetPlayerModel(player));
            glowEntry.Value.ModelRelay.SetModel(Util.GetPlayerModel(player));
        });

        return HookResult.Continue;
    }

    public static HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;
        
        // Find player in GlowData by checking all entries since player reference might change
        var glowEntry = Globals.GlowData.FirstOrDefault(kvp => 
            kvp.Key != null && 
            kvp.Key.IsValid && 
            kvp.Key.UserId == player!.UserId);
        
        if (glowEntry.Key == null) return HookResult.Continue;
        if (!glowEntry.Value.GlowEnt.IsValid) return HookResult.Continue;

        UpdateGlowColor(player!, glowEntry.Value.GlowEnt);

        return HookResult.Continue;
    }

    private static Color GetHealthColor(int health)
    {
        if (health > 100) health = 100;
        if (health < 0) health = 0;

        int r, g, b;
        if (health > 66)
        {
            float t = (100f - health) / 34f;
            r = (int)(255 * t);
            g = 255;
            b = 0;
        }
        else if (health > 33)
        {
            float t = (66f - health) / 33f;
            r = 255;
            g = (int)(255 - 90 * t);
            b = 0;
        }
        else
        {
            float t = (33f - health) / 33f;
            r = 255;
            g = (int)(165 - 165 * t);
            b = 0;
        }

        return Color.FromArgb(255, r, g, b);
    }

    private static void UpdateGlowColor(CCSPlayerController player, CDynamicProp glowEntity)
    {
        int health = 100;
        
        if (player.PlayerPawn?.Value != null && player.PlayerPawn.IsValid)
        {
            health = player.PlayerPawn.Value.Health;
        }

        var color = GetHealthColor(health);
        glowEntity.Glow.GlowColorOverride = color;
        glowEntity.Glow.GlowRange = 5000;
        glowEntity.Glow.GlowRangeMin = 0;
        Utilities.SetStateChanged(glowEntity, "CGlowProperty", "m_glowColorOverride");
    }

    private static void Glow(CCSPlayerController player)
    {
        int health = 100;
        
        if (player.PlayerPawn?.Value != null && player.PlayerPawn.IsValid)
        {
            health = player.PlayerPawn.Value.Health;
        }

        var glowEntity = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        var modelRelay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");

        modelRelay!.Spawnflags = 256;
        modelRelay.Render = Color.Transparent;
        modelRelay.RenderMode = RenderMode_t.kRenderNone;
        modelRelay.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(1u << 2);
        modelRelay.SetModel(Util.GetPlayerModel(player));

        glowEntity!.Spawnflags = 256;
        glowEntity!.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(1u << 2);
        glowEntity.Render = Color.FromArgb(1, 0, 0, 0);
        glowEntity.SetModel(Util.GetPlayerModel(player));

        glowEntity.DispatchSpawn();
        modelRelay.DispatchSpawn();

        glowEntity.Glow.GlowRange = 5000;
        glowEntity.Glow.GlowRangeMin = 0;
        glowEntity.Glow.GlowColorOverride = GetHealthColor(health);
        glowEntity.Glow.GlowTeam = -1;
        glowEntity.Glow.GlowType = 3;

        modelRelay.AcceptInput("FollowEntity", player.Pawn.Value, null, "!activator");
        glowEntity.AcceptInput("FollowEntity", modelRelay, null, "!activator");

        Globals.GlowData.Remove(player);
        Globals.GlowData.Add(player, new() {
            GlowEnt = glowEntity,
            ModelRelay = modelRelay
        });
    }

    public static void Setup()
    {
        Globals.Plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        Globals.Plugin.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        Globals.Plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);
        Globals.Plugin.RegisterEventHandler<EventPlayerTeam>(OnPlayerChangeTeam, HookMode.Post);
        Globals.Plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);

        Globals.Plugin.AddCommand("css_wh", "Gives a player walls", CommandWallhack.OnWallhackCommand);
        Globals.Plugin.AddCommand("css_wallhack", "Gives a player walls", CommandWallhack.OnWallhackCommand);
    }

    public static void Cleanup()
    {
        foreach (var entity in Globals.GlowData)
        {
            Server.NextWorldUpdate(() => 
            {
                entity.Value.GlowEnt.Remove();
                entity.Value.ModelRelay.Remove();
            });
        }

        Globals.GlowData.Clear();
        Globals.Wallhackers.Clear();
    }
}