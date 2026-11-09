using System.Numerics;
using Dalamud.Bindings.ImGui;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>Draws a dairy's box on the ground while the owner is marking it out.</summary>
public static class AreaOverlay
{
    private static readonly uint Edge = ImGui.ColorConvertFloat4ToU32(new Vector4(0.98f, 0.62f, 0.72f, 0.95f));
    private static readonly uint Front = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.82f, 1f, 1f));
    private static readonly uint Post = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.85f, 0.9f));

    public static void Draw(DairyArea a)
    {
        var dl = ImGui.GetBackgroundDrawList();
        var c = a.Corners();
        for (var i = 0; i < 4; i++) Line(dl, c[i], c[(i + 1) % 4], i == 3 ? Front : Edge, 3f);   // c[3] -> c[0] is the front
        foreach (var p in c) Line(dl, p, p + new Vector3(0, 2.5f, 0), Post, 2.5f);
        if (Plugin.GameGui.WorldToScreen(a.Center, out var mid)) dl.AddCircleFilled(mid, 5f, Front);
    }

    // Stall tier colors: plain, wooden, padded, velvet, gilded
    private static readonly Vector4[] TierColor =
    {
        new(1f, 1f, 1f, 0.9f), new(0.85f, 0.7f, 0.5f, 0.9f), new(0.55f, 0.82f, 1f, 0.95f), new(0.98f, 0.5f, 0.72f, 0.95f), new(1f, 0.84f, 0.35f, 1f),
    };

    /// <summary>A small box on the ground around a stall's seat, in its tier color. Brighter when you're on it; a dot on top while it's freshly cleaned.</summary>
    public static void DrawStall(Stall s, bool onIt, bool fresh)
    {
        var dl = ImGui.GetBackgroundDrawList();
        var col = TierColor[System.Math.Clamp(s.Tier, 0, 4)];
        var edge = ImGui.ColorConvertFloat4ToU32(onIt ? col : col with { W = col.W * 0.7f });
        var c = new Vector3(s.X, s.Y, s.Z);
        const float r = 0.9f;
        var pts = new[] { c + new Vector3(-r, 0, -r), c + new Vector3(r, 0, -r), c + new Vector3(r, 0, r), c + new Vector3(-r, 0, r) };
        for (var i = 0; i < 4; i++)
        {
            Line(dl, pts[i], pts[(i + 1) % 4], edge, onIt ? 3f : 2f);
            Line(dl, pts[i], pts[i] + new Vector3(0, 1.2f, 0), edge, 1.5f);
        }

        if (Plugin.GameGui.WorldToScreen(c + new Vector3(0, 1.6f, 0), out var top))
        {
            if (fresh) dl.AddCircleFilled(top, 4f, ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 0.9f, 0.6f, 1f)));
            var label = s.Name;
            var size = ImGui.CalcTextSize(label);
            dl.AddText(top - new Vector2(size.X / 2, size.Y + 6), edge, label);
        }
    }

    /// <summary>A world-space line split into short pieces so it follows the view even when one end is off screen.</summary>
    private static void Line(ImDrawListPtr dl, Vector3 from, Vector3 to, uint color, float thickness)
    {
        const int steps = 20;
        Vector2? prev = null;
        for (var k = 0; k <= steps; k++)
        {
            var p = Vector3.Lerp(from, to, k / (float)steps);
            if (Plugin.GameGui.WorldToScreen(p, out var s))
            {
                if (prev is { } q) dl.AddLine(q, s, color, thickness);
                prev = s;
            }
            else prev = null;
        }
    }
}
