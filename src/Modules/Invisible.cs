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
    private const float MissResolveDelay = 0.046875f;
    private const float GrenadeResolveDelay = 5.0f;
    private const float KnifePrimaryResolveDelay = 0.12f;
    private const float KnifeSecondaryResolveDelay = 0.8f;
    private const float KnifeFireDedupeWindow = 0.08f;
    private const float KnifeAttack2DedupeWindow = 0.2f;
    private const float KnifeAttack2MergeWindow = 0.7f;
    private const float KnifeHitResolveWindow = 1.6f;
    private const float WeaponSkinSweepInterval = 0.2f;

    private enum AttemptKind
    {
        Shot,
        Knife,
        Grenade
    }

    private sealed class PendingMissAttempt
    {
        public int Id { get; init; }
        public int Damage { get; init; }
        public float CreatedAt { get; init; }
        public AttemptKind Kind { get; init; }
        public bool Resolved { get; set; }
    }

    private static List<CEntityInstance> _entities = [];
    private static int _nextAttemptId = 1;
    private static Dictionary<int, List<PendingMissAttempt>> _pendingShots = [];
    private static Dictionary<int, List<PendingMissAttempt>> _pendingGrenades = [];
    private static Dictionary<int, float> _lastKnifeFireAttemptBySlot = [];
    private static Dictionary<int, float> _lastKnifeAttack2AttemptBySlot = [];
    private static Dictionary<int, float> _lastKnifeHitBySlot = [];
    private static bool _wasSkinSuppressionEnabled;
    private static float _lastWeaponSkinSweepAt;
    private static bool _loggedWeaponSkinReflectionWarning;
    private static int _invisibleBombCarrierSlot = -1;
    private static bool _restoredGlobalCosmetics;

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
        if (!_restoredGlobalCosmetics)
        {
            RestoreAllCosmeticsVisibility();
            _restoredGlobalCosmetics = true;
        }

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

        foreach (var invis in Globals.InvisiblePlayers)
        {
            if (!Util.IsPlayerValid(invis.Key)) continue;

            var alpha = 255f;

            var half = Server.CurrentTime + ((invis.Value.StartTime - Server.CurrentTime) / 2);
            if (half < Server.CurrentTime)
                alpha = invis.Value.EndTime < Server.CurrentTime ? 0 : Util.Map(Server.CurrentTime, half, invis.Value.EndTime, 255, 0);

            var progress = (int)Util.Map(alpha, 0, 255, 0, 20);
            var pawn = invis.Key.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            if (alpha == 0)
            {
                pawn.EntitySpottedState.Spotted = false;
                pawn.EntitySpottedState.SpottedByMask[0] = 0;
                _entities.Add(pawn);
            }
            else
            {
                _entities.Remove(pawn);
            }

            invis.Key.PrintToCenterHtml(string.Concat(Enumerable.Repeat("&#9608;", progress)) + string.Concat(Enumerable.Repeat("&#9617;", 20 - progress)));

            pawn.Render = Color.FromArgb((int)alpha, pawn.Render);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

            pawn.ShadowStrength = alpha < 128f ? 1.0f : 0.0f;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_flShadowStrength");

            foreach (var weaponHandle in pawn.WeaponServices!.MyWeapons)
            {
                var weapon = weaponHandle.Value;
                if (weapon == null || !weapon.IsValid) continue;

                weapon.ShadowStrength = alpha < 128f ? 1.0f : 0.0f;
                Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_flShadowStrength");

                weapon.Render = Color.FromArgb((int)alpha, weapon.Render);
                Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");

                if (alpha == 0)
                    AddInvisibleTransmitEntity(weapon);
                else
                    _entities.Remove(weapon);

                foreach (var attachedEntity in GetAttachedModelEntities(weapon))
                {
                    SetAttachedShadowStrength(attachedEntity, alpha < 128f ? 1.0f : 0.0f);
                    SetAttachedRenderAlpha(attachedEntity, (int)alpha);

                    if (alpha == 0)
                        AddInvisibleTransmitEntity(attachedEntity);
                    else
                        _entities.Remove(attachedEntity);
                }
            }

            foreach (var attachedEntity in GetAttachedModelEntities(pawn))
            {
                SetAttachedShadowStrength(attachedEntity, alpha < 128f ? 1.0f : 0.0f);
                SetAttachedRenderAlpha(attachedEntity, (int)alpha);

                if (alpha == 0)
                    AddInvisibleTransmitEntity(attachedEntity);
                else
                    _entities.Remove(attachedEntity);
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

        return HookResult.Continue;
    }

    public static HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!ShouldTrackMissDamage(player)) return HookResult.Continue;

        var weaponName = NormalizeWeaponName(@event.Weapon ?? string.Empty);
        if (IsGrenadeWeapon(weaponName)) return HookResult.Continue;
        var isKnife = IsKnifeWeapon(weaponName);
        if (isKnife)
        {
            var slot = player!.Slot;
            if (IsRecentKnifeAttack2(slot)) return HookResult.Continue;
            if (!TryTrackKnifeFire(slot)) return HookResult.Continue;
        }

        var damage = GetMissedShotDamage(weaponName);
        if (damage <= 0) return HookResult.Continue;

        var resolveDelay = isKnife ? KnifePrimaryResolveDelay : MissResolveDelay;
        QueueMissAttempt(_pendingShots, player!, damage, resolveDelay, isKnife ? AttemptKind.Knife : AttemptKind.Shot);

        return HookResult.Continue;
    }

    public static void OnPlayerButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if ((pressed & PlayerButtons.Attack2) == 0) return;
        if (!ShouldTrackMissDamage(player)) return;

        var activeWeapon = player.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value;
        if (activeWeapon == null || !activeWeapon.IsValid) return;

        var weaponName = NormalizeWeaponName(activeWeapon.DesignerName);
        if (!IsKnifeWeapon(weaponName)) return;
        if (!TryTrackKnifeAttack2(player.Slot)) return;

        QueueMissAttempt(_pendingShots, player, GetMissedShotDamage(weaponName), KnifeSecondaryResolveDelay, AttemptKind.Knife);
    }

    public static HookResult OnGrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!ShouldTrackMissDamage(player)) return HookResult.Continue;

        var weaponName = NormalizeWeaponName(@event.Weapon ?? string.Empty);
        if (!IsGrenadeWeapon(weaponName)) return HookResult.Continue;

        QueueMissAttempt(_pendingGrenades, player!, 5, GrenadeResolveDelay, AttemptKind.Grenade);

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

        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (!Util.IsPlayerValid(attacker) || !Util.IsPlayerValid(victim)) return HookResult.Continue;
        if (attacker == victim) return HookResult.Continue;
        if (!Globals.InvisiblePlayers.ContainsKey(victim!)) return HookResult.Continue;
        if (Globals.InvisiblePlayers.ContainsKey(attacker!)) return HookResult.Continue;

        var weaponName = NormalizeWeaponName(@event.Weapon ?? string.Empty);
        if (IsKnifeWeapon(weaponName))
        {
            _lastKnifeHitBySlot[attacker.Slot] = Server.CurrentTime;
            ResolvePendingKnifeAttempts(attacker.Slot);
            return HookResult.Continue;
        }

        if (IsGrenadeWeapon(weaponName))
        {
            if (!ResolveOldestAttempt(_pendingGrenades, attacker!))
                ResolveOldestAttempt(_pendingShots, attacker);
        }
        else
        {
            if (!ResolveOldestAttempt(_pendingShots, attacker!))
                ResolveOldestAttempt(_pendingGrenades, attacker);
        }

        return HookResult.Continue;
    }

    private static void ResolvePendingKnifeAttempts(int slot)
    {
        if (!_pendingShots.TryGetValue(slot, out var attempts)) return;

        var now = Server.CurrentTime;
        foreach (var attempt in attempts)
        {
            if (attempt.Resolved || attempt.Kind != AttemptKind.Knife) continue;
            if (now - attempt.CreatedAt > KnifeHitResolveWindow) continue;

            attempt.Resolved = true;
        }
    }

    private static void QueueMissAttempt(Dictionary<int, List<PendingMissAttempt>> pendingBySlot, CCSPlayerController player, int damage, float delay, AttemptKind kind)
    {
        if (damage <= 0) return;

        var slot = player.Slot;
        if (!pendingBySlot.TryGetValue(slot, out var attempts))
        {
            attempts = [];
            pendingBySlot[slot] = attempts;
        }

        var attempt = new PendingMissAttempt
        {
            Id = _nextAttemptId++,
            Damage = damage,
            Kind = kind,
            CreatedAt = Server.CurrentTime
        };

        attempts.Add(attempt);

        Globals.Plugin.AddTimer(delay, () =>
        {
            if (!pendingBySlot.TryGetValue(slot, out var activeAttempts)) return;

            var currentAttempt = activeAttempts.FirstOrDefault(item => item.Id == attempt.Id);
            if (currentAttempt == null) return;

            if (currentAttempt.Kind == AttemptKind.Knife &&
                _lastKnifeHitBySlot.TryGetValue(slot, out var lastKnifeHitAt) &&
                lastKnifeHitAt >= currentAttempt.CreatedAt)
            {
                currentAttempt.Resolved = true;
            }

            if (!currentAttempt.Resolved)
                ApplyMissPenalty(slot, currentAttempt.Damage);

            activeAttempts.Remove(currentAttempt);
            if (activeAttempts.Count == 0)
                pendingBySlot.Remove(slot);
        });
    }

    private static bool ResolveOldestAttempt(Dictionary<int, List<PendingMissAttempt>> pendingBySlot, CCSPlayerController player)
    {
        if (!pendingBySlot.TryGetValue(player.Slot, out var attempts)) return false;

        var pending = attempts.FirstOrDefault(item => !item.Resolved);
        if (pending == null) return false;

        pending.Resolved = true;
        return true;
    }

    private static bool TryTrackKnifeFire(int slot)
    {
        var now = Server.CurrentTime;
        if (_lastKnifeFireAttemptBySlot.TryGetValue(slot, out var lastAttemptAt) &&
            now - lastAttemptAt < KnifeFireDedupeWindow)
        {
            return false;
        }

        _lastKnifeFireAttemptBySlot[slot] = now;
        return true;
    }

    private static bool TryTrackKnifeAttack2(int slot)
    {
        var now = Server.CurrentTime;
        if (_lastKnifeAttack2AttemptBySlot.TryGetValue(slot, out var lastAttemptAt) &&
            now - lastAttemptAt < KnifeAttack2DedupeWindow)
        {
            return false;
        }

        _lastKnifeAttack2AttemptBySlot[slot] = now;
        return true;
    }

    private static bool IsRecentKnifeAttack2(int slot)
    {
        if (!_lastKnifeAttack2AttemptBySlot.TryGetValue(slot, out var attack2At))
            return false;

        return Server.CurrentTime - attack2At < KnifeAttack2MergeWindow;
    }

    private static void ApplyMissPenalty(int slot, int damage)
    {
        if (damage <= 0) return;

        var player = GetPlayerBySlot(slot);
        if (!Util.IsPlayerValid(player)) return;
        if (Globals.InvisiblePlayers.ContainsKey(player!)) return;

        var pawn = player.PlayerPawn!.Value;
        if (pawn == null || !pawn.IsValid) return;

        var newHealth = pawn.Health - damage;
        if (newHealth <= 0)
        {
            pawn.CommitSuicide(false, true);
            return;
        }

        pawn.Health = newHealth;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
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

            var entity = entityInstance.As<CBaseModelEntity>();
            if (entity == null || !entity.IsValid) continue;

            yield return entity;
        }
    }

    private static void SetAttachedRenderAlpha(CBaseModelEntity entity, int alpha)
    {
        var clampedAlpha = Math.Clamp(alpha, 0, 255);
        entity.Render = Color.FromArgb(clampedAlpha, entity.Render);
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
    }

    private static void SetAttachedShadowStrength(CBaseModelEntity entity, float shadowStrength)
    {
        entity.ShadowStrength = shadowStrength;
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_flShadowStrength");
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
                var weaponName = NormalizeWeaponName(weapon.DesignerName);
                if (weaponName.Contains("c4"))
                    return invisiblePlayer.Slot;
            }
        }

        return -1;
    }

    private static void RestoreAllCosmeticsVisibility()
    {
        foreach (var player in Util.GetValidPlayers())
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            foreach (var attachedEntity in GetAttachedModelEntities(pawn))
            {
                SetAttachedShadowStrength(attachedEntity, 1.0f);
                SetAttachedRenderAlpha(attachedEntity, 255);
            }

            foreach (var weapon in GetWeaponEntities(pawn))
            {
                foreach (var attachedEntity in GetAttachedModelEntities(weapon))
                {
                    SetAttachedShadowStrength(attachedEntity, 1.0f);
                    SetAttachedRenderAlpha(attachedEntity, 255);
                }
            }
        }
    }

    private static bool ShouldTrackMissDamage(CCSPlayerController? player)
    {
        if (!Util.IsPlayerValid(player)) return false;
        if (Globals.InvisiblePlayers.ContainsKey(player)) return false;

        return HasInvisibleOpponent(player);
    }

    private static CCSPlayerController? GetPlayerBySlot(int slot)
    {
        return Util.GetValidPlayers().FirstOrDefault(player => player.Slot == slot, null);
    }

    private static bool HasInvisibleOpponent(CCSPlayerController player)
    {
        return Globals.InvisiblePlayers.Keys.Any(invisiblePlayer =>
            Util.IsPlayerValid(invisiblePlayer) &&
            invisiblePlayer.Slot != player.Slot &&
            invisiblePlayer.Team >= CsTeam.Terrorist &&
            invisiblePlayer.Team != player.Team);
    }

    private static string NormalizeWeaponName(string weaponName)
    {
        var normalized = weaponName.ToLowerInvariant();
        return normalized.StartsWith("weapon_") ? normalized["weapon_".Length..] : normalized;
    }

    private static bool IsKnifeWeapon(string weaponName)
    {
        return weaponName.Contains("knife") || weaponName.Contains("bayonet");
    }

    private static bool IsGrenadeWeapon(string weaponName)
    {
        return weaponName.Contains("hegrenade") ||
               weaponName.Contains("molotov") ||
               weaponName.Contains("incgrenade");
    }

    private static bool IsShotgunWeapon(string weaponName)
    {
        return weaponName.Contains("nova") ||
               weaponName.Contains("xm1014") ||
               weaponName.Contains("mag7") ||
               weaponName.Contains("sawedoff");
    }

    private static bool IsSniperWeapon(string weaponName)
    {
        return weaponName.Contains("awp") ||
               weaponName.Contains("ssg08") ||
               weaponName.Contains("scar20") ||
               weaponName.Contains("g3sg1");
    }

    private static bool IsSmgWeapon(string weaponName)
    {
        return weaponName.Contains("mp5") ||
               weaponName.Contains("mp7") ||
               weaponName.Contains("mp9") ||
               weaponName.Contains("mac10") ||
               weaponName.Contains("p90") ||
               weaponName.Contains("bizon") ||
               weaponName.Contains("ump45");
    }

    private static bool IsPistolWeapon(string weaponName)
    {
        return weaponName.Contains("deagle") ||
               weaponName.Contains("elite") ||
               weaponName.Contains("fiveseven") ||
               weaponName.Contains("glock") ||
               weaponName.Contains("hkp2000") ||
               weaponName.Contains("p250") ||
               weaponName.Contains("tec9") ||
               weaponName.Contains("cz75") ||
               weaponName.Contains("revolver") ||
               weaponName.Contains("usp_silencer");
    }

    private static bool IsRifleOrLmgWeapon(string weaponName)
    {
        return weaponName.Contains("ak47") ||
               weaponName.Contains("m4a1") ||
               weaponName.Contains("m4a4") ||
               weaponName.Contains("m4a1_silencer") ||
               weaponName.Contains("famas") ||
               weaponName.Contains("galilar") ||
               weaponName.Contains("aug") ||
               weaponName.Contains("sg556") ||
               weaponName.Contains("negev") ||
               weaponName.Contains("m249");
    }

    private static int GetMissedShotDamage(string weaponName)
    {
        if (IsKnifeWeapon(weaponName)) return 5;
        if (IsSniperWeapon(weaponName)) return 8;
        if (IsShotgunWeapon(weaponName) || IsGrenadeWeapon(weaponName)) return 5;
        if (IsPistolWeapon(weaponName) || IsSmgWeapon(weaponName) || IsRifleOrLmgWeapon(weaponName)) return 2;

        return 0;
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
        Globals.Plugin.RegisterListener<Listeners.OnPlayerButtonsChanged>(OnPlayerButtonsChanged);
        Globals.Plugin.RegisterEventHandler<EventBombBeginplant>(OnPlayerStartPlant);
        // EventPlayerShoot doesnt work so we use EventBulletImpact
        Globals.Plugin.RegisterEventHandler<EventBulletImpact>(OnPlayerShoot);
        Globals.Plugin.RegisterEventHandler<EventPlayerSound>(OnPlayerSound);
        Globals.Plugin.RegisterEventHandler<EventBombBegindefuse>(OnPlayerStartDefuse);
        Globals.Plugin.RegisterEventHandler<EventWeaponReload>(OnPlayerReload);
        Globals.Plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        Globals.Plugin.RegisterEventHandler<EventGrenadeThrown>(OnGrenadeThrown);
        Globals.Plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire);

        // Weapon skin suppression events
        Globals.Plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        Globals.Plugin.RegisterEventHandler<EventItemPickup>(OnItemPickup);

        Globals.Plugin.AddCommand("css_invisible", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
        Globals.Plugin.AddCommand("css_invis", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
    }

    public static void Cleanup()
    {
        _entities.Clear();
        _pendingShots.Clear();
        _pendingGrenades.Clear();
        _lastKnifeFireAttemptBySlot.Clear();
        _lastKnifeAttack2AttemptBySlot.Clear();
        _lastKnifeHitBySlot.Clear();
        _wasSkinSuppressionEnabled = false;
        _lastWeaponSkinSweepAt = 0.0f;
        _loggedWeaponSkinReflectionWarning = false;
        _invisibleBombCarrierSlot = -1;
        _restoredGlobalCosmetics = false;
        _nextAttemptId = 1;

        foreach (var player in Util.GetValidPlayers())
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            pawn.Render = Color.FromArgb(255, pawn.Render);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
            pawn.ShadowStrength = 1.0f;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_flShadowStrength");

            foreach (var weaponHandle in pawn.WeaponServices!.MyWeapons)
            {
                var weapon = weaponHandle.Value;
                if (weapon == null || !weapon.IsValid) continue;

                weapon.ShadowStrength = 1.0f;
                Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_flShadowStrength");
                weapon.Render = Color.FromArgb(255, weapon.Render);
                Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");

                foreach (var attachedEntity in GetAttachedModelEntities(weapon))
                {
                    SetAttachedShadowStrength(attachedEntity, 1.0f);
                    SetAttachedRenderAlpha(attachedEntity, 255);
                }
            }

            foreach (var attachedEntity in GetAttachedModelEntities(pawn))
            {
                SetAttachedShadowStrength(attachedEntity, 1.0f);
                SetAttachedRenderAlpha(attachedEntity, 255);
            }
        }

        Globals.InvisiblePlayers.Clear();
    }
}
