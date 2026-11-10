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

    private static readonly Vector4 Green = new(0.25f, 0.85f, 0.4f, 1f);
    private static readonly Vector4 LightRed = new(1f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 Red = new(0.92f, 0.12f, 0.18f, 1f);

    public enum StallLook { Idle, Sitting, Milking, Dozing, Asleep }

    /// <summary>
    /// A small square flat on the ground under a stall's seat. Green; it pulses gently while you sit on it, drains like a
    /// gauge while it milks you (<paramref name="fill"/> = milk left of what you started with), turns light red while
    /// you doze on it and flashes red while you're asleep on it.
    /// </summary>
    public static void DrawStall(Stall s, StallLook look, float fill)
    {
        var dl = ImGui.GetBackgroundDrawList();
        var c = new Vector3(s.X, s.Y, s.Z);
        const float r = 0.45f;
        var world = new[] { c + new Vector3(-r, 0, -r), c + new Vector3(r, 0, -r), c + new Vector3(r, 0, r), c + new Vector3(-r, 0, r) };
        var pts = new Vector2[4];
        for (var i = 0; i < 4; i++)
            if (!Plugin.GameGui.WorldToScreen(world[i], out pts[i])) return;   // only drawn when the whole square is in view

        var t = (float)(System.Environment.TickCount64 / 1000.0);
        var (col, fillA, edgeA) = look switch
        {
            StallLook.Sitting => (Green, 0.3f + 0.2f * (0.5f + 0.5f * System.MathF.Sin(t * 2.2f)), 0.95f),       // a slow breath
            StallLook.Milking => (Green, 0.55f, 1f),
            StallLook.Dozing => (LightRed, 0.4f, 0.9f),
            StallLook.Asleep => System.MathF.Sin(t * 6f) > 0 ? (Red, 0.6f, 1f) : (Red, 0.12f, 0.5f),             // flashing
            _ => (Green, 0.3f, 0.8f),
        };

        var edge = ImGui.ColorConvertFloat4ToU32(col with { W = edgeA });
        if (look == StallLook.Milking)
        {
            // The green drains toward the near edge as the milk comes out; what's left of the square stays faint
            fill = System.Math.Clamp(fill, 0f, 1f);
            dl.AddQuadFilled(pts[0], pts[1], pts[2], pts[3], ImGui.ColorConvertFloat4ToU32(col with { W = 0.12f }));
            var near0 = Plugin.GameGui.WorldToScreen(world[0], out var a0) ? a0 : pts[0];
            var near1 = Plugin.GameGui.WorldToScreen(world[1], out var a1) ? a1 : pts[1];
            if (Plugin.GameGui.WorldToScreen(Vector3.Lerp(world[1], world[2], fill), out var b1) &&
                Plugin.GameGui.WorldToScreen(Vector3.Lerp(world[0], world[3], fill), out var b0) && fill > 0.001f)
                dl.AddQuadFilled(near0, near1, b1, b0, ImGui.ColorConvertFloat4ToU32(col with { W = fillA }));
        }
        else dl.AddQuadFilled(pts[0], pts[1], pts[2], pts[3], ImGui.ColorConvertFloat4ToU32(col with { W = fillA }));

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
