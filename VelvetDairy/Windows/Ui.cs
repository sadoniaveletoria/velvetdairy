using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace VelvetDairy.Windows;

/// <summary>Game fonts. Jupiter is FFXIV's serif (fits the creamery look); TrumpGothic is used for small caps labels.</summary>
public sealed class Fonts : IDisposable
{
    public IFontHandle Title { get; }     // wordmark and big letters (TrumpGothic: big Jupiter lacks glyphs)
    public IFontHandle Heading { get; }   // card titles
    public IFontHandle Small { get; }     // small serif
    public IFontHandle Label { get; }     // small caps labels and buttons
    public IFontHandle Row { get; }       // list entry titles

    // Jupiter has no "0" glyph, so text with numbers in it is drawn with Axis (the game's UI font) instead
    private readonly IFontHandle numTitle;
    private readonly IFontHandle numHeading;
    private readonly IFontHandle numSmall;

    public Fonts()
    {
        var atlas = Plugin.PluginInterface.UiBuilder.FontAtlas;
        Title = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic34)); // big Jupiter is missing most letters
        Heading = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Jupiter23));
        Small = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Jupiter16));
        Label = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic23));
        Row = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Jupiter20));
        numTitle = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis36));
        numHeading = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis18));
        numSmall = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis14));
    }

    /// <summary>TrumpGothic (Label) has no middle dot, arrow or ellipsis; it draws them as "tt". Swap them for plain characters.</summary>
    public string Clean(IFontHandle? font, string text) =>
        font == Label ? text.Replace(" · ", " - ").Replace("·", "-").Replace("→", "-").Replace("…", "...") : text;

    /// <summary>The font to actually draw with: swaps a Jupiter font for Axis when the text has digits.</summary>
    public IFontHandle? For(IFontHandle? font, string text)
    {
        if (font == null || !HasDigit(text)) return font;
        if (font == Title) return numTitle;
        if (font == Heading || font == Row) return numHeading;
        if (font == Small) return numSmall;
        return font;
    }

    private static bool HasDigit(string text)
    {
        foreach (var ch in text)
            if (ch is >= '0' and <= '9') return true;
        return false;
    }

    public void Dispose()
    {
        Title.Dispose();
        Heading.Dispose();
        Small.Dispose();
        Label.Dispose();
        Row.Dispose();
        numTitle.Dispose();
        numHeading.Dispose();
        numSmall.Dispose();
    }
}

/// <summary>Boutique creamery palette: warm cream, dusty rose, powder blue, sage, honey.</summary>
public static class Theme
{
    public static readonly Vector4 Paper = new(0.985f, 0.968f, 0.935f, 1f);
    public static readonly Vector4 Cream = new(0.955f, 0.925f, 0.875f, 1f);
    public static readonly Vector4 Linen = new(0.915f, 0.880f, 0.825f, 1f);
    public static readonly Vector4 Ink = new(0.36f, 0.23f, 0.26f, 1f);
    public static readonly Vector4 InkSoft = new(0.55f, 0.44f, 0.45f, 1f);
    public static readonly Vector4 Rose = new(0.87f, 0.67f, 0.69f, 1f);
    public static readonly Vector4 RoseDeep = new(0.66f, 0.38f, 0.44f, 1f);
    public static readonly Vector4 Blue = new(0.70f, 0.78f, 0.855f, 1f);
    public static readonly Vector4 BlueDeep = new(0.38f, 0.50f, 0.635f, 1f);
    public static readonly Vector4 Sage = new(0.72f, 0.80f, 0.705f, 1f);
    public static readonly Vector4 SageDeep = new(0.415f, 0.54f, 0.43f, 1f);
    public static readonly Vector4 Honey = new(0.895f, 0.80f, 0.61f, 1f);
    public static readonly Vector4 HoneyDeep = new(0.69f, 0.54f, 0.305f, 1f);
    public static readonly Vector4 Mauve = new(0.78f, 0.70f, 0.77f, 1f);
    public static readonly Vector4 Stone = new(0.84f, 0.815f, 0.785f, 1f);
    public static readonly Vector4 StoneDeep = new(0.54f, 0.51f, 0.49f, 1f);
    public static readonly Vector4 Warn = new(0.74f, 0.32f, 0.30f, 1f);
    public static readonly Vector4 Shadow = new(0.35f, 0.22f, 0.22f, 0.18f);

