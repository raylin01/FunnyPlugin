using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;

public partial class TestDriver
{
    private sealed record PrivateGlove(CCSPlayerPawn Pawn, CEconWearable Glove, ushort Definition, bool Initialized, ulong Mesh);
    private readonly Dictionary<int, PrivateGlove> PrivateGloves = [];
    private readonly Dictionary<(int Viewer, int Owner), bool> PrivateVisibility = [];

    private static void NotifyGloveFields(CCSPlayerPawn pawn)
    {
        // The item view is embedded: mark its leaf fields, not the struct's base offset.
        foreach(var field in new[]{"m_iItemDefinitionIndex","m_bInitialized"})
            Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_EconGloves",Schema.GetSchemaOffset("CEconItemView",field));
        pawn.EconGlovesChanged++;
        Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_nEconGlovesChanged");
    }

    private void RestorePrivateGloves()
    {
        foreach(var fixture in PrivateGloves.Values) {
            if(fixture.Glove.IsValid) fixture.Glove.Remove();
            if(!fixture.Pawn.IsValid) continue;
            fixture.Pawn.EconGloves.ItemDefinitionIndex=fixture.Definition;
            fixture.Pawn.EconGloves.Initialized=fixture.Initialized;
            NotifyGloveFields(fixture.Pawn);
            fixture.Pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.MeshGroupMask=fixture.Mesh;
            Utilities.SetStateChanged(fixture.Pawn,"CCSPlayerPawn","m_nEconGlovesChanged");
        }
        PrivateGloves.Clear();
        PrivateVisibility.Clear();
    }

    private void SetupPrivateGloves()
    {
        AddCommand("test_private_remove_models", "Remove private models without restoring pawn cosmetics", (caller, command) => {
            if(caller!=null) return;
            foreach(var fixture in PrivateGloves.Values)
                if(fixture.Glove.IsValid) fixture.Glove.Remove();
        });
        AddCommand("test_regular_gloves", "Normal Vice cosmetic baseline: slot", (caller, command) => {
            if(caller!=null) return;
            var p=Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            if(p?.PlayerPawn.Value is not { } pawn || !p.PawnIsAlive) return;
            Paint(pawn.EconGloves,5030,10048);
            pawn.EconGlovesChanged++;
            Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_nEconGlovesChanged");
            pawn.AcceptInput("SetBodygroup",value:"first_or_third_person,0");
            AddTimer(0.2f,()=> {
                if(pawn.IsValid) pawn.AcceptInput("SetBodygroup",value:"first_or_third_person,1");
            },CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        });
        AddCommand("test_spectate", "Spectate target: viewer slot, target slot", (caller, command) => {
            if(caller!=null) return;
            var viewer=Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(1)));
            var target=Utilities.GetPlayerFromSlot(int.Parse(command.GetArg(2)));
            if(viewer==null || target?.PawnIsAlive!=true) return;
            if(viewer.Team!=CsTeam.Spectator) viewer.ChangeTeam(CsTeam.Spectator);
            AddTimer(0.3f,()=> {
                if(!viewer.IsValid || !target.IsValid) return;
                viewer.ExecuteClientCommandFromServer("spec_mode 2");
                var observer=viewer.Pawn.Value?.ObserverServices;
                if(observer!=null) {
                    observer.ObserverMode=(byte)ObserverMode_t.OBS_MODE_IN_EYE;
                    observer.ObserverTarget.Raw=target.PlayerPawn.Raw;
                    foreach(var field in new[]{"m_iObserverMode","m_hObserverTarget"})
                        NativeAPI.SchemaNetworkStateChanged(observer.__m_pChainEntity.Handle,(uint)Schema.GetSchemaOffset("CPlayer_ObserverServices",field),uint.MaxValue,uint.MaxValue);
                }
                Console.WriteLine($"[TEST SPECTATE] viewer={viewer.Slot} target={target.Slot} mode={viewer.Pawn.Value?.ObserverServices?.ObserverMode} actualTarget={viewer.Pawn.Value?.ObserverServices?.ObserverTarget.Raw}");
            });
        });
        AddCommand("test_private_clear", "Restore owner-only glove fixtures", (caller, command) => {
            if(caller==null) RestorePrivateGloves();
        });
        AddCommand("test_private_gloves", "Owner-only Vice glove prototype: player slot", (caller, command) => {
            if(caller!=null) return;
            var slot=int.Parse(command.GetArg(1));
            var p=Utilities.GetPlayerFromSlot(slot);
            if(p?.PlayerPawn.Value is not { } pawn || !p.PawnIsAlive || PrivateGloves.ContainsKey(slot)) return;
            var glove=Utilities.CreateEntityByName<CEconWearable>("wearable_item");
            if(glove?.IsValid!=true) return;
            var model=pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState;
            PrivateGloves[slot]=new(pawn,glove,pawn.EconGloves.ItemDefinitionIndex,pawn.EconGloves.Initialized,model.MeshGroupMask);
            glove.AlwaysAllow=true;
            glove.Render=Color.White;
            glove.RenderMode=RenderMode_t.kRenderNormal;
            glove.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(1u<<2);
            Paint(glove.AttributeManager.Item,5030,10048);
            glove.FallbackPaintKit=10048; glove.FallbackSeed=1; glove.FallbackWear=0.01f;
            glove.SetModel("agents/models/shared/arms/glove_sporty/glove_sporty.vmdl");
            glove.OwnerEntity.Raw=pawn.EntityHandle.Raw;
            glove.DispatchSpawn();
            glove.Teleport(pawn.AbsOrigin,pawn.AbsRotation,new Vector(0,0,0));
            glove.AcceptInput("FollowEntity",pawn,null,"!activator");
            glove.AcceptInput("SetBodygroup",value:"first_or_third_person,1");
            pawn.EconGloves.ItemDefinitionIndex=0;
            pawn.EconGloves.Initialized=false;
            NotifyGloveFields(pawn);
            pawn.AcceptInput("SetBodygroup",value:"first_or_third_person,0");
            Console.WriteLine($"[PRIVATE GLOVE] owner={slot} pawn={pawn.EntityHandle.Raw} glove={glove.EntityHandle.Raw}");
        });
        RegisterListener<Listeners.CheckTransmit>(list => {
            foreach(var (info,viewer) in list) {
                if(viewer==null) continue;
                var observer=viewer.Pawn.Value?.ObserverServices;
                foreach(var (slot,fixture) in PrivateGloves) {
                    if(!fixture.Glove.IsValid || !fixture.Pawn.IsValid) continue;
                    var ownView=viewer.Slot==slot || (!viewer.PawnIsAlive &&
                        observer?.ObserverMode==(byte)ObserverMode_t.OBS_MODE_IN_EYE &&
                        observer.ObserverTarget.Raw==fixture.Pawn.EntityHandle.Raw);
                    if(ownView) info.TransmitEntities.Add(fixture.Glove);
                    else info.TransmitEntities.Remove(fixture.Glove);
                    var key=(viewer.Slot,slot);
                    if(!PrivateVisibility.TryGetValue(key,out var old) || old!=ownView) {
                        PrivateVisibility[key]=ownView;
                        Console.WriteLine($"[PRIVATE TRANSMIT] viewer={viewer.Slot} owner={slot} visible={ownView}");
                    }
                }
            }
        });
    }
}
