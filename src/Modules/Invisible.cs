using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Funnies.Commands;

namespace Funnies.Modules;

public class Invisible
{

    private static List<CEntityInstance> _entities = [];
    private static Dictionary<CCSPlayerController, int> _pendingShots = new();
    private static Dictionary<CCSPlayerController, int> _hitCredits = new();
    private static Dictionary<CCSPlayerController, string> _thrownGrenades = new();

    public static void OnPlayerTransmit(CCheckTransmitInfo info, CCSPlayerController player)
    {
        // TODO: Should store these but dont know a good way :/
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First();

        foreach (var entity in _entities)
        {
            if (!Globals.InvisiblePlayers.ContainsKey(player))
                info.TransmitEntities.Remove(entity);
        }

        if (gameRules.GameRules!.WarmupPeriod) return;

        var c4s = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4");

        if (c4s.Any())
        {
            var c4 = c4s.First();
            if (player!.Team != CsTeam.Terrorist && !gameRules.GameRules!.BombPlanted && !c4.IsPlantingViaUse  && !gameRules.GameRules!.BombDropped)
                info.TransmitEntities.Remove(c4);
            else
                info.TransmitEntities.Add(c4);
        }
    }

    public static void OnTick()
    {
        _entities.Clear();
        
        foreach (var invis in Globals.InvisiblePlayers)
        {
            if (!Util.IsPlayerValid(invis.Key)) continue;
            
            var alpha = 255f;

            var half = Server.CurrentTime + ((invis.Value.StartTime - Server.CurrentTime) / 2);
            if (half < Server.CurrentTime)
                alpha = invis.Value.EndTime < Server.CurrentTime ? 0 : Util.Map(Server.CurrentTime, half, invis.Value.EndTime, 255, 0);

            var progress = (int)Util.Map(alpha, 0, 255, 0, 20);
            var pawn = invis.Key.PlayerPawn.Value;

            if (alpha == 0)
            {
                pawn!.EntitySpottedState.Spotted = false;
                pawn!.EntitySpottedState.SpottedByMask[0] = 0;
                _entities.Add(pawn);
            }
            else
                _entities.Remove(pawn);

            invis.Key.PrintToCenterHtml(string.Concat(Enumerable.Repeat("&#9608;", progress)) + string.Concat(Enumerable.Repeat("&#9617;", 20 - progress)));

            pawn!.Render = Color.FromArgb((int)alpha, pawn.Render);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

            pawn.ShadowStrength = alpha < 128f ? 1.0f : 0.0f;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_flShadowStrength");

            foreach (var weapon in pawn.WeaponServices!.MyWeapons)
            {
                weapon.Value!.ShadowStrength = alpha < 128f ? 1.0f : 0.0f;
                Utilities.SetStateChanged(weapon.Value!, "CBaseModelEntity", "m_flShadowStrength");

                if (alpha < 128f)
                {
                    weapon.Value!.Render = Color.FromArgb((int)alpha, pawn.Render);
                    Utilities.SetStateChanged(weapon.Value!, "CBaseModelEntity", "m_clrRender");
                    _entities.Add(weapon.Value!);
                }
            }
        }
    }

    public static HookResult OnPlayerSound(EventPlayerSound @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, @event.Duration * 2);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerShoot(EventBulletImpact @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 0.5f);
        HandleMissedShotDamage(@event);