    public static Vector4 Alpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, c.W * a);
    public static Vector4 Lighten(Vector4 c, float k) => new(c.X + (1 - c.X) * k, c.Y + (1 - c.Y) * k, c.Z + (1 - c.Z) * k, c.W);
    public static Vector4 Darken(Vector4 c, float k) => new(c.X * (1 - k), c.Y * (1 - k), c.Z * (1 - k), c.W);

    /// <summary>Gauge colour: calm when fine, rose when getting low, red when urgent.</summary>
    public static Vector4 NeedColor(float v, Vector4 ok) => v >= 40 ? ok : v >= 20 ? RoseDeep : Warn;

    public static Vector4 GradeColor(int g) => g switch
    {
        0 => StoneDeep,
        1 => SageDeep,
        2 => BlueDeep,
        3 => RoseDeep,
        _ => HoneyDeep,
    };
}

/// <summary>Drawing helpers shared by the main window and the HUD.</summary>
public static class Ui
{
    public static Art? Art { get; set; }
    public static Fonts? Fonts { get; set; }

    public static uint U(Vector4 c) => ImGui.GetColorU32(c);
    public static uint U(Vector4 c, float alpha) => ImGui.GetColorU32(Theme.Alpha(c, alpha));
    public static float EaseOut(float t) => 1f - MathF.Pow(1f - Math.Clamp(t, 0f, 1f), 3f);

    /// <summary>Draws text aligned around pos (0,0 = top-left, 0.5,0.5 = centered). Returns its size.</summary>
    public static Vector2 Text(ImDrawListPtr dl, IFontHandle? font, string text, Vector2 pos, Vector4 col, float alignX = 0f, float alignY = 0f)
    {
        text = Fonts?.Clean(font, text) ?? text;
        font = Fonts?.For(font, text) ?? font;
        IDisposable? pushed = font?.Push();
        try
        {
            var size = ImGui.CalcTextSize(text);
            var p = new Vector2(MathF.Round(pos.X - size.X * alignX), MathF.Round(pos.Y - size.Y * alignY));
            dl.AddText(p, U(col), text);
            return size;
        }
        finally
        {
            pushed?.Dispose();
        }
    }

    public static Vector2 Measure(IFontHandle? font, string text)
    {
        text = Fonts?.Clean(font, text) ?? text;
        font = Fonts?.For(font, text) ?? font;
        IDisposable? pushed = font?.Push();
        try { return ImGui.CalcTextSize(text); }
        finally { pushed?.Dispose(); }
    }

