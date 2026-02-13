using System.Drawing;
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
    private const float KnifeSecondaryResolveDelay = 0.35f;
    private const float KnifeAttemptDedupeWindow = 0.08f;

    private sealed class PendingMissAttempt
    {
        public int Id { get; init; }
        public int Damage { get; init; }
        public bool Resolved { get; set; }
    }

    private static List<CEntityInstance> _entities = [];
    private static int _nextAttemptId = 1;
    private static Dictionary<int, List<PendingMissAttempt>> _pendingShots = [];
    private static Dictionary<int, List<PendingMissAttempt>> _pendingGrenades = [];
    private static Dictionary<int, float> _lastKnifeAttemptBySlot = [];

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
            if (player.Team != CsTeam.Terrorist && !gameRules.GameRules.BombPlanted && !c4.IsPlantingViaUse && !gameRules.GameRules.BombDropped)
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
        if (isKnife && !TryTrackKnifeAttempt(player!.Slot)) return HookResult.Continue;

        var damage = GetMissedShotDamage(weaponName);
        if (damage <= 0) return HookResult.Continue;

        var resolveDelay = isKnife ? KnifePrimaryResolveDelay : MissResolveDelay;
        QueueMissAttempt(_pendingShots, player!, damage, resolveDelay);

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
        if (!TryTrackKnifeAttempt(player.Slot)) return;

        QueueMissAttempt(_pendingShots, player, GetMissedShotDamage(weaponName), KnifeSecondaryResolveDelay);
    }

    public static HookResult OnGrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!ShouldTrackMissDamage(player)) return HookResult.Continue;

        var weaponName = NormalizeWeaponName(@event.Weapon ?? string.Empty);
        if (!IsGrenadeWeapon(weaponName)) return HookResult.Continue;

        QueueMissAttempt(_pendingGrenades, player!, 5, GrenadeResolveDelay);

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

    private static void QueueMissAttempt(Dictionary<int, List<PendingMissAttempt>> pendingBySlot, CCSPlayerController player, int damage, float delay)
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
            Damage = damage
        };

        attempts.Add(attempt);

        Globals.Plugin.AddTimer(delay, () =>
        {
            if (!pendingBySlot.TryGetValue(slot, out var activeAttempts)) return;

            var currentAttempt = activeAttempts.FirstOrDefault(item => item.Id == attempt.Id);
            if (currentAttempt == null) return;

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

    private static bool TryTrackKnifeAttempt(int slot)
    {
        var now = Server.CurrentTime;
        if (_lastKnifeAttemptBySlot.TryGetValue(slot, out var lastAttemptAt) &&
            now - lastAttemptAt < KnifeAttemptDedupeWindow)
        {
            return false;
        }

        _lastKnifeAttemptBySlot[slot] = now;
        return true;
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

    private static IEnumerable<CBaseEntity> GetAttachedModelEntities(CCSPlayerPawn pawn)
    {
        var rootNode = pawn.CBodyComponent?.SceneNode;
        if (rootNode == null) yield break;

        foreach (var childNode in Util.GetChildrenRecursive(rootNode))
        {
            var identity = childNode.Owner?.Entity;
            if (identity == null) continue;

            var entityInstance = identity.EntityInstance;
            if (!entityInstance.IsValid) continue;
            if (entityInstance.Handle == pawn.Handle) continue;

            var entity = entityInstance.As<CBaseEntity>();
            if (entity == null || !entity.IsValid) continue;

            var renderProperty = entity.GetType().GetProperty("Render");
            if (renderProperty?.PropertyType != typeof(Color) || !renderProperty.CanRead || !renderProperty.CanWrite)
                continue;

            yield return entity;
        }
    }

    private static void SetAttachedRenderAlpha(CBaseEntity entity, int alpha)
    {
        var renderProperty = entity.GetType().GetProperty("Render");
        if (renderProperty?.PropertyType != typeof(Color) || !renderProperty.CanRead || !renderProperty.CanWrite)
            return;

        if (renderProperty.GetValue(entity) is not Color currentColor) return;

        var clampedAlpha = Math.Clamp(alpha, 0, 255);
        renderProperty.SetValue(entity, Color.FromArgb(clampedAlpha, currentColor));
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
    }

    private static void SetAttachedShadowStrength(CBaseEntity entity, float shadowStrength)
    {
        var shadowProperty = entity.GetType().GetProperty("ShadowStrength");
        if (shadowProperty?.PropertyType != typeof(float) || !shadowProperty.CanWrite)
            return;

        shadowProperty.SetValue(entity, shadowStrength);
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_flShadowStrength");
    }

    private static void AddInvisibleTransmitEntity(CEntityInstance entity)
    {
        if (!_entities.Contains(entity))
            _entities.Add(entity);
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

        Globals.Plugin.AddCommand("css_invisible", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
        Globals.Plugin.AddCommand("css_invis", "Makes a player invisible", CommandInvisible.OnInvisibleCommand);
    }

    public static void Cleanup()
    {
        _entities.Clear();
        _pendingShots.Clear();
        _pendingGrenades.Clear();
        _lastKnifeAttemptBySlot.Clear();
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
