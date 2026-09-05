using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;

public partial class TestDriver : BasePlugin
{
    private readonly Dictionary<int, (ulong Mask, float End)> Held = [];
    private readonly Dictionary<int, (float Yaw, float Pitch)> Aim = [];
    private (int Slot, float Delay)? EnterFire;
    private (int Slot, RenderMode_t Mode)? RenderOverride;
    private CEconWearable? GloveFixture;
    private (CCSPlayerPawn Pawn, ushort Definition, bool Initialized)? SavedGloves;
    private void RestoreGloveFixture()
    {
        if(GloveFixture?.IsValid == true) GloveFixture.Remove();
        GloveFixture=null;
        if(SavedGloves is { } saved && saved.Pawn.IsValid) {
            saved.Pawn.EconGloves.ItemDefinitionIndex=saved.Definition;
            saved.Pawn.EconGloves.Initialized=saved.Initialized;
            saved.Pawn.EconGlovesChanged++;
            Utilities.SetStateChanged(saved.Pawn,"CCSPlayerPawn","m_nEconGlovesChanged");
        }
        SavedGloves=null;
    }
    public override void Unload(bool hotReload) { RestoreGloveFixture(); RestorePrivateGloves(); }
    private ulong NextFixtureItemId = 900000000;
    private MemoryFunctionVoid<nint, string, float>? SetAttribute;
    private void Paint(CEconItemView item, ushort definition, int paint)
    {
        // Signature from cs2-WeaponPaints gamedata; this fixture runs only on Windows.
        SetAttribute ??= new("40 53 55 41 56 48 81 EC 90 00 00 00");
        item.ItemDefinitionIndex = definition;
        var id = NextFixtureItemId++;
        item.ItemID = id;
        item.ItemIDLow = (uint)id;
        item.ItemIDHigh = (uint)(id >> 32);
        foreach (var list in new[] { item.AttributeList, item.NetworkedDynamicAttributes })
        {
            list.Attributes.RemoveAll();
            SetAttribute.Invoke(list.Handle, "set item texture prefab", paint);
            SetAttribute.Invoke(list.Handle, "set item texture seed", 1);
            SetAttribute.Invoke(list.Handle, "set item texture wear", 0.01f);
        }
        item.Initialized = true;
    }
    public override string ModuleName => "Local regression driver";
    public override string ModuleVersion => "1.0";
    public override void Load(bool hotReload)
    {
        SetupPrivateGloves();
        AddCommand("test_acquire", "Probe native item acquisition: slot definition method", (caller, command) => {
            var p = Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            var pawn = p?.PlayerPawn.Value;
            var weapon = pawn?.WeaponServices?.MyWeapons.Select(w => w.Value).FirstOrDefault(w => w != null && w.IsValid);
            if (weapon == null || pawn?.ItemServices == null) return;
            var item = weapon.AttributeManager.Item;
            var saved = item.ItemDefinitionIndex;
            try {
                item.ItemDefinitionIndex = ushort.Parse(command.GetArg(2));
                var services = new CCSPlayer_ItemServices(pawn.ItemServices.Handle);
                var result = services.CanAcquire(item, (AcquireMethod)int.Parse(command.GetArg(3)));
                Console.WriteLine($"[TEST ACQUIRE] slot={p!.Slot} definition={item.ItemDefinitionIndex} result={result}");
            } finally { item.ItemDefinitionIndex = saved; }
        });
        AddCommand("test_purchase_event", "Synthetic purchase counter fixture: slot item", (caller, command) => {
            var e = new EventItemPurchase(false) { Userid = Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1))), Weapon = command.GetArg(2) };
            e.FireEvent(false);
        });
        AddCommand("test_money_state", "Inspect current money", (caller, command) => {
            foreach (var p in Utilities.GetPlayers()) Console.WriteLine($"[TEST MONEY] slot={p.Slot} money={p.InGameMoneyServices?.Account}");
        });
        AddCommand("test_fade_state", "Inspect bot pawn and attachment opacity: slot", (caller, command) => {
            var pawn=Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)))?.PlayerPawn.Value;
            if(pawn==null) return;
            Console.WriteLine($"[TEST ALPHA] pawn={pawn.EntityHandle.Raw} alpha={pawn.Render.A} mode={pawn.RenderMode}");
            var seen=new HashSet<IntPtr>();
            void Walk(CGameSceneNode? node) {
                while(node != null && seen.Add(node.Handle)) {
                    if(node.Owner is { } owner && owner.IsValid) {
                        var m=owner.As<CBaseModelEntity>();
                        Console.WriteLine($"[TEST ALPHA] child={owner.DesignerName}:{owner.EntityHandle.Raw} alpha={m.Render.A} mode={m.RenderMode}");
                    }
                    Walk(node.Child);
                    node=node.NextSibling;
                }
            }
            Walk(pawn.CBodyComponent?.SceneNode?.Child);
            foreach(var glove in Utilities.FindAllEntitiesByDesignerName<CEconWearable>("wearable_item"))
                Console.WriteLine($"[TEST ALPHA] wearable={glove.EntityHandle.Raw} alpha={glove.Render.A} mode={glove.RenderMode}");
        });
        AddCommand("test_glove_clear", "Remove experimental bot wearable: slot", (caller, command) => {
            if(caller != null) return;
            var p=Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            if(p?.IsBot != true) return;
            RestoreGloveFixture();
            foreach(var glove in Utilities.FindAllEntitiesByDesignerName<CEconWearable>("wearable_item").ToArray()) {
                if(glove.IsValid && glove.OwnerEntity.Raw == p.PlayerPawn.Raw) {
                    Console.WriteLine($"[TEST GLOVE] removing {glove.EntityHandle.Raw}");
                    glove.Remove();
                }
            }
        });
        AddCommand("test_glove_entity", "Experimental server-owned glove fixture: bot slot", (caller, command) => {
            if(caller != null) return;
            var p=Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            if(p?.IsBot != true || p.PlayerPawn.Value is not { } pawn) return;
            RestoreGloveFixture();
            var glove=Utilities.CreateEntityByName<CEconWearable>("wearable_item");
            if(glove?.IsValid != true) { Console.WriteLine("[TEST GLOVE] wearable_item unavailable"); return; }
            GloveFixture=glove;
            glove.AlwaysAllow=true;
            glove.Render=System.Drawing.Color.White;
            glove.RenderMode=RenderMode_t.kRenderNormal;
            glove.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(1u << 2);
            Paint(glove.AttributeManager.Item,5030,10048);
            glove.FallbackPaintKit=10048; glove.FallbackSeed=1; glove.FallbackWear=0.01f;
            glove.SetModel("agents/models/shared/arms/glove_sporty/glove_sporty.vmdl");
            glove.OwnerEntity.Raw=pawn.EntityHandle.Raw;
            glove.DispatchSpawn();
            glove.Teleport(pawn.AbsOrigin,pawn.AbsRotation,new Vector(0,0,0));
            glove.AcceptInput("FollowEntity",pawn,null,"!activator");
            glove.AcceptInput("SetBodygroup",value:"first_or_third_person,0");
            SavedGloves=(pawn,pawn.EconGloves.ItemDefinitionIndex,pawn.EconGloves.Initialized);
            pawn.EconGloves.ItemDefinitionIndex=0;
            pawn.EconGloves.Initialized=false;
            pawn.EconGlovesChanged++;
            Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_nEconGlovesChanged");
            // Model metadata: agent group 1 hides default gloves; glove group 0 is third-person.
            AddTimer(0.2f, () => {
                if(pawn.IsValid) pawn.AcceptInput("SetBodygroup",value:"first_or_third_person,1");
            }, TimerFlags.STOP_ON_MAPCHANGE);
            Console.WriteLine($"[TEST GLOVE] server wearable created {glove.EntityHandle.Raw} render={glove.Render} effects={glove.Effects} pos={glove.AbsOrigin} mesh={glove.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.MeshGroupMask}");
        });
        AddCommand("test_render_mode", "Bot rendering experiment: slot mode (-1 clears)", (caller, command) => {
            if(caller != null) return;
            var slot=int.Parse(command.GetArg(1));
            if(Utilities.GetPlayerFromSlot(slot)?.IsBot != true) return;
            var mode=int.Parse(command.GetArg(2));
            RenderOverride=mode < 0 ? null : (slot,(RenderMode_t)mode);
        });
        AddCommand("test_models", "Dump server model entities", (caller, command) => {
            foreach(var e in Utilities.GetAllEntities()) {
                if(!e.IsValid) continue;
                var b=e.As<CBaseEntity>();
                var node=b.CBodyComponent?.SceneNode;
                if(node==null) continue;
                var skeleton=node.GetSkeletonInstance();
                if(skeleton.Handle==IntPtr.Zero) continue;
                Console.WriteLine($"[TEST MODEL] {e.DesignerName}:{e.EntityHandle.Raw} owner={b.OwnerEntity.Raw} parent={node.PParent?.Owner?.DesignerName} model={skeleton.ModelState.ModelName}");
            }
        });
        AddCommand("test_skins", "Equip bot with custom cosmetic regression fixtures: slot", (caller, command) => {
            if(caller != null) return;
            var p = Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            if(p?.IsBot != true || p.PlayerPawn.Value is not { } pawn) return;
            pawn.SetModel("agents/models/tm_professional/tm_professional_varf5.vmdl");
            Paint(pawn.EconGloves, 5030, 10048);
            pawn.EconGlovesChanged++;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_nEconGlovesChanged");
            pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,0");
            AddTimer(0.2f, () => {
                if(pawn.IsValid) pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,1");
            }, TimerFlags.STOP_ON_MAPCHANGE);
            foreach(var h in pawn.WeaponServices!.MyWeapons.ToArray()) {
                var w = h.Value;
                if(w?.DesignerName != "weapon_glock") continue;
                pawn.RemovePlayerItem(w);
                w.Remove();
            }
            AddTimer(0.25f, () => {
                if(!pawn.IsValid || !p.IsValid || !p.PawnIsAlive) return;
                var w = new CBasePlayerWeapon(p.GiveNamedItem("weapon_glock"));
                Paint(w.AttributeManager.Item, 4, 38);
                w.AttributeManager.Item.AccountID = (uint)p.SteamID;
                w.FallbackPaintKit = 38; w.FallbackSeed = 1; w.FallbackWear = 0.01f;
                w.AcceptInput("SetBodygroup", value: "body,0");
                pawn.WeaponServices!.ActiveWeapon.Raw = w.EntityHandle.Raw;
                Console.WriteLine($"[TEST SKIN GLOCK] created handle={w.EntityHandle.Raw} paint={w.FallbackPaintKit}");
            }, TimerFlags.STOP_ON_MAPCHANGE);
            Console.WriteLine($"[TEST SKINS] slot={p.Slot} Darryl / Vice / Glock Fade equipped");
        });
        AddCommand("test_cosmetics", "Inspect active fixture cosmetics", (caller, command) => {
            foreach(var p in Utilities.GetPlayers()) {
                var pawn=p.PlayerPawn.Value;
                if(pawn==null) continue;
                var gloves=pawn.EconGloves;
                Console.WriteLine($"[TEST COSMETICS] slot={p.Slot} model={pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName} gloves={gloves.ItemDefinitionIndex} initialized={gloves.Initialized}");
                foreach(var h in pawn.WeaponServices!.MyWeapons) {
                    var w=h.Value;
                    if(w?.DesignerName=="weapon_glock") Console.WriteLine($"[TEST GLOCK] slot={p.Slot} paint={w.FallbackPaintKit} item={w.AttributeManager.Item.ItemDefinitionIndex}");
                }
            }
        });
        AddCommand("test_client", "Execute server-supported client command: slot command", (caller, command) => {
            var player = Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            var text = command.ArgString;
            player?.ExecuteClientCommandFromServer(text[(text.IndexOf(' ') + 1)..]);
        });
        AddCommand("test_team", "Fixture team: slot team number", (caller, command) => {
            var p=Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            if(p==null) return;
            p.SwitchTeam((CsTeam)int.Parse(command.GetArg(2)));
            if(!p.PawnIsAlive) p.Respawn();
        });
        AddCommand("test_reveal", "Trigger the normal sound reveal path: slot total seconds", (caller, command) => {
            var p=Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            if(p==null) return;
            new EventPlayerSound(true) { Userid=p, Duration=float.Parse(command.GetArg(2))/2f }.FireEvent(false);
        });
        AddCommand("test_enterfire", "Move bot into next inferno after delay: slot seconds", (caller, command) => {
            EnterFire=(int.Parse(command.GetArg(1)), float.Parse(command.GetArg(2)));
        });
        AddCommand("test_after", "Schedule local fixture command: seconds command", (caller, command) => {
            var seconds = float.Parse(command.GetArg(1));
            var text = command.ArgString;
            text = text[(text.IndexOf(' ')+1)..];
            AddTimer(seconds,()=>Server.ExecuteCommand(text));
        });
        RegisterListener<Listeners.OnTick>(() => {
            foreach(var (slot, aim) in Aim) {
                var p=Utilities.GetPlayerFromSlot(slot);
                if(p?.IsBot!=true || p.PlayerPawn.Value is not { } pawn) continue;
                pawn.V_angle.X=aim.Pitch; pawn.V_angle.Y=aim.Yaw;
                if(pawn.EyeAngles.X!=aim.Pitch || pawn.EyeAngles.Y!=aim.Yaw) {
                    pawn.EyeAngles.X=aim.Pitch; pawn.EyeAngles.Y=aim.Yaw;
                    Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_angEyeAngles");
                }
            }
            if(RenderOverride is { } render && Utilities.GetPlayerFromSlot(render.Slot)?.PlayerPawn.Value is { } renderPawn) {
                renderPawn.RenderMode=render.Mode;
                Utilities.SetStateChanged(renderPawn, "CBaseModelEntity", "m_nRenderMode");
            }
            foreach (var (slot, held) in Held.ToArray()) {
                var pawn = Utilities.GetPlayerFromSlot(slot)?.PlayerPawn.Value;
                if (pawn?.MovementServices is not { } movement) { Held.Remove(slot); continue; }
                var mask = Server.CurrentTime < held.End ? held.Mask : 0;
                movement.Buttons.ButtonStates[0] = mask;
                movement.QueuedButtonDownMask = mask;
                movement.QueuedButtonChangeMask = held.Mask;
                if (Aim.TryGetValue(slot, out var aim)) {
                    pawn.V_angle.X=aim.Pitch; pawn.V_angle.Y=aim.Yaw;
                    pawn.EyeAngles.X=aim.Pitch; pawn.EyeAngles.Y=aim.Yaw;
                }
                if(mask == 0) Held.Remove(slot);
            }
        });
        AddCommand("test_hold", "Bot input: slot mask seconds", (caller, command) => {
            var slot = int.Parse(command.GetArg(1));
            if(Utilities.GetPlayerFromSlot(slot)?.IsBot != true) return;
            Held[slot] = (ulong.Parse(command.GetArg(2)), Server.CurrentTime+float.Parse(command.GetArg(3)));
        });
        AddCommand("test_aim", "Bot aim: slot yaw pitch", (caller, command) => {
            Aim[int.Parse(command.GetArg(1))]=(float.Parse(command.GetArg(2)),float.Parse(command.GetArg(3)));
        });
        AddTimer(0.1f, () => {
            var inbox = Path.Combine(ModuleDirectory, "commands.txt");
            if (!File.Exists(inbox)) return;
            var commands = File.ReadAllLines(inbox);
            File.Delete(inbox);
            foreach (var command in commands) Server.ExecuteCommand(command);
        }, TimerFlags.REPEAT);
        AddCommand("test_dump", "Local fixture state", (caller, command) => {
            foreach (var p in Utilities.GetPlayers()) {
                var pawn = p.PlayerPawn.Value;
                Console.WriteLine($"[TEST] slot={p.Slot} name={p.PlayerName} team={p.TeamNum} alive={p.PawnIsAlive} hp={pawn?.Health} pawn={p.PlayerPawn.Raw} pos={pawn?.AbsOrigin} render={pawn?.Render} mode={pawn?.RenderMode}");
                var w = pawn?.WeaponServices?.ActiveWeapon.Value;
                Console.WriteLine($"[TEST WEAPON] slot={p.Slot} name={w?.DesignerName} primary={w?.NextPrimaryAttackTick} secondary={w?.NextSecondaryAttackTick} tick={Server.TickCount} buttons={p.Buttons}");
            }
        });
        AddCommand("test_glow", "Dump glow entity state", (caller, command) => {
            foreach (var e in Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic")) {
                if(e.Glow.GlowType != 3) continue;
                Console.WriteLine($"[TEST GLOW] handle={e.EntityHandle.Raw} color={e.Glow.GlowColorOverride.ToArgb():X8} range={e.Glow.GlowRange} network={e.LastNetworkChange}");
            }
        });
        AddCommand("test_hp", "Set fixture health: slot health", (caller, command) => {
            var p = Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            if (p?.PlayerPawn.Value is not { } pawn) return;
            pawn.Health = int.Parse(command.GetArg(2));
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        });
        AddCommand("test_pos", "Teleport: slot x y z yaw", (caller, command) => {
            var p = Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            p?.PlayerPawn.Value?.Teleport(new Vector(float.Parse(command.GetArg(2)),float.Parse(command.GetArg(3)),float.Parse(command.GetArg(4))), new QAngle(0,float.Parse(command.GetArg(5)),0),new Vector(0,0,0));
        });
        AddCommand("test_give", "Give weapon: slot designerName", (caller, command) => {
            Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)))?.GiveNamedItem(command.GetArg(2));
        });
        AddCommand("test_equip", "Fixture equip: slot designerName", (caller, command) => {
            var pawn = Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)))?.PlayerPawn.Value;
            if (pawn?.WeaponServices is not { } services) return;
            var weapon = services.MyWeapons.Select(h=>h.Value).FirstOrDefault(w=>w?.DesignerName==command.GetArg(2));
            if(weapon==null) return;
            services.ActiveWeapon.Raw = weapon.EntityHandle.Raw;
            weapon.NextPrimaryAttackTick=Server.TickCount;
            weapon.NextSecondaryAttackTick=Server.TickCount;
        });
        RegisterListener<Listeners.OnPlayerTakeDamagePost>((pawn, damage, result) => {
            Console.WriteLine($"[TEST DAMAGE] tick={Server.TickCount} victim={pawn.Index} attacker={damage.Attacker.Value?.DesignerName}:{damage.Attacker.Raw} inflictor={damage.Inflictor.Value?.DesignerName}:{damage.Inflictor.Raw} ability={damage.Ability.Value?.DesignerName}:{damage.Ability.Raw} result={result.Handle}");
        });
        RegisterListener<Listeners.OnEntitySpawned>(entity => {
            if (!entity.DesignerName.Contains("projectile") && entity.DesignerName != "inferno") return;
            Server.NextFrame(() => {
                if (!entity.IsValid) return;
                var b = entity.As<CBaseEntity>();
                Console.WriteLine($"[TEST ENTITY] {entity.DesignerName}:{entity.EntityHandle.Raw} owner={b.OwnerEntity.Raw} position={b.AbsOrigin}");
                if (entity.DesignerName.Contains("projectile")) Console.WriteLine($"[TEST THROWER] {entity.As<CBaseGrenade>().Thrower.Raw}");
                if(entity.DesignerName=="inferno" && EnterFire is { } enter && b.AbsOrigin is { } pos) {
                    EnterFire=null;
                    var destination=new Vector(pos.X,pos.Y,pos.Z+2);
                    AddTimer(enter.Delay,()=> {
                        Utilities.GetPlayerFromSlot(enter.Slot)?.PlayerPawn.Value?.Teleport(destination,new QAngle(0,0,0),new Vector(0,0,0));
                        Console.WriteLine($"[TEST ENTER FIRE] tick={Server.TickCount} slot={enter.Slot}");
                    });
                }
            });
        });
        RegisterEventHandler<EventWeaponFire>((e,i) => { Console.WriteLine($"[TEST FIRE] tick={Server.TickCount} slot={e.Userid?.Slot} weapon={e.Weapon}"); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerHurt>((e,i) => { Console.WriteLine($"[TEST HURT] tick={Server.TickCount} attacker={e.Attacker?.Slot} victim={e.Userid?.Slot} weapon={e.Weapon} hp={e.Health} damage={e.DmgHealth}"); return HookResult.Continue; });
        RegisterListener<Listeners.OnPlayerButtonsChanged>((p,pressed,released) => {
            if ((pressed & (PlayerButtons.Attack | PlayerButtons.Attack2)) != 0) Console.WriteLine($"[TEST BUTTON] tick={Server.TickCount} slot={p.Slot} pressed={pressed}");
        });
    }
}