        return HookResult.Continue;
    }

    private static void HandleMissedShotDamage(EventBulletImpact @event)
    {
        var shooter = @event.Userid;
        if (!Util.IsPlayerValid(shooter)) return;
        
        // Only apply damage if there's at least one invisible player
        if (Globals.InvisiblePlayers.Count == 0) return;
        
        // Don't damage invisible players
        if (Globals.InvisiblePlayers.ContainsKey(shooter)) return;
        
        // Increment pending shot counter
        if (!_pendingShots.ContainsKey(shooter))
            _pendingShots[shooter] = 0;
        _pendingShots[shooter]++;
        
        // Schedule damage check after 3 ticks (3/64 seconds) to let OnPlayerHurt fire if it's a hit
        Globals.Plugin.AddTimer(0.046875f, () =>
        {
            if (!Util.IsPlayerValid(shooter)) return;
            if (!_pendingShots.ContainsKey(shooter)) return;
            
            // Decrement pending shot counter
            _pendingShots[shooter]--;
            
            // Check if we have hit credits available
            var hasCredit = _hitCredits.TryGetValue(shooter, out var credits) && credits > 0;
            
            if (hasCredit)
            {
                // This was a hit - consume one credit
                _hitCredits[shooter]--;
                if (_hitCredits[shooter] <= 0)
                    _hitCredits.Remove(shooter);
            }
            else
            {
                // This is a miss - apply damage
                var shooterPawn = shooter.PlayerPawn.Value;
                if (shooterPawn == null || !shooterPawn.IsValid) return;
                
                var activeWeapon = shooterPawn.WeaponServices?.ActiveWeapon?.Value;
                if (activeWeapon == null) return;
                
                var weaponName = activeWeapon.DesignerName.ToLower();
                var damage = GetMissedShotDamage(weaponName);
                
                if (damage > 0)
                {
                    var currentHealth = shooterPawn.Health;
                    var newHealth = currentHealth - damage;
                    
                    if (newHealth <= 0)
                    {
                        // Kill the player
                        shooterPawn.CommitSuicide(false, true);
                    }
                    else
                    {
                        shooterPawn.Health = newHealth;
                        Utilities.SetStateChanged(shooterPawn, "CBaseEntity", "m_iHealth");
                    }
                }
            }
            
            // Clean up if no more pending shots
            if (_pendingShots[shooter] <= 0)
                _pendingShots.Remove(shooter);
        });
    }

    private static int GetMissedShotDamage(string weaponName)
    {
        // Pistols - 2 HP
        if (weaponName.Contains("pistol") || weaponName.Contains("deagle") || 
            weaponName.Contains("elite") || weaponName.Contains("fiveseven") || 
            weaponName.Contains("glock") || weaponName.Contains("hkp2000") || 
            weaponName.Contains("p250") || weaponName.Contains("tec9") || 
            weaponName.Contains("cz75") || weaponName.Contains("revolver"))
        {
            return 2;
        }
        
        // Snipers - 5 HP
        if (weaponName.Contains("awp") || weaponName.Contains("ssg08") || 
            weaponName.Contains("scar20") || weaponName.Contains("g3sg1"))
        {
            return 5;
        }
        
        // Shotguns - 5 HP
        if (weaponName.Contains("nova") || weaponName.Contains("xm1014") || 
            weaponName.Contains("mag7") || weaponName.Contains("sawedoff"))
        {
            return 5;
        }
        
        // Rifles and SMGs - 3 HP
        if (weaponName.Contains("ak47") || weaponName.Contains("m4a1") || 
            weaponName.Contains("m4a4") || weaponName.Contains("famas") || 
            weaponName.Contains("galilar") || weaponName.Contains("aug") || 
            weaponName.Contains("sg556") || weaponName.Contains("mp5") || 
            weaponName.Contains("mp7") || weaponName.Contains("mp9") || 
            weaponName.Contains("mac10") || weaponName.Contains("p90") || 
            weaponName.Contains("bizon") || weaponName.Contains("ump45") || 
            weaponName.Contains("negev") || weaponName.Contains("m249"))
        {
            return 3;
        }
        
        return 0;
    }

    public static HookResult OnPlayerStartPlant(EventBombBeginplant @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 1f);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerStartDefuse(EventBombBegindefuse @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 1f);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerReload(EventWeaponReload @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 1.5f);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 0.5f);
        
        // Track successful hits for missed shot detection
        // Only track if attacker hit someone OTHER than themselves
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        if (Util.IsPlayerValid(attacker) && attacker != victim)
        {
            // Add a hit credit for this attacker
            if (!_hitCredits.ContainsKey(attacker))
                _hitCredits[attacker] = 0;
            _hitCredits[attacker]++;
            
            // If this was from a tracked grenade, clear the grenade tracking
            if (_thrownGrenades.ContainsKey(attacker))
            {
                _thrownGrenades.Remove(attacker);
            }
        }

        return HookResult.Continue;
    }

    public static HookResult OnGrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;
        
        // Only track HE grenades and molotovs/incendiaries
        var weaponName = @event.Weapon.ToLower();
        if (!weaponName.Contains("hegrenade") && !weaponName.Contains("molotov") && !weaponName.Contains("incgrenade"))
            return HookResult.Continue;
        
        // Only apply damage if there's at least one invisible player
        if (Globals.InvisiblePlayers.Count == 0) return HookResult.Continue;
        
        // Don't damage invisible players
        if (Globals.InvisiblePlayers.ContainsKey(player)) return HookResult.Continue;
        
        // Track this grenade throw
        _thrownGrenades[player] = weaponName;
        
        // Wait longer for grenades (5 seconds) to see if they damage anyone
        Globals.Plugin.AddTimer(5.0f, () =>
        {
            if (!Util.IsPlayerValid(player)) return;
            
            // Remove from tracking - if it was removed earlier by OnPlayerHurt, that means it hit
            var wasHit = !_thrownGrenades.ContainsKey(player);
            if (!wasHit)
                _thrownGrenades.Remove(player);
            
            if (!wasHit)
            {
                // Grenade missed - apply 20 HP damage
                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) return;
                
                var currentHealth = pawn.Health;
                var newHealth = currentHealth - 20;
                
                if (newHealth <= 0)
                {
                    pawn.CommitSuicide(false, true);
                }
                else
                {
                    pawn.Health = newHealth;
                    Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
                }
            }
        });
        
        return HookResult.Continue;
    }

    public static HookResult OnPlayerSwingKnife(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;
        
        // Check if it's a knife swing first
        var weaponName = @event.Weapon.ToLower();
        if (!weaponName.Contains("knife") && !weaponName.Contains("bayonet"))
            return HookResult.Continue;
        
        // Only apply damage if there's at least one invisible player
        if (Globals.InvisiblePlayers.Count == 0) return HookResult.Continue;
        
        // Don't damage invisible players
        if (Globals.InvisiblePlayers.ContainsKey(player)) return HookResult.Continue;
        
        // Track this knife swing
        if (!_pendingShots.ContainsKey(player))
            _pendingShots[player] = 0;
        _pendingShots[player]++;
        
        // Schedule damage check after 3 ticks (3/64 seconds)
        Globals.Plugin.AddTimer(0.046875f, () =>
        {
            if (!Util.IsPlayerValid(player)) return;
            if (!_pendingShots.ContainsKey(player)) return;
            
            _pendingShots[player]--;
            
            // Check if we have hit credits available
            var hasCredit = _hitCredits.TryGetValue(player, out var credits) && credits > 0;
            
            if (hasCredit)
            {
                // This was a hit - consume one credit
                _hitCredits[player]--;
                if (_hitCredits[player] <= 0)
                    _hitCredits.Remove(player);
            }
            else
            {
                // Knife swing missed - apply 2 HP damage
                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) return;
                
                var currentHealth = pawn.Health;
                var newHealth = currentHealth - 2;
                
                if (newHealth <= 0)
                {
                    pawn.CommitSuicide(false, true);
                }
                else
                {
                    pawn.Health = newHealth;
                    Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
                }
            }
            
            // Clean up if no more pending shots
            if (_pendingShots[player] <= 0)
                _pendingShots.Remove(player);
        });
        
        return HookResult.Continue;
    }

    private static void SetPlayerInvisibleFor(CCSPlayerController player, float time)
    {
        if (!Util.IsPlayerValid(player)) return;
        if (!Globals.InvisiblePlayers.TryGetValue(player!, out var data)) return;

        data.StartTime = Server.CurrentTime;
        data.EndTime = Server.CurrentTime + time;

        Globals.InvisiblePlayers[player!] = data;
    }

    public static void Setup()
    {
        Globals.Plugin.RegisterEventHandler<EventBombBeginplant>(OnPlayerStartPlant);
        // EventPlayerShoot doesnt work so we use EventBulletImpact
        Globals.Plugin.RegisterEventHandler<EventBulletImpact>(OnPlayerShoot);
        Globals.Plugin.RegisterEventHandler<EventPlayerSound>(OnPlayerSound);
        Globals.Plugin.RegisterEventHandler<EventBombBegindefuse>(OnPlayerStartDefuse);
        Globals.Plugin.RegisterEventHandler<EventWeaponReload>(OnPlayerReload);
        Globals.Plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        Globals.Plugin.RegisterEventHandler<EventGrenadeThrown>(OnGrenadeThrown);
        Globals.Plugin.RegisterEventHandler<EventWeaponFire>(OnPlayerSwingKnife);

        Globals.Plugin.AddCommand("css_invisible", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
        Globals.Plugin.AddCommand("css_invis", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
    }

    public static void Cleanup()
    {
        _entities.Clear();
        _pendingShots.Clear();
        _hitCredits.Clear();
        _thrownGrenades.Clear();

        foreach (var player in Util.GetValidPlayers())
        {
            var pawn = player.PlayerPawn.Value;

            pawn!.Render = Color.FromArgb(255, pawn.Render);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
            pawn!.ShadowStrength = 1.0f;
            Utilities.SetStateChanged(pawn!, "CBaseModelEntity", "m_flShadowStrength");

            foreach (var weapon in pawn.WeaponServices!.MyWeapons)
            {
                weapon.Value!.ShadowStrength = 1.0f;
                Utilities.SetStateChanged(weapon.Value!, "CBaseModelEntity", "m_flShadowStrength");
            }
        }

        Globals.InvisiblePlayers.Clear();
    }
}
