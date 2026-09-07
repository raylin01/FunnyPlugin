using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Funnies.Models;

namespace Funnies.Modules;

public static class MissPenalty
{
    private static readonly AttackLedger Ledger = new();
    private sealed record KnifeState(uint Weapon, int Primary, int Secondary);
    private sealed class Projectile(AttackLedger.Attempt attempt, bool fire)
    {
        public AttackLedger.Attempt Attempt = attempt;
        public bool Fire = fire;
        public bool Linked;
        public float X, Y, Z;
        public int DeletedTick = -1;
    }
    private static readonly Dictionary<uint, KnifeState> Knives = [];
    private static readonly Dictionary<uint, Projectile> Projectiles = [];
    private static readonly HashSet<uint> ObservedProjectiles = [];
    private static readonly Dictionary<(uint Victim, IntPtr Info), int> HealthBefore = [];

    private static bool Eligible(CCSPlayerController? player) => Util.IsPlayerValid(player) &&
        player.PawnIsAlive && player.TeamNum >= 2 && !Globals.InvisiblePlayers.ContainsKey(player) &&
        Globals.InvisiblePlayers.Keys.Any(p => Util.IsPlayerValid(p) && p.PawnIsAlive && p.TeamNum >= 2 && p.TeamNum != player.TeamNum);

    private static CCSPlayerController? Player(uint pawn) => Util.GetValidPlayers().FirstOrDefault(p => p.PlayerPawn.Raw == pawn);
    private static bool Knife(string name) => name.Contains("knife") || name.Contains("bayonet");
    private static bool Grenade(string name) => name is "hegrenade_projectile" or "molotov_projectile";

    public static int DamageFor(string name)
    {
        name = name.Replace("weapon_", "");
        if (Knife(name)) return 5;
        return name switch
        {
            "awp" or "ssg08" or "scar20" or "g3sg1" => 8,
            "nova" or "xm1014" or "mag7" or "sawedoff" => 5,
            "deagle" or "elite" or "fiveseven" or "glock" or "hkp2000" or "p250" or "tec9" or "cz75a" or "revolver" or "usp_silencer" or
            "mp5sd" or "mp7" or "mp9" or "mac10" or "p90" or "bizon" or "ump45" or
            "ak47" or "m4a1" or "m4a4" or "m4a1_silencer" or "famas" or "galilar" or "aug" or "sg556" or "negev" or "m249" => 2,
            _ => 0
        };
    }

    private static HookResult OnFire(EventWeaponFire e, GameEventInfo info)
    {
        var p = e.Userid;
        if (!Eligible(p)) return HookResult.Continue;
        var weapon = p!.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        var damage = DamageFor(e.Weapon);
        if (weapon?.IsValid == true && damage > 0)
            Ledger.Record(p.PlayerPawn.Raw, weapon.EntityHandle.Raw, Server.TickCount, damage);
        return HookResult.Continue;
    }

    public static void OnTick()
    {
        HealthBefore.Clear(); // Damage callbacks are synchronous; discard suppressed pre-only calls.
        foreach (var p in Util.GetValidPlayers())
        {
            var pawn = p.PlayerPawn.Value;
            var w = pawn?.WeaponServices?.ActiveWeapon.Value;
            if (!Eligible(p) || w?.IsValid != true || !Knife(w.DesignerName)) { Knives.Remove(p.PlayerPawn.Raw); continue; }
            var state = new KnifeState(w.EntityHandle.Raw, w.NextPrimaryAttackTick, w.NextSecondaryAttackTick);
            if (Knives.TryGetValue(p.PlayerPawn.Raw, out var previous) && previous.Weapon == state.Weapon &&
                (state.Primary != previous.Primary || state.Secondary != previous.Secondary) &&
                (p.Buttons & (PlayerButtons.Attack | PlayerButtons.Attack2)) != 0 &&
                Math.Max(state.Primary, state.Secondary) > Server.TickCount)
                Ledger.Record(p.PlayerPawn.Raw, state.Weapon, Server.TickCount, 5);
            Knives[p.PlayerPawn.Raw] = state;
        }
        foreach (var attempt in Ledger.Collect(Server.TickCount))
        {
            if (!attempt.Hit) Apply(attempt);
            foreach (var key in Projectiles.Where(p => ReferenceEquals(p.Value.Attempt, attempt)).Select(p => p.Key).ToList()) Projectiles.Remove(key);
        }
    }

