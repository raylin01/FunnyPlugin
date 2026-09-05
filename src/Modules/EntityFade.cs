using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace Funnies.Modules;

public static class EntityFade
{
    private sealed record Original(CBaseModelEntity Entity, Color Color, RenderMode_t Mode, float Shadow);
    private static readonly Dictionary<uint, Original> Originals = [];
    private static readonly HashSet<uint> Seen = [];
    public static void Begin() => Seen.Clear();

    public static void Apply(CBaseModelEntity entity, int alpha)
    {
        // Wallhack relays follow the pawn but have their own rendering contract.
        if (Globals.GlowData.Values.Any(g => g.GlowEnt.Handle == entity.Handle || g.ModelRelay.Handle == entity.Handle)) return;
        var raw = entity.EntityHandle.Raw;
        Seen.Add(raw);
        if (!Originals.TryGetValue(raw, out var original))
            Originals[raw] = original = new(entity, entity.Render, entity.RenderMode, entity.ShadowStrength);
        Set(entity, Color.FromArgb(Math.Clamp(alpha, 0, 255), original.Color),
            alpha == 255 ? original.Mode : RenderMode_t.kRenderTransAlpha, original.Shadow * alpha / 255f);
    }

    private static void Set(CBaseModelEntity entity, Color color, RenderMode_t mode, float shadow)
    {
        if (entity.Render.ToArgb() != color.ToArgb()) {
            entity.Render = color; Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
        }
        if (entity.RenderMode != mode) {
            entity.RenderMode = mode; Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_nRenderMode");
        }
        if (entity.ShadowStrength != shadow) {
            entity.ShadowStrength = shadow; Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_flShadowStrength");
        }
    }

    public static void End()
    {
        foreach (var (raw, original) in Originals.ToArray())
        {
            if (Seen.Contains(raw)) continue;
            if (original.Entity.IsValid && original.Entity.EntityHandle.Raw == raw)
                Set(original.Entity, original.Color, original.Mode, original.Shadow);
            Originals.Remove(raw);
        }
    }

    public static void Restore() { Seen.Clear(); End(); }
    public static void Clear() { Seen.Clear(); Originals.Clear(); }
}
