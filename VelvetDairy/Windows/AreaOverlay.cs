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

    private static readonly Vector4 StallRed = new(0.9f, 0.15f, 0.22f, 1f);

    /// <summary>A small red square flat on the ground under a stall's seat. A little brighter while you're on it.</summary>
    public static void DrawStall(Stall s, bool onIt, bool fresh)
    {
        var dl = ImGui.GetBackgroundDrawList();
        var c = new Vector3(s.X, s.Y, s.Z);
        const float r = 0.45f;
        var world = new[] { c + new Vector3(-r, 0, -r), c + new Vector3(r, 0, -r), c + new Vector3(r, 0, r), c + new Vector3(-r, 0, r) };
        var pts = new Vector2[4];
        for (var i = 0; i < 4; i++)
            if (!Plugin.GameGui.WorldToScreen(world[i], out pts[i])) return;   // only drawn when the whole square is in view
        var fill = ImGui.ColorConvertFloat4ToU32(StallRed with { W = onIt ? 0.55f : 0.35f });
        var edge = ImGui.ColorConvertFloat4ToU32(StallRed with { W = onIt ? 1f : 0.8f });
        dl.AddQuadFilled(pts[0], pts[1], pts[2], pts[3], fill);
        dl.AddQuad(pts[0], pts[1], pts[2], pts[3], edge, 2f);
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
