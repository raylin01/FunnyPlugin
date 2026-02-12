using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Funnies.Commands;

namespace Funnies.Modules;

public class Wallhack
{
    private static CCSPlayerController? GetPlayerBySlot(int slot)
    {
        return Util.GetValidPlayers().FirstOrDefault(player => player.Slot == slot, null);
    }

    private static bool IsLivePlayer(CCSPlayerController? player)
    {
        return Util.IsPlayerValid(player) &&
               player.Team >= CsTeam.Terrorist &&
               player.Team != CsTeam.Spectator;
    }

    private static void RemoveGlowForSlot(int slot)
    {
        if (!Globals.GlowData.TryGetValue(slot, out var glowData)) return;

        if (glowData.GlowEnt.IsValid)
            glowData.GlowEnt.Remove();
        if (glowData.ModelRelay.IsValid)
            glowData.ModelRelay.Remove();

        Globals.GlowData.Remove(slot);
    }

    public static void OnTick()
    {
        foreach (var entry in Globals.GlowData.ToList())
        {
            if (!entry.Value.GlowEnt.IsValid) continue;

            var target = GetPlayerBySlot(entry.Key);
            if (!IsLivePlayer(target)) continue;

            UpdateGlowColor(target!, entry.Value.GlowEnt);
        }
    }

    public static void OnPlayerTransmit(CCheckTransmitInfo info, CCSPlayerController player)
    {
        var viewerHasWallhack = Globals.Wallhackers.Contains(player.Slot);

        foreach (var entry in Globals.GlowData)
        {
            if (!entry.Value.GlowEnt.IsValid || !entry.Value.ModelRelay.IsValid)
                continue;

            var target = GetPlayerBySlot(entry.Key);
            var shouldShow = viewerHasWallhack &&
                             IsLivePlayer(target) &&
                             target!.Slot != player.Slot;

            if (shouldShow)
            {
                info.TransmitEntities.Add(entry.Value.ModelRelay);
                info.TransmitEntities.Add(entry.Value.GlowEnt);
                continue;
            }

            info.TransmitEntities.Remove(entry.Value.ModelRelay);
            info.TransmitEntities.Remove(entry.Value.GlowEnt);
        }
    }

    public static HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsLivePlayer(player)) return HookResult.Continue;

        Glow(player!);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;

        RemoveGlowForSlot(player.Slot);
        Globals.Wallhackers.Remove(player.Slot);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;

        if (!Globals.GlowData.TryGetValue(player!.Slot, out var glowData)) return HookResult.Continue;
        if (!glowData.GlowEnt.IsValid) return HookResult.Continue;

        glowData.GlowEnt.Glow.GlowRange = 0;
        glowData.GlowEnt.DispatchSpawn();

        return HookResult.Continue;
    }

    public static HookResult OnPlayerChangeTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;

        if (!Globals.GlowData.TryGetValue(player!.Slot, out var glowData)) return HookResult.Continue;
        if (!glowData.GlowEnt.IsValid) return HookResult.Continue;
        if (!glowData.ModelRelay.IsValid) return HookResult.Continue;

        Server.NextWorldUpdate(() => 
        {
            glowData.GlowEnt.SetModel(Util.GetPlayerModel(player));
            glowData.ModelRelay.SetModel(Util.GetPlayerModel(player));
        });

        return HookResult.Continue;
    }

    public static HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;

        if (!Globals.GlowData.TryGetValue(player!.Slot, out var glowData)) return HookResult.Continue;
        if (!glowData.GlowEnt.IsValid) return HookResult.Continue;

        UpdateGlowColor(player, glowData.GlowEnt);

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
        var health = 0;

        if (player.PlayerPawn?.Value != null && player.PlayerPawn.IsValid)
            health = player.PlayerPawn.Value.Health;

        var color = GetHealthColor(health);
        glowEntity.Glow.GlowColorOverride = color;
        glowEntity.Glow.GlowRange = health > 0 ? 5000 : 0;
        glowEntity.Glow.GlowRangeMin = 0;
        Utilities.SetStateChanged(glowEntity, "CGlowProperty", "m_glowColorOverride");
    }

    private static void Glow(CCSPlayerController player)
    {
        if (player.PlayerPawn?.Value == null || !player.PlayerPawn.IsValid) return;

        var health = player.PlayerPawn.Value.Health;

        var glowEntity = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        var modelRelay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (glowEntity == null || modelRelay == null) return;

        modelRelay.Spawnflags = 256;
        modelRelay.Render = Color.Transparent;
        modelRelay.RenderMode = RenderMode_t.kRenderNone;
        modelRelay.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(1u << 2);
        modelRelay.SetModel(Util.GetPlayerModel(player));

        glowEntity.Spawnflags = 256;
        glowEntity.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(1u << 2);
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

        RemoveGlowForSlot(player.Slot);
        Globals.GlowData[player.Slot] = new() {
            GlowEnt = glowEntity,
            ModelRelay = modelRelay
        };
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
        foreach (var entry in Globals.GlowData.Values.ToList())
        {
            Server.NextWorldUpdate(() => 
            {
                if (entry.GlowEnt.IsValid)
                    entry.GlowEnt.Remove();
                if (entry.ModelRelay.IsValid)
                    entry.ModelRelay.Remove();
            });
        }

        Globals.GlowData.Clear();
        Globals.Wallhackers.Clear();
    }
}