    /// <summary>Text that wraps inside a width, drawn at the cursor. Returns the height used.</summary>
    public static void Wrapped(string text, Vector4 col, float width)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(col, text);
        ImGui.PopTextWrapPos();
    }

    // ---------------------------------------------------------------- surfaces

    /// <summary>Cream card with a fine double rule.</summary>
    public static void Card(ImDrawListPtr dl, Vector2 min, Vector2 max, bool blue = false)
    {
        var tex = Art?.Card(blue);
        if (tex != null)
        {
            Windows.Art.NineSlice(dl, tex, min, max, 48f, 34f);
            return;
        }

        dl.AddRectFilled(min + new Vector2(0, 3), max + new Vector2(0, 3), U(Theme.Shadow), 14);
        dl.AddRectFilled(min, max, U(Theme.Paper), 14);
        dl.AddRect(min, max, U(blue ? Theme.BlueDeep : Theme.RoseDeep, 0.5f), 14, ImDrawFlags.None, 1.5f);
        dl.AddRect(min + new Vector2(9, 9), max - new Vector2(9, 9), U(blue ? Theme.BlueDeep : Theme.RoseDeep, 0.35f), 8, ImDrawFlags.None, 1f);
    }

    /// <summary>Small-caps label with a hairline after it, like a menu board heading.</summary>
    public static void Eyebrow(ImDrawListPtr dl, string text, Vector2 pos, float width, Vector4? color = null)
    {
        var c = color ?? Theme.RoseDeep;
        var size = Text(dl, Fonts?.Label, text.ToUpperInvariant(), pos, c);
        var y = pos.Y + size.Y * 0.55f;
        if (width > size.X + 12) dl.AddLine(new Vector2(pos.X + size.X + 10, y), new Vector2(pos.X + width, y), U(c, 0.35f), 1f);
    }

    public static void Icon(ImDrawListPtr dl, string name, Vector2 center, float radius, float alpha = 1f)
    {
        var tex = Art?.Icon(name);
        if (tex == null)
        {
            dl.AddCircleFilled(center, radius, U(Theme.Cream, alpha));
            dl.AddCircle(center, radius, U(Theme.RoseDeep, 0.7f * alpha), 0, 1.5f);
            return;
        }

        var tint = ImGui.GetColorU32(new Vector4(1, 1, 1, alpha));
        dl.AddImage(tex.Handle, center - new Vector2(radius, radius), center + new Vector2(radius, radius), Vector2.Zero, Vector2.One, tint);
    }

    public static void Avatar(ImDrawListPtr dl, string name, Vector2 center, float radius)
    {
        var tex = Art?.Avatar(name);
        if (tex == null)
        {
            dl.AddCircleFilled(center, radius, U(Theme.Rose));
            return;
        }

        dl.AddImage(tex.Handle, center - new Vector2(radius, radius), center + new Vector2(radius, radius));
    }

    /// <summary>Round seal with a grade letter.</summary>
    public static void Seal(ImDrawListPtr dl, Vector2 center, float radius, string letter, Vector4 ink)
    {
        var tex = Art?.Seal;
        if (tex != null) dl.AddImage(tex.Handle, center - new Vector2(radius, radius), center + new Vector2(radius, radius));
        else
        {
            dl.AddCircleFilled(center, radius, U(Theme.Rose));
            dl.AddCircleFilled(center, radius * 0.78f, U(Theme.Paper));
        }

        var font = radius > 34 ? Fonts?.Title : Fonts?.Heading;
        Text(dl, font, letter, center + new Vector2(0, 1), ink, 0.5f, 0.5f);
    }

    public enum Flow { Still, Filling, Draining }

    /// <summary>
    /// The milk bottle gauge: glass, milk cropped to the fill level with a moving surface, then label and highlights on top.
    /// While filling, drops of milk fall in and splash; while draining, bubbles rise and the surface sloshes.
    /// </summary>
    /// <summary>
    /// Where milk sits in each bottle image (fractions of the image): how high it reaches when full, the bottom,
    /// where drops fall in, and the inside width of the body.
    /// </summary>
    private sealed record BottleStyle(float Top, float Bottom, float NeckY, float NeckX, bool Drops, float InX0, float InX1, bool Round = false);

    private static readonly BottleStyle[] Styles =
    {
        new(0.20f, 0.96f, 0.12f, 0.5f, true, 0.12f, 0.88f),           // 0 (unused)
        new(0.20f, 0.96f, 0.12f, 0.5f, true, 0.12f, 0.88f),           // 1 Quart
        new(0.27f, 0.96f, 0.19f, 0.5f, true, 0.08f, 0.92f),           // 2 Half gallon
        new(0.21f, 0.96f, 0.12f, 0.405f, true, 0.07f, 0.74f),         // 3 Gallon
        new(0.17f, 0.96f, 0.13f, 0.28f, true, 0.06f, 0.94f),          // 4 Big jug (big milk jug, cap on the left)
        new(0.444f, 0.902f, 0f, 0.5f, false, 0.41f, 0.59f),           // 5 Milk churn (milk shows in the window)
    };

    private static BottleStyle StyleFor(int tier) => Styles[Math.Clamp(tier, 1, 5)];

    /// <summary>Inside width of the bottle at a height (fractions), for the surface line.</summary>
    private static (float X0, float X1) InsideAt(BottleStyle st, float y)
    {
        if (!st.Round) return (st.InX0, st.InX1);
        // Ellipse body centred at 0.6425 with half-height 0.3425; the neck above it is narrow
        if (y < 0.33f) return (0.44f, 0.56f);
        var dy = (y - 0.6425f) / 0.3425f;
        var half = 0.44f * MathF.Sqrt(Math.Max(0f, 1f - dy * dy));
        return (0.5f - half, 0.5f + half);
    }

    public static void Bottle(ImDrawListPtr dl, Vector2 min, Vector2 size, float fill, Flow flow = Flow.Still, Vector4? milkTint = null, int tier = 1)
    {
        fill = Math.Clamp(fill, 0f, 1f);
        var max = min + size;
        var back = Art?.BottleBack(tier);
        var milk = Art?.BottleMilk(tier);
        var front = Art?.BottleFront(tier);
        var t = (float)ImGui.GetTime();
        var st = StyleFor(tier);

        // How high the milk is, between this bottle's bottom and its full line
        var top = st.Top;
        var bottom = st.Bottom;
        var level = bottom - (bottom - top) * fill;
        var surfaceY = min.Y + size.Y * level;

        if (back == null || milk == null || front == null)
        {
            var bmin = new Vector2(min.X + size.X * 0.1f, min.Y + size.Y * top);
            var bmax = new Vector2(max.X - size.X * 0.1f, min.Y + size.Y * bottom);
            dl.AddRectFilled(bmin, bmax, U(Theme.Cream), 16);
            dl.AddRectFilled(new Vector2(bmin.X, surfaceY), bmax, U(Theme.Paper), 16);
            dl.AddRect(bmin, bmax, U(Theme.StoneDeep, 0.6f), 16, ImDrawFlags.None, 2f);
            return;
        }

        dl.AddImage(back.Handle, min, max);

        if (fill > 0.001f)
        {
            var tint = ImGui.GetColorU32(milkTint ?? Vector4.One);

            // Wavy surface: the milk is drawn in thin vertical slices, each cut at its own wave height
            var amp = size.Y * (flow == Flow.Draining ? 0.010f : flow == Flow.Filling ? 0.006f : 0.003f);
            var speed = flow == Flow.Draining ? 3.2f : 1.8f;
            const int slices = 36;
            var surface = new Vector2[slices + 1];
            for (var i = 0; i <= slices; i++)
            {
                var u = i / (float)slices;
                var wave = MathF.Sin(t * speed + u * 9f) * amp + MathF.Sin(t * speed * 1.7f - u * 15f) * amp * 0.45f;
                var y = Math.Clamp(surfaceY + wave, min.Y + size.Y * top, max.Y);
                surface[i] = new Vector2(min.X + size.X * u, y);
            }

            for (var i = 0; i < slices; i++)
            {
                var u0 = i / (float)slices;
                var u1 = (i + 1) / (float)slices;
                var y = Math.Min(surface[i].Y, surface[i + 1].Y);
                var v0 = (y - min.Y) / size.Y;
                dl.AddImage(milk.Handle, new Vector2(min.X + size.X * u0, y), new Vector2(min.X + size.X * u1, max.Y),
                    new Vector2(u0, v0), new Vector2(u1, 1f), tint);
            }

            // A soft cream line along the surface, inside the glass
            var (ix0, ix1) = InsideAt(st, level);
            for (var i = 0; i < slices; i++)
            {
                var u = (i + 0.5f) / slices;
                if (u < ix0 + 0.03f || u > ix1 - 0.03f) continue;
                dl.AddLine(surface[i], surface[i + 1], U(Theme.Linen, 0.95f), 2f);
            }

            if (flow == Flow.Draining) Bubbles(dl, min, size, surfaceY, t, st);
        }

        if (flow == Flow.Filling && st.Drops) Drops(dl, min, size, surfaceY, t, st);

        dl.AddImage(front.Handle, min, max);
    }

    /// <summary>Drops of milk falling from the neck into the bottle, with a small splash.</summary>
    private static void Drops(ImDrawListPtr dl, Vector2 min, Vector2 size, float surfaceY, float t, BottleStyle st)
    {
        const float period = 1.6f;
        var cx = min.X + size.X * st.NeckX;
        var startY = min.Y + size.Y * st.NeckY;
        var r = Math.Max(2f, size.X * 0.028f);
        var milkCol = U(new Vector4(1f, 0.995f, 0.975f, 1f));
        var edge = U(Theme.StoneDeep, 0.35f);

        for (var k = 0; k < 2; k++)
        {
            var phase = (t / period + k * 0.5f) % 1f;
            var x = cx + (k == 0 ? -1 : 1) * size.X * 0.03f;
            const float fall = 0.62f;
            if (phase < fall)
            {
                var p = phase / fall;
                var y = startY + (surfaceY - startY - r) * p * p;   // speeds up as it falls
                var stretch = 1f + p * 0.8f;
                dl.AddCircleFilled(new Vector2(x, y), r, milkCol);
                dl.AddTriangleFilled(new Vector2(x - r * 0.9f, y - r * 0.2f), new Vector2(x, y - r * 2.2f * stretch), new Vector2(x + r * 0.9f, y - r * 0.2f), milkCol);
                dl.AddCircle(new Vector2(x, y), r, edge, 0, 1f);
            }
            else
            {
                // Splash: a few droplets jump up and fall back, and a ring spreads out
                var p = (phase - fall) / (1f - fall);
                for (var j = -2; j <= 2; j++)
                {
                    if (j == 0) continue;
                    var dx = j * size.X * 0.035f * p;
                    var dy = -MathF.Sin(p * MathF.PI) * size.Y * 0.03f * (1.2f - Math.Abs(j) * 0.3f);
                    dl.AddCircleFilled(new Vector2(x + dx, surfaceY + dy), r * 0.45f * (1 - p * 0.5f), U(new Vector4(1f, 0.995f, 0.975f, 1f - p)));
                }

                var ring = size.X * 0.04f + size.X * 0.14f * p;
                dl.AddLine(new Vector2(x - ring, surfaceY + 1), new Vector2(x + ring, surfaceY + 1), U(Theme.StoneDeep, 0.35f * (1 - p)), 1.5f);
            }
        }
    }

    /// <summary>Little bubbles rising through the milk while it drains.</summary>
    private static void Bubbles(ImDrawListPtr dl, Vector2 min, Vector2 size, float surfaceY, float t, BottleStyle st)
    {
        var bottomY = min.Y + size.Y * (st.Bottom - 0.02f);
        if (bottomY - surfaceY < 8) return;
        for (var k = 0; k < 7; k++)
        {
            var seed = k * 0.618f;
            var phase = (t * (0.35f + (k % 3) * 0.12f) + seed) % 1f;
            var span = st.InX1 - st.InX0;
            var x = min.X + size.X * (st.InX0 + span * (0.15f + ((k * 0.37f) % 1f) * 0.7f)) + MathF.Sin(t * 2f + k) * size.X * span * 0.02f;
            var y = bottomY - (bottomY - surfaceY) * phase;
            var r = Math.Max(1.5f, size.X * (0.012f + (k % 2) * 0.008f));
            dl.AddCircle(new Vector2(x, y), r, U(Theme.StoneDeep, 0.35f * (1 - phase * 0.7f)), 0, 1f);
        }
    }

    /// <summary>Labelled need gauge with an icon. Returns its height.</summary>
    public static float Gauge(ImDrawListPtr dl, Vector2 pos, float width, string icon, string label, float value, Vector4 color)
    {
        const float r = 17f;
        Icon(dl, icon, pos + new Vector2(r, r + 2), r);
        var x = pos.X + r * 2 + 10;
        var w = width - (x - pos.X);
        Text(dl, Fonts?.Label, label.ToUpperInvariant(), new Vector2(x, pos.Y - 1), Theme.InkSoft);
        Text(dl, Fonts?.Small, $"{value:0}", new Vector2(x + w, pos.Y), Theme.Ink, 1f);
        var barMin = new Vector2(x, pos.Y + 24);
        var barMax = new Vector2(x + w, pos.Y + 32);
        dl.AddRectFilled(barMin, barMax, U(Theme.Linen), 4);
        var c = Theme.NeedColor(value, color);
        var fillMax = new Vector2(barMin.X + (barMax.X - barMin.X) * Math.Clamp(value / 100f, 0f, 1f), barMax.Y);
        if (fillMax.X > barMin.X + 1) dl.AddRectFilled(barMin, fillMax, U(c), 4);
        dl.AddRect(barMin, barMax, U(Theme.StoneDeep, 0.25f), 4, ImDrawFlags.None, 1f);
        return r * 2 + 6;
    }

    /// <summary>Thin progress bar.</summary>
    public static void Bar(ImDrawListPtr dl, Vector2 min, Vector2 max, float t, Vector4 color)
    {
        var r = (max.Y - min.Y) / 2;
        dl.AddRectFilled(min, max, U(Theme.Linen), r);
        var fx = min.X + (max.X - min.X) * Math.Clamp(t, 0f, 1f);
        if (fx > min.X + 1) dl.AddRectFilled(min, new Vector2(fx, max.Y), U(color), r);
    }

    // ---------------------------------------------------------------- buttons

    /// <summary>
    /// Matte pill button drawn at the cursor (or at pos). Colours: rose, blue, sage, honey, mauve, cream.
    /// Disabled buttons still show their tooltip so you can see why.
    /// </summary>
    public static bool Button(string id, string label, Vector2 size, string color = "rose", bool enabled = true, string? tooltip = null, Vector2? pos = null)
    {
        if (pos.HasValue) ImGui.SetCursorScreenPos(pos.Value);
        var p = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        var dl = ImGui.GetWindowDrawList();

        var min = p + new Vector2(0, enabled && held ? 1.5f : 0f);
        var max = p + size + new Vector2(0, enabled && held ? 1.5f : 0f);
        var tex = Art?.Button(enabled ? color : "cream");

        if (tex != null)
        {
            uint tint = !enabled ? 0xB0FFFFFFu : held ? 0xFFE0E0E0u : 0xFFFFFFFFu;
            Windows.Art.NineSlice(dl, tex, min, max, 40f, size.Y * 0.45f, tint);
            if (enabled && hovered) dl.AddRectFilled(min + new Vector2(2, 2), max - new Vector2(2, 4), U(Theme.Paper, 0.18f), size.Y / 2);
        }
        else
        {
            var c = enabled ? color switch
            {
                "blue" => Theme.Blue,
                "sage" => Theme.Sage,
                "honey" => Theme.Honey,
                "mauve" => Theme.Mauve,
                "cream" => Theme.Cream,
                _ => Theme.Rose,
            } : Theme.Linen;
            dl.AddRectFilled(min, max, U(hovered && enabled ? Theme.Lighten(c, 0.15f) : c), size.Y / 2);
            dl.AddRect(min, max, U(Theme.Darken(c, 0.3f), 0.8f), size.Y / 2, ImDrawFlags.None, 1.5f);
        }

        Text(dl, Fonts?.Label, label.ToUpperInvariant(), (min + max) / 2 + new Vector2(0, -1), enabled ? Theme.Ink : Theme.InkSoft, 0.5f, 0.5f);

        if (tooltip != null && hovered) ImGui.SetTooltip(tooltip);
        return clicked && enabled;
    }
}
