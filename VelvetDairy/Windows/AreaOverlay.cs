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
    /// A small round badge floating over the head of whoever sits on a stall (at head height over an empty one).
    /// Green; it pulses gently while someone sits there, drains like a gauge while it milks them (<paramref name="fill"/> =
    /// milk left of what they started with), turns light red while they doze and flashes red while they're asleep.
    /// </summary>
    public static void DrawStall(Stall s, StallLook look, float fill)
    {
        // A seated character's head is about 1.6 y above the seat
        if (!Plugin.GameGui.WorldToScreen(new Vector3(s.X, s.Y + 1.6f, s.Z), out var c)) return;
        var dl = ImGui.GetBackgroundDrawList();
        const float r = 10f;
        c -= new Vector2(0, r + 6);   // sit just above the head, not on it

        var t = (float)(System.Environment.TickCount64 / 1000.0);
        var (col, a) = look switch
        {
            StallLook.Sitting => (Green, 0.55f + 0.4f * (0.5f + 0.5f * System.MathF.Sin(t * 2.2f))),   // a slow breath
            StallLook.Milking => (Green, 0.95f),
            StallLook.Dozing => (LightRed, 0.9f),
            StallLook.Asleep => (Red, System.MathF.Sin(t * 6f) > 0 ? 1f : 0.25f),                        // flashing
            _ => (Green, 0.6f),
        };
        uint U(Vector4 v, float alpha) => ImGui.ColorConvertFloat4ToU32(v with { W = alpha });

        // A dark backing so it reads on any background, then the color
        dl.AddCircleFilled(c, r + 2.5f, U(new Vector4(0.08f, 0.06f, 0.06f, 1f), 0.55f));
        if (look == StallLook.Milking)
        {
            // Drains from the top down as the milk comes out
            fill = System.Math.Clamp(fill, 0f, 1f);
            dl.AddCircleFilled(c, r, U(col, 0.18f));
            var top = c.Y + r - 2 * r * fill;
            dl.PushClipRect(new Vector2(c.X - r - 1, top), new Vector2(c.X + r + 1, c.Y + r + 1), true);
            dl.AddCircleFilled(c, r, U(col, a));
            dl.PopClipRect();
        }
        else dl.AddCircleFilled(c, r, U(col, a));

        dl.AddCircle(c, r, U(col, 1f), 0, 1.8f);
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
