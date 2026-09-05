using System.Drawing;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Funnies.Commands;

namespace Funnies.Modules;

public class Invisible
{
    private const float WeaponSkinSweepInterval = 0.2f;
    private static List<CEntityInstance> _entities = [];
    private static bool _wasSkinSuppressionEnabled;
    private static float _lastWeaponSkinSweepAt;
    private static bool _loggedWeaponSkinReflectionWarning;
    private static int _invisibleBombCarrierSlot = -1;

    public static void OnPlayerTransmit(CCheckTransmitInfo info, CCSPlayerController player)
    {
        // TODO: Should store these but dont know a good way :/
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();

        foreach (var entity in _entities)
        {
            if (entity.IsValid && !Globals.InvisiblePlayers.ContainsKey(player))
                info.TransmitEntities.Remove(entity);
        }

        if (gameRules?.GameRules == null || gameRules.GameRules.WarmupPeriod) return;

        var c4s = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4");

        if (c4s.Any())
        {
            var c4 = c4s.First();
            if (!gameRules.GameRules.BombPlanted &&
                _invisibleBombCarrierSlot >= 0 &&
                player.Slot != _invisibleBombCarrierSlot)
            {
                info.TransmitEntities.Remove(c4);
                return;
            }

            if (player.Team != CsTeam.Terrorist && !gameRules.GameRules.BombPlanted && !c4.IsPlantingViaUse && !gameRules.GameRules.BombDropped)
                info.TransmitEntities.Remove(c4);
            else
                info.TransmitEntities.Add(c4);
        }
    }

    public static void OnTick()
    {
        if (Globals.Config.DisableSkinsServerWide)
        {
            SuppressWeaponSkinsServerWide();
            _wasSkinSuppressionEnabled = true;
        }
        else if (_wasSkinSuppressionEnabled)
        {
            _wasSkinSuppressionEnabled = false;
            _lastWeaponSkinSweepAt = 0.0f;
            _loggedWeaponSkinReflectionWarning = false;
        }

        _invisibleBombCarrierSlot = GetInvisibleBombCarrierSlot();

        _entities.Clear();
        EntityFade.Begin();

        foreach (var invis in Globals.InvisiblePlayers)
        {
            if (!Util.IsPlayerValid(invis.Key)) continue;

            if (!invis.Key.PawnIsAlive) continue;
            var alpha = Funnies.Models.FadeTimeline.Alpha(Server.CurrentTime, invis.Value.StartTime, invis.Value.EndTime);

            var progress = (int)Util.Map(alpha, 0, 255, 0, 20);
            var pawn = invis.Key.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            if (alpha == 0)
            {
                pawn.EntitySpottedState.Spotted = false;
                pawn.EntitySpottedState.SpottedByMask.Clear();
                _entities.Add(pawn);
            }
            else
            {
                _entities.Remove(pawn);
            }

            invis.Key.PrintToCenterHtml(string.Concat(Enumerable.Repeat("&#9608;", progress)) + string.Concat(Enumerable.Repeat("&#9617;", 20 - progress)));

            EntityFade.Apply(pawn, alpha);

            foreach (var weaponHandle in pawn.WeaponServices!.MyWeapons)
            {
                var weapon = weaponHandle.Value;
                if (weapon == null || !weapon.IsValid) continue;

                EntityFade.Apply(weapon, alpha);

                if (alpha == 0)
                    AddInvisibleTransmitEntity(weapon);
                else
                    _entities.Remove(weapon);

                foreach (var attachedEntity in GetAttachedModelEntities(weapon))
                {
                    EntityFade.Apply(attachedEntity, alpha);

                    if (alpha == 0)
                        AddInvisibleTransmitEntity(attachedEntity);
                    else
                        _entities.Remove(attachedEntity);
                }
            }

            foreach (var attachedEntity in GetAttachedModelEntities(pawn))
            {
                EntityFade.Apply(attachedEntity, alpha);

                if (alpha == 0)
                    AddInvisibleTransmitEntity(attachedEntity);
                else
                    _entities.Remove(attachedEntity);
            }
        }
        EntityFade.End();
    }

    public static HookResult OnPlayerSound(EventPlayerSound @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, @event.Duration * 2);

