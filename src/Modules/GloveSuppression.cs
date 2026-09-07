using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace Funnies.Modules;

public static class GloveSuppression
{
    private sealed record Saved(CCSPlayerPawn Pawn, ushort Definition, bool Initialized);
    private static readonly Dictionary<uint, Saved> Originals = [];
    private static bool Warned;

    public static void Setup() => Globals.Plugin.RegisterListener<Listeners.OnMapStart>(_ => Originals.Clear());

    private static void Update(CCSPlayerPawn pawn, ushort definition, bool initialized)
    {
        pawn.EconGloves.ItemDefinitionIndex = definition;
        pawn.EconGloves.Initialized = initialized;
        // This is an embedded item view. Its base offset is not a networked leaf.
        foreach (var field in new[] { "m_iItemDefinitionIndex", "m_bInitialized" })
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_EconGloves", Schema.GetSchemaOffset("CEconItemView", field));
        pawn.EconGlovesChanged++;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_nEconGlovesChanged");
        pawn.AcceptInput("SetBodygroup", value: $"first_or_third_person,{(definition != 0 && initialized ? 1 : 0)}");
    }

    public static void OnTick()
    {
        if (CoreConfig.FollowCS2ServerGuidelines) {
            if (Globals.Config.DisableGloveSkinsServerWide && !Warned) {
                Console.WriteLine("[Funnies] Glove suppression requires FollowCS2ServerGuidelines=false; it is not active.");
                Warned = true;
            }
            return;
        }
        Warned = false;
        if (!Globals.Config.DisableGloveSkinsServerWide) { Restore(); return; }
        foreach (var (raw, saved) in Originals.ToArray())
            if (!saved.Pawn.IsValid || saved.Pawn.EntityHandle.Raw != raw) Originals.Remove(raw);
        foreach (var player in Util.GetValidPlayers()) {
            if (player.PlayerPawn.Value is not { IsValid: true } pawn) continue;
            var item = pawn.EconGloves;
            if (item.ItemDefinitionIndex == 0 && !item.Initialized) {
                // Cosmetic plugins may issue a delayed hide-default-gloves bodygroup update.
                if (Originals.ContainsKey(pawn.EntityHandle.Raw) &&
                    pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.MeshGroupMask == 2)
                    pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,0");
                continue;
            }
            // Retain attributes and item IDs; restore the latest cosmetic applied by the game/another plugin.
            Originals[pawn.EntityHandle.Raw] = new(pawn, item.ItemDefinitionIndex, item.Initialized);
            Update(pawn, 0, false);
        }
    }

    public static void Restore()
    {
        if (CoreConfig.FollowCS2ServerGuidelines) return;
        foreach (var (raw, saved) in Originals) {
            if (!saved.Pawn.IsValid || saved.Pawn.EntityHandle.Raw != raw) continue;
            var item = saved.Pawn.EconGloves;
            if (item.ItemDefinitionIndex == 0 && !item.Initialized)
                Update(saved.Pawn, saved.Definition, saved.Initialized);
        }
        Originals.Clear();
    }
}