    private static void Apply(AttackLedger.Attempt attempt)
    {
        var player = Player(attempt.Pawn);
        if (!Eligible(player)) return;
        var pawn = player!.PlayerPawn.Value!;
        // A fixed gameplay cost intentionally bypasses armor; it does not rewrite or
        // refund engine damage. Revalidate the same life immediately before charging.
        var health = Math.Max(0, pawn.Health - attempt.Damage);
        if (health == 0) pawn.CommitSuicide(false, true);
        else { pawn.Health = health; Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth"); }
        if (Globals.Config.DebugDamage) Console.WriteLine($"[Funnies penalty] tick={Server.TickCount} pawn={attempt.Pawn} source={attempt.Source} cost={attempt.Damage} hp={health}");
    }

    private static Projectile? Track(CEntityInstance entity)
    {
        var raw = entity.EntityHandle.Raw;
        if (Projectiles.TryGetValue(raw, out var tracked)) return tracked;
        if (!Grenade(entity.DesignerName)) return null;
        if (!ObservedProjectiles.Add(raw)) return null;
        var grenade = entity.As<CBaseGrenade>();
        var owner = Player(grenade.Thrower.Raw);
        if (!Eligible(owner)) return null;
        var attempt = Ledger.Record(owner!.PlayerPawn.Raw, raw, Server.TickCount, 5, true);
        tracked = new Projectile(attempt, entity.DesignerName == "molotov_projectile");
        UpdatePosition(tracked, grenade);
        Projectiles[raw] = tracked;
        return tracked;
    }

    private static void UpdatePosition(Projectile projectile, CBaseEntity entity)
    {
        if (entity.AbsOrigin is not { } pos) return;
        projectile.X = pos.X; projectile.Y = pos.Y; projectile.Z = pos.Z;
    }

    private static Projectile? LinkInferno(CBaseEntity entity)
    {
        var raw = entity.EntityHandle.Raw;
        if (Projectiles.TryGetValue(raw, out var known)) return known;
        var pos = entity.AbsOrigin;
        if (pos == null) return null;
        var owner = entity.OwnerEntity.Raw;
        var candidates = Projectiles.Where(p => p.Value.Fire && !p.Value.Linked &&
            (p.Value.DeletedTick < 0 || Server.TickCount - p.Value.DeletedTick <= 2)).ToList();
        foreach (var candidate in candidates)
        {
            var live = new CounterStrikeSharp.API.Modules.Utils.CHandle<CBaseEntity>(candidate.Key).Value;
            if (live?.IsValid == true) UpdatePosition(candidate.Value, live);
        }
        // An inferno belongs to the thrower (or its projectile). Restrict spatial
        // matching to that owner; never let another player's fire cancel this throw.
        var match = candidates.Where(p => p.Value.Attempt.Pawn == owner || p.Key == owner)
            .Select(p => (p.Value, Distance: MathF.Pow(p.Value.X-pos.X,2)+MathF.Pow(p.Value.Y-pos.Y,2)+MathF.Pow(p.Value.Z-pos.Z,2)))
            .Where(p => p.Distance < 256*256).OrderBy(p => p.Distance).FirstOrDefault().Value;
        if (match == null) return null;
        match.Linked = true;
        match.Attempt.FinishTick = null;
        Projectiles[raw] = match;
        return match;
    }

    private static void OnSpawned(CEntityInstance entity)
    {
        if (!Grenade(entity.DesignerName) && entity.DesignerName != "inferno") return;
        Server.NextFrame(() => {
            if (!entity.IsValid) return;
            if (entity.DesignerName == "inferno") LinkInferno(entity.As<CBaseEntity>());
            else Track(entity);
        });
    }

    private static void OnDeleted(CEntityInstance entity)
    {
        ObservedProjectiles.Remove(entity.EntityHandle.Raw);
        if (!Projectiles.TryGetValue(entity.EntityHandle.Raw, out var projectile)) return;
        if (entity.IsValid) UpdatePosition(projectile, entity.As<CBaseEntity>());
        projectile.DeletedTick = Server.TickCount;
        if (entity.DesignerName == "inferno" || !projectile.Linked)
            projectile.Attempt.FinishTick = Server.TickCount + 2;
    }

    private static void FinishAt(int entityIndex)
    {
        foreach (var pair in Projectiles)
            if (new CounterStrikeSharp.API.Modules.Utils.CHandle<CBaseEntity>(pair.Key).Index == entityIndex)
                pair.Value.Attempt.FinishTick = Server.TickCount + 2;
    }

    private static void OnDamage(CCSPlayerPawn victimPawn, CTakeDamageInfo damage, CTakeDamageResult result)
    {
        HealthBefore.Remove((victimPawn.EntityHandle.Raw, damage.Handle), out var before);
        // Some native damage paths do not supply CTakeDamageResult.
        var lost = result.Handle != IntPtr.Zero ? result.HealthLost : before - victimPawn.Health;
        if (lost <= 0) return;
        var victim = Player(victimPawn.EntityHandle.Raw);
        var attacker = Player(damage.Attacker.Raw);
        if (victim == null || attacker == null || victim == attacker ||
            victim.TeamNum == attacker.TeamNum || !Globals.InvisiblePlayers.ContainsKey(victim) ||
            Globals.InvisiblePlayers.ContainsKey(attacker)) return;
        var inflictor = damage.Inflictor.Value;
        if (inflictor?.IsValid == true && (Grenade(inflictor.DesignerName) || inflictor.DesignerName == "inferno"))
        {
            var projectile = inflictor.DesignerName == "inferno" ? LinkInferno(inflictor) : Track(inflictor);
            if (projectile != null) projectile.Attempt.Hit = true;
            return;
        }
        var weapon = damage.Ability.Value;
        if (weapon?.IsValid != true || !weapon.DesignerName.StartsWith("weapon_"))
            weapon = attacker.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        // Grenade/fire damage must never fall back to a held gun or knife.
        if ((damage.BitsDamageType & (DamageTypes_t.DMG_BLAST | DamageTypes_t.DMG_BURN)) != 0) return;
        if (weapon?.IsValid == true)
            Ledger.Hit(attacker.PlayerPawn.Raw, weapon.EntityHandle.Raw, Server.TickCount);
    }

    private static void Cancel(uint pawn)
    {
        Ledger.Cancel(pawn);
        Knives.Remove(pawn);
        // Keep observed handles until deletion so lingering projectiles from a
        // dead player's previous life cannot create a fresh penalty after respawn.
        foreach (var key in Projectiles.Where(p => p.Value.Attempt.Pawn == pawn).Select(p => p.Key).ToList()) Projectiles.Remove(key);
    }

    public static void Clear() { Ledger.Clear(); Knives.Clear(); Projectiles.Clear(); ObservedProjectiles.Clear(); HealthBefore.Clear(); }
    public static void Setup()
    {
        Globals.Plugin.RegisterEventHandler<EventWeaponFire>(OnFire);
        Globals.Plugin.RegisterEventHandler<EventHegrenadeDetonate>((e,i) => { FinishAt(e.Entityid); return HookResult.Continue; });
        Globals.Plugin.RegisterEventHandler<EventInfernoExpire>((e,i) => { FinishAt(e.Entityid); return HookResult.Continue; });
        Globals.Plugin.RegisterEventHandler<EventInfernoExtinguish>((e,i) => { FinishAt(e.Entityid); return HookResult.Continue; });
        Globals.Plugin.RegisterListener<Listeners.OnPlayerTakeDamagePre>((pawn, damage) => {
            HealthBefore[(pawn.EntityHandle.Raw, damage.Handle)] = pawn.Health;
            return HookResult.Continue;
        });
        Globals.Plugin.RegisterListener<Listeners.OnPlayerTakeDamagePost>(OnDamage);
        Globals.Plugin.RegisterListener<Listeners.OnEntitySpawned>(OnSpawned);
        Globals.Plugin.RegisterListener<Listeners.OnEntityDeleted>(OnDeleted);
        Globals.Plugin.RegisterEventHandler<EventRoundPrestart>((e,i) => { Clear(); return HookResult.Continue; });
        Globals.Plugin.RegisterEventHandler<EventPlayerDeath>((e,i) => { if(e.Userid != null) Cancel(e.Userid.PlayerPawn.Raw); return HookResult.Continue; });
        Globals.Plugin.RegisterEventHandler<EventPlayerDisconnect>((e,i) => { if(e.Userid != null) Cancel(e.Userid.PlayerPawn.Raw); return HookResult.Continue; });
        Globals.Plugin.RegisterListener<Listeners.OnMapStart>(_ => Clear());
    }
}