        return HookResult.Continue;
    }

    public static HookResult OnPlayerShoot(EventBulletImpact @event, GameEventInfo info)
    {
        SetPlayerInvisibleFor(@event.Userid, 0.5f);

        return HookResult.Continue;
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
        return HookResult.Continue;
    }

    private static IEnumerable<CBaseModelEntity> GetAttachedModelEntities(CBaseEntity rootEntity)
    {
        var rootNode = rootEntity.CBodyComponent?.SceneNode;
        if (rootNode == null) yield break;

        foreach (var childNode in Util.GetChildrenRecursive(rootNode))
        {
            var identity = childNode.Owner?.Entity;
            if (identity == null) continue;

            var entityInstance = identity.EntityInstance;
            if (!entityInstance.IsValid) continue;
            if (entityInstance.Handle == rootEntity.Handle) continue;
            if (Globals.GlowData.Values.Any(g => g.GlowEnt.Handle == entityInstance.Handle || g.ModelRelay.Handle == entityInstance.Handle)) continue;

            var entity = entityInstance.As<CBaseModelEntity>();
            if (entity == null || !entity.IsValid) continue;

            yield return entity;
        }
    }

    private static void AddInvisibleTransmitEntity(CEntityInstance entity)
    {
        if (!_entities.Contains(entity))
            _entities.Add(entity);
    }

    private static IEnumerable<CBaseEntity> GetWeaponEntities(CCSPlayerPawn pawn)
    {
        foreach (var weaponHandle in pawn.WeaponServices!.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon == null || !weapon.IsValid) continue;

            yield return weapon;
        }
    }

    private static void SuppressWeaponSkinsServerWide()
    {
        if (Server.CurrentTime - _lastWeaponSkinSweepAt < WeaponSkinSweepInterval) return;
        _lastWeaponSkinSweepAt = Server.CurrentTime;

        var totalWeapons = 0;
        var updatedWeapons = 0;
        foreach (var player in Util.GetValidPlayers())
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            foreach (var weapon in GetWeaponEntities(pawn))
            {
                totalWeapons++;
                if (SuppressWeaponSkin(weapon))
                    updatedWeapons++;
            }
        }

        if (updatedWeapons > 0)
        {
            _loggedWeaponSkinReflectionWarning = false;
            return;
        }

        if (totalWeapons > 0 && !_loggedWeaponSkinReflectionWarning)
        {
            _loggedWeaponSkinReflectionWarning = true;
            Console.WriteLine("[Funnies] Weapon skin suppression found no writable fallback fields on weapon entities. API snapshot may not expose paintkit fields.");
        }
    }

    public static HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player) || player!.PawnIsAlive != true) return HookResult.Continue;

        if (Globals.Config.DisableSkinsServerWide)
        {
            Server.NextFrame(() => SetDefaultWeaponSkins(player));
        }

        return HookResult.Continue;
    }

    public static HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;

        if (Globals.Config.DisableSkinsServerWide)
        {
            Server.NextFrame(() => SetDefaultWeaponSkins(player!));
        }

        return HookResult.Continue;
    }

    private static void SetDefaultWeaponSkins(CCSPlayerController player)
    {
        if (!Util.IsPlayerValid(player) || player.PawnIsAlive != true) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null) return;

        // Process all weapons the player is carrying
        foreach (var weaponHandle in weaponServices.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon == null || !weapon.IsValid) continue;

            SuppressWeaponSkinDirect(weapon);
        }
    }

    private static void SuppressWeaponSkinDirect(CBasePlayerWeapon weapon)
    {
        // Use ItemServices API to fully disable weapon skins
        // Set PaintKit to 0 (default), remove StatTrak, reset wear to minimal
        var itemServices = TryGetItemServices(weapon);
        if (itemServices != null)
        {
            // Use reflection to call SetPaintKit, SetStatTrak, SetWear methods
            // These are server-side methods that force default skin appearance
            TryCallItemServicesMethod(itemServices, "SetPaintKit", 0);
            TryCallItemServicesMethod(itemServices, "SetStatTrak", 0);
            TryCallItemServicesMethod(itemServices, "SetWear", 0.001f);
            TryCallItemServicesMethod(itemServices, "SetSeed", 0);
            return;
        }

        // Fallback to reflection-based property setting if ItemServices unavailable
        SuppressWeaponSkinViaFallback(weapon);
    }

    private static object? TryGetItemServices(CBasePlayerWeapon weapon)
    {
        try
        {
            var type = weapon.GetType();
            var property = type.GetProperty("ItemServices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(weapon);
        }
        catch
        {
            return null;
        }
    }

    private static void TryCallItemServicesMethod(object itemServices, string methodName, object value)
    {
        try
        {
            var type = itemServices.GetType();
            var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) return;

            var parameters = method.GetParameters();
            if (parameters.Length != 1) return;

            var converted = ConvertForPropertyType(value, parameters[0].ParameterType);
            if (converted == null && parameters[0].ParameterType.IsValueType) return;

            method.Invoke(itemServices, [converted]);
        }
        catch
        {
            // Method not available in this API version
        }
    }

    private static void SuppressWeaponSkinViaFallback(CBaseEntity weapon)
    {
        // Fallback: Try common skin fields via reflection for API compatibility
        var changed = false;

        // Try CEconEntity/CBasePlayerWeapon fallback properties
        changed |= TrySetPathValue(weapon, "FallbackPaintKit", 0);
        changed |= TrySetPathValue(weapon, "FallbackSeed", 0);
        changed |= TrySetPathValue(weapon, "FallbackWear", 0.001f);
        changed |= TrySetPathValue(weapon, "FallbackStatTrak", 0);

        // Try AttributeManager.Item paths for wrapped econ items
        changed |= TrySetPathValue(weapon, "AttributeManager.Item.FallbackPaintKit", 0);
        changed |= TrySetPathValue(weapon, "AttributeManager.Item.FallbackSeed", 0);
        changed |= TrySetPathValue(weapon, "AttributeManager.Item.FallbackWear", 0.001f);
        changed |= TrySetPathValue(weapon, "AttributeManager.Item.FallbackStatTrak", 0);

        // Try direct Item property
        changed |= TrySetPathValue(weapon, "Item.FallbackPaintKit", 0);
        changed |= TrySetPathValue(weapon, "Item.FallbackSeed", 0);
        changed |= TrySetPathValue(weapon, "Item.FallbackWear", 0.001f);
        changed |= TrySetPathValue(weapon, "Item.FallbackStatTrak", 0);

        if (!changed) return;

        // Notify network changes
        TrySetStateChanged(weapon, "CBasePlayerWeapon", "m_nFallbackPaintKit");
        TrySetStateChanged(weapon, "CBasePlayerWeapon", "m_nFallbackSeed");
        TrySetStateChanged(weapon, "CBasePlayerWeapon", "m_flFallbackWear");
        TrySetStateChanged(weapon, "CBasePlayerWeapon", "m_nFallbackStatTrak");
        TrySetStateChanged(weapon, "CEconEntity", "m_nFallbackPaintKit");
        TrySetStateChanged(weapon, "CEconEntity", "m_nFallbackSeed");
        TrySetStateChanged(weapon, "CEconEntity", "m_flFallbackWear");
        TrySetStateChanged(weapon, "CEconEntity", "m_nFallbackStatTrak");
    }

    private static bool SuppressWeaponSkin(CBaseEntity weapon)
    {
        // Fully disable weapon skins by setting PaintKit to 0 (default),
        // removing StatTrak, and resetting wear to minimal (0.001).
        var weaponEntity = weapon.As<CBasePlayerWeapon>();
        if (weaponEntity != null && weaponEntity.IsValid)
        {
            SuppressWeaponSkinDirect(weaponEntity);
            return true;
        }

        // Fallback to reflection-based approach
        SuppressWeaponSkinViaFallback(weapon);
        return true;
    }

    private static bool TrySetPathValue(object root, string path, object value)
    {
        var current = root;
        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var type = current.GetType();
            var property = type.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (property == null) return false;

            if (i == segments.Length - 1)
            {
                if (!property.CanWrite) return false;
                var converted = ConvertForPropertyType(value, property.PropertyType);
                if (converted == null && property.PropertyType.IsValueType) return false;

                property.SetValue(current, converted);
                return true;
            }

            var next = property.GetValue(current);
            if (next == null) return false;
            current = next;
        }

        return false;
    }

    private static object? ConvertForPropertyType(object value, Type propertyType)
    {
        var target = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        try
        {
            if (target.IsEnum)
                return Enum.ToObject(target, value);

            if (target == typeof(float))
                return Convert.ToSingle(value);
            if (target == typeof(double))
                return Convert.ToDouble(value);
            if (target == typeof(int))
                return Convert.ToInt32(value);
            if (target == typeof(uint))
                return Convert.ToUInt32(value);
            if (target == typeof(short))
                return Convert.ToInt16(value);
            if (target == typeof(ushort))
                return Convert.ToUInt16(value);
            if (target == typeof(byte))
                return Convert.ToByte(value);
            if (target == typeof(sbyte))
                return Convert.ToSByte(value);
            if (target == typeof(long))
                return Convert.ToInt64(value);
            if (target == typeof(ulong))
                return Convert.ToUInt64(value);
            if (target == typeof(bool))
                return Convert.ToBoolean(value);

            return Convert.ChangeType(value, target);
        }
        catch
        {
            return null;
        }
    }

    private static void TrySetStateChanged(CBaseEntity entity, string table, string field)
    {
        try
        {
            Utilities.SetStateChanged(entity, table, field);
        }
        catch
        {
            // Property table/field availability differs across API snapshots.
        }
    }

    private static int GetInvisibleBombCarrierSlot()
    {
        foreach (var invisibleEntry in Globals.InvisiblePlayers)
        {
            var invisiblePlayer = invisibleEntry.Key;
            if (!Util.IsPlayerValid(invisiblePlayer)) continue;
            if (invisiblePlayer.Team != CsTeam.Terrorist) continue;

            var pawn = invisiblePlayer.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            foreach (var weapon in GetWeaponEntities(pawn))
            {
                var weaponName = weapon.DesignerName.ToLowerInvariant().Replace("weapon_", "");
                if (weaponName.Contains("c4"))
                    return invisiblePlayer.Slot;
            }
        }

        return -1;
    }

    private static void SetPlayerInvisibleFor(CCSPlayerController? player, float time)
    {
        if (!Util.IsPlayerValid(player)) return;
        if (!Globals.InvisiblePlayers.TryGetValue(player, out var data)) return;

        data.StartTime = Server.CurrentTime;
        data.EndTime = Server.CurrentTime + time;

        Globals.InvisiblePlayers[player] = data;
    }

    public static void Setup()
    {
        Globals.Plugin.RegisterEventHandler<EventPlayerDisconnect>((e, i) => {
            if (e.Userid != null) Globals.InvisiblePlayers.Remove(e.Userid);
            return HookResult.Continue;
        });
        Globals.Plugin.RegisterListener<Listeners.OnMapStart>(_ => {
            _entities.Clear();
            EntityFade.Clear();
            Globals.InvisiblePlayers.Clear();
        });
        Globals.Plugin.RegisterEventHandler<EventBombBeginplant>(OnPlayerStartPlant);
        // EventPlayerShoot doesnt work so we use EventBulletImpact
        Globals.Plugin.RegisterEventHandler<EventBulletImpact>(OnPlayerShoot);
        Globals.Plugin.RegisterEventHandler<EventPlayerSound>(OnPlayerSound);
        Globals.Plugin.RegisterEventHandler<EventBombBegindefuse>(OnPlayerStartDefuse);
        Globals.Plugin.RegisterEventHandler<EventWeaponReload>(OnPlayerReload);
        Globals.Plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);

        // Weapon skin suppression events
        Globals.Plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        Globals.Plugin.RegisterEventHandler<EventItemPickup>(OnItemPickup);

        Globals.Plugin.AddCommand("css_invisible", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
        Globals.Plugin.AddCommand("css_invis", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
    }

    public static void Cleanup()
    {
        EntityFade.Restore();
        _entities.Clear();
        _wasSkinSuppressionEnabled = false;
        _lastWeaponSkinSweepAt = 0;
        _invisibleBombCarrierSlot = -1;
        Globals.InvisiblePlayers.Clear();
    }
}
