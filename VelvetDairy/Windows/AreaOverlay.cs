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

    /// <summary>A small green dot just above head height over a stall, so you can see which seats milk you.</summary>
    public static void DrawStall(Stall s)
    {
        // A seated character's head is about 1.4 y above the seat
        if (!Plugin.GameGui.WorldToScreen(new Vector3(s.X, s.Y + 1.4f, s.Z), out var c)) return;
        c -= new Vector2(0, 12);
        var dl = ImGui.GetBackgroundDrawList();
        const float r = 6f;
        dl.AddCircleFilled(c + new Vector2(0, 1.5f), r + 2f, ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.06f, 0.06f, 0.55f)));
        dl.AddCircleFilled(c, r, ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.85f, 0.4f, 0.95f)));
        dl.AddCircle(c, r, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.8f)), 0, 1.2f);
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
