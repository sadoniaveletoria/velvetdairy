using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>Short messages shown at the bottom of the window (and on the HUD).</summary>
public static class Toasts
{
    public sealed record Toast(string Text, int Kind, double T0);

    public static readonly List<Toast> Items = new();

    public static void Add(string text, int kind)
    {
        Items.Insert(0, new Toast(text, kind, ImGui.GetTime()));
        if (Items.Count > 4) Items.RemoveRange(4, Items.Count - 4);
    }

    public static void Prune(double now) => Items.RemoveAll(t => now - t.T0 > 5);
}

public sealed partial class MainWindow : Window
{
    private readonly Plugin plugin;
    private readonly Art art;
    private readonly Fonts fonts;

    private FarmSave F => plugin.Config.Farm;
    private DairyGame G => plugin.Game;

    private static readonly string[] Tabs = { "Barn", "Creamery", "Shop", "Dairy", "Dairies", "Progress", "Settings" };
    private int tab;
    private bool ticked18;
    private string dairyName = "";
    private string newEmote = "";
    private float shownMilk = -1;
    private bool showHiddenFeed;
    private int seenDump;
    private bool showOwnedUpgrades;
    private int lastWholeOz = -1;
    private readonly List<double> ozPops = new();

    /// <summary>What the bottle should be doing: filling while milk is coming in, draining while you're milked.</summary>
    private Ui.Flow FlowNow()
    {
        if (G.Active != null) return Ui.Flow.Draining;
        if (G.Paused || G.Overfull || G.Spoiled || G.Blocked() != null) return Ui.Flow.Still;
        return Ui.Flow.Filling;
    }

    private const float Pad = 18f;

    /// <summary>Opens the window on a tab (used by the HUDs).</summary>
    public void OpenTab(int i)
    {
        tab = i;
        IsOpen = true;
    }

    /// <summary>Spoiled milk is tinted a sour yellow.</summary>
    public static readonly Vector4 SourMilk = new(0.93f, 0.88f, 0.60f, 1f);

    public MainWindow(Plugin plugin, Art art, Fonts fonts)
        : base("Velvet Dairy##VelvetDairyMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.art = art;
        this.fonts = fonts;

        Size = new Vector2(1060, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(980, 700),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        plugin.Game.Toast += Toasts.Add;
        dairyName = plugin.Config.Farm.Dairy?.Name ?? "My Dairy";
    }

    // =====================================================================
    // Window style: light cream instead of the default dark ImGui
    // =====================================================================

    private int pushedColors;
    private int pushedVars;

    public override void PreDraw()
    {
        void C(ImGuiCol c, Vector4 v)
        {
            ImGui.PushStyleColor(c, v);
            pushedColors++;
        }

        void V(ImGuiStyleVar v, float f)
        {
            ImGui.PushStyleVar(v, f);
            pushedVars++;
        }

        C(ImGuiCol.WindowBg, Theme.Paper);
        C(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
        C(ImGuiCol.PopupBg, Theme.Paper);
        C(ImGuiCol.Border, Theme.Alpha(Theme.RoseDeep, 0.35f));
        C(ImGuiCol.TitleBg, Theme.Rose);
        C(ImGuiCol.TitleBgActive, Theme.RoseDeep);
        C(ImGuiCol.TitleBgCollapsed, Theme.Rose);
        C(ImGuiCol.Text, Theme.Ink);
        C(ImGuiCol.TextDisabled, Theme.InkSoft);
        C(ImGuiCol.FrameBg, Theme.Cream);
        C(ImGuiCol.FrameBgHovered, Theme.Linen);
        C(ImGuiCol.FrameBgActive, Theme.Linen);
        C(ImGuiCol.Button, Theme.Rose);
        C(ImGuiCol.ButtonHovered, Theme.Lighten(Theme.Rose, 0.15f));
        C(ImGuiCol.ButtonActive, Theme.RoseDeep);
        C(ImGuiCol.Header, Theme.Alpha(Theme.Rose, 0.6f));
        C(ImGuiCol.HeaderHovered, Theme.Rose);
        C(ImGuiCol.HeaderActive, Theme.RoseDeep);
        C(ImGuiCol.CheckMark, Theme.RoseDeep);
        C(ImGuiCol.SliderGrab, Theme.RoseDeep);
        C(ImGuiCol.SliderGrabActive, Theme.Ink);
        C(ImGuiCol.Separator, Theme.Alpha(Theme.RoseDeep, 0.3f));
        C(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0));
        C(ImGuiCol.ScrollbarGrab, Theme.Alpha(Theme.RoseDeep, 0.35f));
        C(ImGuiCol.ScrollbarGrabHovered, Theme.Alpha(Theme.RoseDeep, 0.55f));
        C(ImGuiCol.ScrollbarGrabActive, Theme.RoseDeep);
        C(ImGuiCol.ResizeGrip, Theme.Alpha(Theme.RoseDeep, 0.25f));
        C(ImGuiCol.ResizeGripHovered, Theme.Alpha(Theme.RoseDeep, 0.5f));
        C(ImGuiCol.ResizeGripActive, Theme.RoseDeep);

        V(ImGuiStyleVar.WindowRounding, 12f);
        V(ImGuiStyleVar.FrameRounding, 8f);
        V(ImGuiStyleVar.GrabRounding, 8f);
        V(ImGuiStyleVar.ScrollbarSize, 10f);
        // WindowPadding is a Vector2 style var: pushing a float here is a fatal ImGui error
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        pushedVars++;
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(pushedColors);
        ImGui.PopStyleVar(pushedVars);
        pushedColors = 0;
        pushedVars = 0;
    }

    // =====================================================================
    // Frame
    // =====================================================================

    public override void Draw()
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var winMin = ImGui.GetWindowPos();
        var winMax = winMin + ImGui.GetWindowSize();

        // Wallpaper behind everything
        var bg = art.Background;
        if (bg != null)
        {
            var (uv0, uv1) = Art.Cover(bg, avail);
            dl.AddImage(bg.Handle, origin, origin + avail, uv0, uv1);
        }

        var headerH = DrawHeader(dl, origin, avail.X);
        var tabsH = DrawTabs(dl, origin + new Vector2(0, headerH), avail.X);
        var bodyMin = origin + new Vector2(Pad, headerH + tabsH + 10);
        var bodyMax = origin + avail - new Vector2(Pad, Pad);

        if (!F.Confirmed18) DrawGate(bodyMin, bodyMax);
        else
        {
            switch (tab)
            {
                case 0: DrawBarn(bodyMin, bodyMax); break;
                case 1: DrawCreamery(bodyMin, bodyMax); break;
                case 2: DrawShop(bodyMin, bodyMax); break;
                case 3: DrawDairy(bodyMin, bodyMax); break;
                case 4: DrawNetwork(bodyMin, bodyMax); break;
                case 5: DrawProgress(bodyMin, bodyMax); break;
                default: DrawSettings(bodyMin, bodyMax); break;
            }
        }

        DrawToasts(ImGui.GetForegroundDrawList(), winMin, winMax);
    }

    private float DrawHeader(ImDrawListPtr dl, Vector2 o, float width)
    {
        const float h = 92f;
        var (lvl, into, need) = Leveling.FromXp(F.Xp);

        // A cream band with a rose rule under it
        dl.AddRectFilled(o, o + new Vector2(width, h), Ui.U(Theme.Paper, 0.92f));
        dl.AddLine(o + new Vector2(0, h - 4), o + new Vector2(width, h - 4), Ui.U(Theme.RoseDeep, 0.55f), 2f);
        dl.AddLine(o + new Vector2(0, h - 1), o + new Vector2(width, h - 1), Ui.U(Theme.RoseDeep, 0.3f), 1f);

        Ui.Avatar(dl, "you", o + new Vector2(Pad + 30, 44), 30);
        var tx = o.X + Pad + 74;
        Ui.Text(dl, fonts.Label, "VELVET", new Vector2(tx, o.Y + 14), Theme.RoseDeep);
        Ui.Text(dl, fonts.Title, "DAIRY", new Vector2(tx - 1, o.Y + 34), Theme.Ink);

        // Level and rank
        var mx = tx + 150;
        Ui.Text(dl, fonts.Heading, $"Level {lvl} · {Leveling.Rank(lvl)}", new Vector2(mx, o.Y + 24), Theme.Ink);
        Ui.Bar(dl, new Vector2(mx, o.Y + 56), new Vector2(mx + 240, o.Y + 63), need == 0 ? 1f : (float)into / need, Theme.RoseDeep);
        Ui.Text(dl, fonts.Small, need == 0 ? "Max level" : $"{into} / {need} xp", new Vector2(mx, o.Y + 66), Theme.InkSoft);

        // Coins and where you are, right aligned
        var right = o.X + width - Pad;
        var coins = $"{F.Coins:N0}";
        var cw = Ui.Measure(fonts.Heading, coins).X;
        Ui.Icon(dl, "coin", new Vector2(right - cw - 22, o.Y + 36), 16);
        Ui.Text(dl, fonts.Heading, coins, new Vector2(right, o.Y + 24), Theme.Ink, 1f);
        var where = G.Where;
        var dairy = G.DairyName;
        var chip = (G.Dev ? "DEV - " : "") + (G.Paused ? "Paused" : where.InDairy ? where.Why : dairy.Length == 0 ? "No dairy yet" : $"Away from {dairy}");
        if (G.Networked && G.Role != "cow" && !G.Paused) chip = $"{G.Role} · {chip}";
        if (G.Net.UpdateVersion is { } nv) chip = $"Update {nv} available · {chip}";
        var chipColor = G.Paused ? Theme.InkSoft : where.InDairy ? Theme.SageDeep : Theme.RoseDeep;
        var cs = Ui.Measure(fonts.Label, chip.ToUpperInvariant());
        var cmin = new Vector2(right - cs.X - 30, o.Y + 56);
        var cmax = new Vector2(right, o.Y + 56 + cs.Y + 6);
        dl.AddRectFilled(cmin, cmax, Ui.U(chipColor, 0.12f), 99);
        dl.AddRect(cmin, cmax, Ui.U(chipColor, 0.6f), 99, ImDrawFlags.None, 1f);
        dl.AddCircleFilled(new Vector2(cmin.X + 11, (cmin.Y + cmax.Y) / 2), 3.5f, Ui.U(chipColor));
        Ui.Text(dl, fonts.Label, chip.ToUpperInvariant(), new Vector2(cmin.X + 20, cmin.Y + 3), chipColor);

        // How many people are playing right now
        if (G.Net.Last != null)
        {
            var online = $"{G.Net.Last.Online:N0} online";
            var ow = Ui.Measure(fonts.Small, online).X;
            var ox = right - cw - 40 - ow;
            dl.AddCircleFilled(new Vector2(ox - 9, o.Y + 35), 4f, Ui.U(Theme.SageDeep));
            Ui.Text(dl, fonts.Small, online, new Vector2(ox, o.Y + 27), Theme.SageDeep);
        }

        return h;
    }

    private float DrawTabs(ImDrawListPtr dl, Vector2 o, float width)
    {
        const float h = 44f;
        var x = o.X + Pad;
        for (var i = 0; i < Tabs.Length; i++)
        {
            var label = Tabs[i].ToUpperInvariant();
            var size = Ui.Measure(fonts.Label, label);
            var min = new Vector2(x, o.Y + 8);
            var bw = size.X + 28;
            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton($"##tab{i}", new Vector2(bw, h - 10))) tab = i;
            var hovered = ImGui.IsItemHovered();
            var sel = tab == i;
            if (sel) dl.AddRectFilled(min, min + new Vector2(bw, h - 10), Ui.U(Theme.Paper, 0.85f), 8, ImDrawFlags.RoundCornersTop);
            Ui.Text(dl, fonts.Label, label, min + new Vector2(bw / 2, (h - 10) / 2), sel ? Theme.Ink : hovered ? Theme.RoseDeep : Theme.InkSoft, 0.5f, 0.5f);
            if (sel) dl.AddLine(min + new Vector2(10, h - 12), min + new Vector2(bw - 10, h - 12), Ui.U(Theme.RoseDeep), 2f);
            x += bw + 4;
        }

        return h;
    }

    private void DrawToasts(ImDrawListPtr dl, Vector2 winMin, Vector2 winMax)
    {
        var now = ImGui.GetTime();
        Toasts.Prune(now);
        var y = winMax.Y - 24;
        foreach (var t in Toasts.Items)
        {
            var age = (float)(now - t.T0);
            var a = age < 0.25f ? age / 0.25f : age > 4.4f ? Math.Max(0, (5f - age) / 0.6f) : 1f;
            var size = Ui.Measure(null, t.Text);
            var min = new Vector2((winMin.X + winMax.X) / 2 - size.X / 2 - 18, y - size.Y - 14);
            var max = new Vector2((winMin.X + winMax.X) / 2 + size.X / 2 + 18, y);
            var c = t.Kind == 2 ? Theme.Warn : t.Kind == 1 ? Theme.SageDeep : Theme.BlueDeep;
            dl.AddRectFilled(min + new Vector2(0, 3), max + new Vector2(0, 3), Ui.U(Theme.Shadow, a), 99);
            dl.AddRectFilled(min, max, Ui.U(Theme.Paper, 0.98f * a), 99);
            dl.AddRect(min, max, Ui.U(c, 0.8f * a), 99, ImDrawFlags.None, 1.5f);
            dl.AddText(min + new Vector2(18, 7), Ui.U(Theme.Ink, a), t.Text);
            y = min.Y - 6;
        }
    }

    // =====================================================================
    // Layout helpers
    // =====================================================================

    /// <summary>A card with a heading and a scrolling body. body gets the usable width.</summary>
    private void Panel(string id, Vector2 min, Vector2 max, string title, Action<float> body, bool blue = false)
    {
        var dl = ImGui.GetWindowDrawList();
        Ui.Card(dl, min, max, blue);
        var inMin = min + new Vector2(28, 24);
        var inMax = max - new Vector2(28, 24);
        Ui.Eyebrow(dl, title, inMin, inMax.X - inMin.X, blue ? Theme.BlueDeep : Theme.RoseDeep);
        ImGui.SetCursorScreenPos(inMin + new Vector2(0, 32));
        var size = inMax - inMin - new Vector2(0, 32);
        if (size.X > 10 && size.Y > 10 && ImGui.BeginChild(id, size, false))
        {
            body(ImGui.GetContentRegionAvail().X);
            ImGui.Dummy(new Vector2(0, 4));
        }

        if (size.X > 10 && size.Y > 10) ImGui.EndChild();
    }

    private sealed record Btn(string Id, string Label, string Color, bool Enabled = true, string? Tip = null, float Width = 0);

    private sealed record Line(string Text, Vector4 Color);

    private float BtnWidth(Btn b) => b.Width > 0 ? b.Width : Math.Max(86, Ui.Measure(fonts.Label, b.Label.ToUpperInvariant()).X + 34);

    /// <summary>
    /// One list entry that sizes itself: icon on the left, a title and wrapped lines of text, an optional progress bar,
    /// and buttons. Buttons sit on the right when there's room and move under the text when there isn't.
    /// Returns the index of the button that was clicked, or -1.
    /// </summary>
    private int ListRow(Action<Vector2>? icon, string title, Vector4 titleColor, IReadOnlyList<Line> lines, IReadOnlyList<Btn> buttons, float width,
        float barT = -1, Vector4? barColor = null)
    {
        const float gap = 8f, bh = 34f, iconR = 20f;
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var iconW = icon != null ? iconR * 2 + 14 : 0;
        var widths = buttons.Select(BtnWidth).ToArray();
        var btnW = widths.Sum() + gap * Math.Max(0, widths.Length - 1);
        var inline = buttons.Count == 0 || width - iconW - btnW - 14 >= 120;
        var textW = Math.Max(60, inline ? width - iconW - (buttons.Count > 0 ? btnW + 14 : 0) : width - iconW);
        var tx = p.X + iconW;

        // Text, wrapped to its column. The group keeps every line indented past the icon
        // (without it, each new line jumps back to the left edge of the panel).
        ImGui.SetCursorScreenPos(new Vector2(tx, p.Y + 6));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        using (fonts.For(fonts.Row, title)!.Push()) ImGui.TextColored(titleColor, title);
        foreach (var l in lines)
            if (l.Text.Length > 0) ImGui.TextColored(l.Color, l.Text);
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var textBottom = ImGui.GetItemRectMax().Y + 2;
        if (barT >= 0)
        {
            Ui.Bar(dl, new Vector2(tx, textBottom + 2), new Vector2(tx + textW, textBottom + 9), barT, barColor ?? Theme.BlueDeep);
            textBottom += 14;
        }

        // Buttons beside the text, or under it (wrapping onto more lines) on narrow panels
        var pos = new Vector2[buttons.Count];
        if (inline)
        {
            var bx = p.X + width - btnW;
            var by = p.Y + Math.Max(6, (textBottom - p.Y - bh) / 2);
            for (var i = 0; i < buttons.Count; i++)
            {
                pos[i] = new Vector2(bx, by);
                bx += widths[i] + gap;
            }
        }
        else
        {
            float bx = tx, by = textBottom + 2;
            for (var i = 0; i < buttons.Count; i++)
            {
                if (bx > tx && bx + widths[i] > p.X + width)
                {
                    bx = tx;
                    by += bh + gap;
                }

                pos[i] = new Vector2(bx, by);
                bx += widths[i] + gap;
            }
        }

        var buttonsBottom = buttons.Count == 0 ? textBottom : pos.Max(v => v.Y) + bh;
        var bottom = Math.Max(textBottom, buttonsBottom) + 8;
        icon?.Invoke(new Vector2(p.X + iconR + 2, p.Y + Math.Min(iconR + 10, (bottom - p.Y) / 2)));

        var clicked = -1;
        for (var i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            if (Ui.Button(b.Id, b.Label, new Vector2(widths[i], bh), b.Color, b.Enabled, b.Tip, pos[i])) clicked = i;
        }

        dl.AddLine(new Vector2(p.X, bottom), new Vector2(p.X + width, bottom), Ui.U(Theme.RoseDeep, 0.15f), 1f);
        ImGui.SetCursorScreenPos(new Vector2(p.X, bottom + 6));
        ImGui.Dummy(new Vector2(width, 1));
        return clicked;
    }

    /// <summary>Simple list row with one button.</summary>
    private bool Row(string id, string icon, string title, string sub, string button, string color, bool enabled, string? tip, float width)
    {
        var buttons = button.Length > 0 ? new[] { new Btn(id, button, color, enabled, tip) } : Array.Empty<Btn>();
        return ListRow(Ico(icon, enabled || button.Length == 0 ? 1f : 0.65f), title, Theme.Ink, new[] { new Line(sub, Theme.InkSoft) }, buttons, width) == 0;
    }

    private static Action<Vector2> Ico(string name, float alpha = 1f) => c => Ui.Icon(ImGui.GetWindowDrawList(), name, c, 20, alpha);

    /// <summary>A row of buttons at the cursor that wraps onto new lines when it runs out of room.</summary>
    private int Buttons(IReadOnlyList<Btn> buttons, float width)
    {
        const float gap = 8f, bh = 34f;
        var p = ImGui.GetCursorScreenPos();
        float x = p.X, y = p.Y;
        var clicked = -1;
        for (var i = 0; i < buttons.Count; i++)
        {
            var w = BtnWidth(buttons[i]);
            if (x > p.X && x + w > p.X + width)
            {
                x = p.X;
                y += bh + gap;
            }

            var b = buttons[i];
            if (Ui.Button(b.Id, b.Label, new Vector2(w, bh), b.Color, b.Enabled, b.Tip, new Vector2(x, y))) clicked = i;
            x += w + gap;
        }

        ImGui.SetCursorScreenPos(new Vector2(p.X, y + bh + 6));
        ImGui.Dummy(new Vector2(width, 1));
        return clicked;
    }

    private static void Gap(float h = 8) => ImGui.Dummy(new Vector2(0, h));

    private void SubHeading(string text, float width)
    {
        Gap(6);
        var dl = ImGui.GetWindowDrawList();
        Ui.Eyebrow(dl, text, ImGui.GetCursorScreenPos(), width, Theme.InkSoft);
        Gap(28);
    }

    private static string Mins(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s" : $"{Math.Max(0, t.Seconds)}s";

    // =====================================================================
    // 18+ gate
    // =====================================================================

    private void DrawGate(Vector2 min, Vector2 max)
    {
        var w = 560f;
        var h = 300f;
        var c = (min + max) / 2;
        var pmin = c - new Vector2(w / 2, h / 2);
        Panel("##gate", pmin, pmin + new Vector2(w, h), "Adults only", width =>
        {
            Ui.Wrapped("Velvet Dairy is an adult roleplay game. You play the cow: keep yourself fed and comfortable inside your dairy, " +
                       "get milked in your stalls or by a farmhand you trust, and sell what you make to the creamery.", Theme.Ink, width);
            Gap(10);
            Ui.Wrapped("Everything happens in this window and your own chat log. Nothing is ever said in public chat, and nothing moves your character for you.", Theme.InkSoft, width);
            Gap(14);
            ImGui.Checkbox("I'm 18 or older##gate18", ref ticked18);
            Gap(10);
            if (Ui.Button("##gateok", "Open the barn", new Vector2(200, 40), "rose", ticked18))
            {
                F.Confirmed18 = true;
                plugin.SaveConfig();
            }
        });
    }

    // =====================================================================
    // Barn
    // =====================================================================

    private void DrawBarn(Vector2 min, Vector2 max)
    {
        var blocked = G.Blocked();
        var milkH = 200f;
        var topMax = new Vector2(max.X, max.Y - milkH - 14);
        var leftW = 270f;
        var rightW = 330f;

        DrawBottleCard(min, new Vector2(min.X + leftW, topMax.Y));
        var midMin = new Vector2(min.X + leftW + 14, min.Y);
        var midMax = new Vector2(max.X - rightW - 14, topMax.Y);
        Panel("##needs", midMin, midMax, "How you're doing", DrawNeeds);
        Panel("##care", new Vector2(max.X - rightW, min.Y), topMax, "Care", DrawCare);
        Panel("##milking", new Vector2(min.X, max.Y - milkH), max, G.Active != null ? "Milking now" : "Milking", DrawMilking, true);

        if (blocked != null && blocked != "Not logged in.")
        {
            var dl = ImGui.GetWindowDrawList();
            dl.AddText(min + new Vector2(28, -4), Ui.U(Theme.Warn), blocked);
        }
    }

    private void DrawBottleCard(Vector2 min, Vector2 max)
    {
        var dl = ImGui.GetWindowDrawList();
        Ui.Card(dl, min, max);
        var cow = F.Cow;
        var cap = G.Capacity;
        var size = G.BottleSize;
        var count = G.BottleCount;

        // Ease the shown level toward the real one so filling and draining animate
        var target = cow.Milk;
        if (seenDump != G.DumpSerial)
        {
            seenDump = G.DumpSerial;
            shownMilk = target;
        }

        shownMilk = shownMilk < 0 ? target : shownMilk + (target - shownMilk) * Math.Min(1f, ImGui.GetIO().DeltaTime * 4f);

        // The big bottle is the one filling right now; full ones sit in the row underneath
        var full = Math.Min(count, (int)(shownMilk / size + 0.001f));
        var current = full >= count ? count - 1 : full;
        var frac = full >= count ? 1f : Math.Clamp((shownMilk - full * size) / size, 0f, 1f);

        var w = max.X - min.X;
        var rowH = count > 1 ? 58f : 0f;
        var bh = Math.Clamp(max.Y - min.Y - 150 - rowH, 150f, 380f);
        var bw = bh / 2f;
        var bmin = new Vector2(min.X + (w - bw) / 2 - 8, min.Y + 26);
        var milking = G.Active != null;
        var tint = G.Spoiled ? SourMilk
            : milking && G.Active!.Kind == SessionKind.Hand ? new Vector4(1f, 0.97f, 0.96f, 1f) : (Vector4?)null;
        Ui.Bottle(dl, bmin, new Vector2(bw, bh), frac, FlowNow(), tint, G.BottleTier);

        // "+1 oz" floats up from the surface each time another whole ounce comes in
        var whole = (int)MathF.Floor(cow.Milk);
        if (lastWholeOz >= 0 && whole > lastWholeOz && G.Active == null) ozPops.Add(ImGui.GetTime());
        lastWholeOz = whole;
        var nowT = ImGui.GetTime();
        ozPops.RemoveAll(t0 => nowT - t0 > 1.6);
        var surface = bmin.Y + bh * (0.96f - 0.70f * frac);
        foreach (var t0 in ozPops)
        {
            var a = (float)((nowT - t0) / 1.6);
            Ui.Text(dl, fonts.Label, "+1 OZ", new Vector2(bmin.X + bw + 4, surface - 10 - a * 34), Theme.Alpha(Theme.SageDeep, 1f - a), 0f, 0.5f);
        }

        // Grade seal on the bottle's shoulder
        var grade = G.CurrentGrade;
        var sealC = new Vector2(bmin.X + bw + 6, bmin.Y + bh * 0.3f);
        Ui.Seal(dl, sealC, 28, Grades.Name(grade), Theme.GradeColor(grade));
        if (ImGui.IsMouseHoveringRect(sealC - new Vector2(28, 28), sealC + new Vector2(28, 28)))
            ImGui.SetTooltip($"Grade {Grades.Name(grade)} ({Grades.All[grade].Blurb}) if you're milked now.\nScore {Grades.Score(cow):0} from your needs and feed quality. Hand milking adds one grade." +
                (cow.Taint > 0 ? $"\nOff taste from dumped milk: -{Grades.TaintPenalty(cow):0} points." : "") +
                GradeCapNote());

        var y = bmin.Y + bh + 6;
        var cx = min.X + w / 2;
        Ui.Text(dl, fonts.Title, $"{cow.Milk:0.#}", new Vector2(cx - 6, y), Theme.Ink, 1f);
        Ui.Text(dl, fonts.Heading, $"/ {cap:0} oz", new Vector2(cx, y + 14), Theme.InkSoft);
        y += 46;

        // Milk speed, with what goes into it on hover
        string line;
        Vector4 col = Theme.InkSoft;
        if (G.Spoiled)
        {
            line = "Spoiled! Dump it";
            col = Theme.Warn;
        }
        else if (G.Overfull)
        {
            var left = G.SpoilsIn;
            line = left.HasValue ? $"Full! Spoils in {Mins(left.Value)}" : count > 1 ? "All bottles full! Get milked" : "Full! Get milked soon";
            col = Theme.Warn;
        }
        else if (milking) line = "Being milked";
        else line = $"+{G.ProductionPerMinute():0.00} oz a minute";
        var ls = Ui.Text(dl, fonts.Label, line.ToUpperInvariant(), new Vector2(cx, y), col, 0.5f);
        if (!milking && ImGui.IsMouseHoveringRect(new Vector2(cx - ls.X / 2, y), new Vector2(cx + ls.X / 2, y + ls.Y)))
        {
            var tip = "Milk speed\n" + string.Join("\n", G.RateParts().Select(r => $"  {r.Name}: x{r.Multiplier:0.00}"));
            tip += "\nFull speed needs good care. Level, contentment, treats and resting all add to it.";
            ImGui.SetTooltip(tip);
        }

        y += 24;
        if (G.Spoiled)
        {
            if (Ui.Button("##dumpmain", "Dump milk", new Vector2(150, 36), "rose", true,
                    "Pour it all out. You get nothing for it, and your milk tastes off for a while.", new Vector2(cx - 75, y)))
                G.DumpMilk();
            return;
        }

        var extras = new List<string>();
        if (cow.Taint > 0) extras.Add($"Off taste -{Grades.TaintPenalty(cow):0}");
        if (G.Where.InDairy && G.Where.Seated && !milking) extras.Add("Resting");
        if (G.Contented) extras.Add("Contented");
        if (G.BoostLeft > TimeSpan.Zero) extras.Add($"Treat boost {Mins(G.BoostLeft)}");
        if (extras.Count > 0) Ui.Text(dl, null, string.Join(" · ", extras), new Vector2(cx, y), Theme.SageDeep, 0.5f);
        y += 22;

        // Row of small bottles when you own more than one
        var label = count > 1 ? $"{G.BottleName} · bottle {current + 1} of {count}" : $"{G.BottleName} bottle · {size:0} oz";
        if (count == 1)
        {
            Ui.Text(dl, null, label, new Vector2(cx, y), Theme.InkSoft, 0.5f);
            return;
        }

        const float mh = 40f, mw = 20f, gap = 10f;
        var rowW = count * mw + (count - 1) * gap;
        var mx = cx - rowW / 2;
        for (var i = 0; i < count; i++)
        {
            var f = i < full ? 1f : i == full ? frac : 0f;
            var mmin = new Vector2(mx + i * (mw + gap), y);
            Ui.Bottle(dl, mmin, new Vector2(mw, mh), f, Ui.Flow.Still, null, G.BottleTier);
            if (i == current) dl.AddLine(new Vector2(mmin.X + 2, y + mh + 3), new Vector2(mmin.X + mw - 2, y + mh + 3), Ui.U(Theme.RoseDeep), 2f);
        }

        Ui.Text(dl, fonts.Small, label, new Vector2(cx, y + mh + 8), Theme.InkSoft, 0.5f);
    }

    private void DrawNeeds(float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var cow = F.Cow;
        var p = ImGui.GetCursorScreenPos();
        var y = p.Y;
        var rows = new (string Icon, string Label, float Value, Vector4 Color, string Tip)[]
        {
            ("hay", "Hunger", cow.Hunger, Theme.HoneyDeep, "Eat feed from your pantry. Drops slowly all the time."),
            ("water", "Thirst", cow.Thirst, Theme.BlueDeep, "Drink at the trough. Drops a little faster than hunger."),
            ("cushion", "Comfort", cow.Comfort, Theme.RoseDeep, "Sit on furniture in your dairy to rest. Being too full hurts it."),
            ("clover", "Mood", cow.Mood, Theme.SageDeep, "Follows how well you're kept. Treats and being milked lift it."),
            ("premium", "Feed quality", cow.Richness, Theme.HoneyDeep, "Rich feed (grain, salt, Velvet blend) raises your milk grade."),
        };

        foreach (var r in rows)
        {
            var h = Ui.Gauge(dl, new Vector2(p.X, y), width, r.Icon, r.Label, r.Value, r.Color);
            if (ImGui.IsMouseHoveringRect(new Vector2(p.X, y), new Vector2(p.X + width, y + h))) ImGui.SetTooltip(r.Tip);
            y += h + 10;
        }

        ImGui.SetCursorScreenPos(new Vector2(p.X, y + 2));
        QualityBar(width);
        var notes = new List<(string, Vector4)>();
        var where = G.Where;
        if (G.Paused) notes.Add(("The game is paused (Developer settings).", Theme.InkSoft));
        if (!where.InDairy) notes.Add((where.Why + " Care and milking wait until you're back.", Theme.RoseDeep));
        if (cow.Hunger < 20) notes.Add(("You're hungry.", Theme.Warn));
        if (cow.Thirst < 20) notes.Add(("You're thirsty.", Theme.Warn));
        if (G.Spoiled) notes.Add(("Your milk spoiled from sitting too long. Dump it (under the bottle or on the HUD) before you can be milked.", Theme.Warn));
        else if (G.Overfull)
        {
            var left = G.SpoilsIn;
            notes.Add((left.HasValue ? $"You're full and it's getting uncomfortable. It spoils in {Mins(left.Value)} if you don't get milked." : "You're full and it's getting uncomfortable.", Theme.Warn));
        }

        if (cow.Taint > 0)
            notes.Add(($"Your milk tastes off after the dump (-{Grades.TaintPenalty(cow):0} grade points). Care tasks, milkings and creamery deliveries wear it off.", Theme.RoseDeep));
        if (notes.Count == 0) notes.Add(("All good. Keep it up for better milk.", Theme.SageDeep));
        foreach (var (t, c) in notes) Ui.Wrapped(t, c, width);
    }

    private void DrawCare(float width)
    {
        var problem = G.CareProblem();
        if (problem != null)
        {
            Ui.Wrapped(problem, Theme.RoseDeep, width);
            Gap(6);
        }

        var ok = problem == null;
        var wcd = G.WaterCooldown;
        if (Row("##drink", "water", "Drink", wcd > 0 ? $"Ready in {wcd:0}s" : "Free · +40 thirst", "Drink", "blue",
                ok && wcd <= 0, problem, width))
            G.Drink();

        var scd = G.StretchCooldown;
        if (Row("##stretch", "cushion", "Stretch", scd > 0 ? $"Ready in {Mins(TimeSpan.FromSeconds(scd))}" : "Free · +12 comfort", "Stretch", "rose",
                ok && scd <= 0, problem, width))
            G.Stretch();

        var shown = 0;
        foreach (var item in Catalog.Feed)
        {
            if (G.Level < item.Unlock || F.HiddenFeed!.Contains(item.Id)) continue;
            var have = G.Count(item.Id);
            if (have <= 0 && !F.CareShowEmpty) continue;   // only what's in your pantry
            shown++;
            var wait = item.Quality > 0 ? G.QualityTreatWait : TimeSpan.Zero;
            var waiting = wait > TimeSpan.Zero;
            var tip = problem ?? (have <= 0 ? "Buy more in the Shop." : waiting ? $"Quality treats share a {Grades.QualityTreatHours:0}-hour cooldown." : item.Desc);
            var sub = waiting ? $"{have} left · ready in {DairyGame.Wait(wait)}" : $"{have} left · {Effects(item)}";
            if (Row($"##feed{item.Id}", item.Icon, item.Name, sub, "Eat", "honey", ok && have > 0 && !waiting, tip, width))
                G.UseFeed(item.Id);
        }

        if (shown == 0) Ui.Wrapped("Your pantry is empty. Buy feed in the Shop.", Theme.InkSoft, width);
        Gap(4);
        var showEmpty = F.CareShowEmpty;
        if (ImGui.Checkbox("Show feed I'm out of##careempty", ref showEmpty))
        {
            F.CareShowEmpty = showEmpty;
            plugin.SaveConfig();
        }
    }

    /// <summary>Tooltip line about how far your milk quality lets your grade go.</summary>
    private string GradeCapNote()
    {
        var cap = G.GradeCap;
        var next = G.NextQuality is { } need ? $" Grade {Grades.Name(cap + 1)} unlocks at {need:0} quality (you have {F.Quality:0})." : "";
        var held = G.CareGrade > cap ? $" Your care is good enough for {Grades.Name(Math.Min(G.CareGrade, Grades.Count - 1))}, but your milk quality holds it at {Grades.Name(cap)}." : "";
        return $"\nBest grade your milk quality allows: {Grades.Name(cap)}.{held}{next}";
    }

    /// <summary>Milk quality progress bar with what raises it.</summary>
    private void QualityBar(float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var cap = G.GradeCap;
        var need = G.NextQuality;
        var from = Grades.QualityNeed[cap];
        var t = need is { } n ? (F.Quality - from) / (n - from) : 1f;
        Ui.Icon(dl, "ribbon", p + new Vector2(17, 22), 17);
        var x = p.X + 44;
        // Label on top, bar in the middle, numbers underneath, so nothing overlaps on narrow panels
        Ui.Text(dl, fonts.Label, $"MILK QUALITY - GRADE {Grades.Name(cap)}", new Vector2(x, p.Y - 1), Theme.InkSoft);
        Ui.Bar(dl, new Vector2(x, p.Y + 24), new Vector2(p.X + width, p.Y + 32), t, Theme.GradeColor(Math.Min(cap + 1, Grades.Count - 1)));
        var numbers = need is { } nn ? $"{F.Quality:0} / {nn:0} to Grade {Grades.Name(cap + 1)}" : $"{F.Quality:0} - every grade unlocked";
        Ui.Text(dl, null, numbers, new Vector2(x, p.Y + 36), Theme.InkSoft);
        if (ImGui.IsMouseHoveringRect(p, p + new Vector2(width, 56)))
            ImGui.SetTooltip((need is { } q ? $"Grade {Grades.Name(cap + 1)} unlocks at {q:0} quality.\n" : "Every grade is unlocked.\n") +
                             $"Each oz you're milked: +{Grades.QualityPerOz:0} (+{Grades.QualityPerOzHand:0.#} by hand). Each delivery: +{Grades.QualityPerOrder:0}.\n" +
                             $"Quality treats in the Shop add more (one every {Grades.QualityTreatHours:0} hours): Cream biscuit +20, Golden alfalfa +50, Star clover +100.");
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(width, 60));
    }

    private static string Effects(FeedItem f)
    {
        var parts = new List<string>();
        if (f.Hunger != 0) parts.Add($"{f.Hunger:+0;-0} hunger");
        if (f.Thirst != 0) parts.Add($"{f.Thirst:+0;-0} thirst");
        if (f.Mood != 0) parts.Add($"{f.Mood:+0;-0} mood");
        if (f.Richness != 0) parts.Add($"{f.Richness:+0;-0} feed quality");
        if (f.Quality != 0) parts.Add($"+{f.Quality:0} milk quality");
        return string.Join(", ", parts);
    }

    private void DrawMilking(float width)
    {
        var s = G.Active;
        if (s != null)
        {
            var who = s.Kind == SessionKind.Stall
                ? $"{s.Stall!.Name} · {Catalog.StallTierNames[s.Stall.Tier]}"
                : s.Hand!.IsTest ? "Test farmhand" : $"Farmhand {s.Hand.Name}";
            var hint = s.Kind == SessionKind.Stall
                ? "Stay seated. Getting up stops the stall."
                : s.Hand!.IsTest
                    ? $"The test farmhand milks you every few seconds. {s.Strokes} strokes so far."
                    : $"{s.Hand.Name} milks you with {string.Join(", ", F.HandEmotes!)} (within {G.HandRange:0.#} y{(G.HandNeedsTarget ? ", targeting you" : "")}). {s.Strokes} so far.";
            if (ListRow(Ico(s.Kind == SessionKind.Stall ? "bucket" : "ribbon"), who, Theme.Ink,
                    new[] { new Line($"{s.Extracted * s.Yield:0.#} oz collected · Grade {Grades.Name(s.Grade)}", Theme.GradeColor(s.Grade)), new Line(hint, Theme.InkSoft) },
                    new[] { new Btn("##stop", "Stop", "cream") }, width, s.Progress, Theme.BlueDeep) == 0)
                G.Stop();
            return;
        }

        // Two columns: stalls on the left, hand milking on the right
        var col = (width - 20) / 2;
        var h = ImGui.GetContentRegionAvail().Y;
        if (ImGui.BeginChild("##mcolStall", new Vector2(col, h), false)) DrawStallColumn(ImGui.GetContentRegionAvail().X);
        ImGui.EndChild();
        ImGui.SameLine(0, 20);
        if (ImGui.BeginChild("##mcolHand", new Vector2(col, h), false)) DrawHandColumn(ImGui.GetContentRegionAvail().X);
        ImGui.EndChild();
    }

    private void DrawStallColumn(float width)
    {
        var problem = G.StallProblem();
        var status = G.Stalls.Count == 0 ? (G.Networked && !G.IsOwner ? "Your dairy's owner hasn't set up stalls yet." : "No stalls yet. Set one up on the Dairy tab.") : problem ?? $"You're in {G.Where.AtStall!.Name}.";
        var how = G.AutoStall ? "Sitting in a stall starts it by itself." : "Sit in a stall, then press Start.";
        if (ListRow(Ico("bucket"), "Stalls", Theme.Ink,
                new[] { new Line(status, problem == null ? Theme.SageDeep : Theme.InkSoft), new Line(how, Theme.InkSoft) },
                new[] { new Btn("##stall", "Start", "blue", problem == null, problem) }, width) == 0)
            G.StartStall();
    }

    private void DrawHandColumn(float width)
    {
        var hands = G.Farmhands.Where(f => G.Dev || !f.IsTest).ToList();
        if (hands.Count == 0)
        {
            ListRow(Ico("ribbon"), "Hand milking", Theme.Ink,
                new[]
                {
                    new Line(G.Networked ? "Only a farmer of your dairy can do this." : "Only a registered farmhand of your dairy can do this.", Theme.InkSoft),
                    new Line(G.Networked ? "Your dairy's owner invites farmers." : "Right-click a player > Velvet Dairy > Register as farmhand.", Theme.InkSoft),
                }, Array.Empty<Btn>(), width);
            return;
        }

        var buttons = hands.Select(fh =>
        {
            var hp = G.HandProblem(fh);
            return new Btn($"##hand{fh.Name}{fh.WorldId}", fh.IsTest ? "Test" : fh.Name.Split(' ')[0], "sage", hp == null, hp ?? $"Let {fh.Display} milk you.");
        }).ToList();

        ListRow(Ico("ribbon"), "Hand milking", Theme.Ink, new[] { new Line("Better grade and more xp. Pick who milks you:", Theme.InkSoft) }, Array.Empty<Btn>(), width);
        var clicked = Buttons(buttons, width);
        if (clicked >= 0) G.StartHand(hands[clicked]);
    }

    // =====================================================================
    // Creamery
    // =====================================================================

    private void DrawCreamery(Vector2 min, Vector2 max)
    {
        var mid = (min.X + max.X) / 2;
        Panel("##tank", min, new Vector2(mid - 7, max.Y), "Your tank", DrawTank);
        Panel("##orders", new Vector2(mid + 7, min.Y), max, "Creamery orders", DrawOrders, true);
    }

    private void DrawTank(float width)
    {
        Ui.Wrapped($"Milk you've given waits here. The creamery buys it by the cup ({Catalog.BottleOz:0} oz). Orders pay more than selling.", Theme.InkSoft, width);
        Gap(8);
        for (var g = Grades.Count - 1; g >= 0; g--)
        {
            var grade = g;
            var bottles = G.Bottles(g);
            var tip = bottles == 0 ? "You need at least a cup (8 oz) to sell." : null;
            var clicked = ListRow(c => Ui.Seal(ImGui.GetWindowDrawList(), c, 22, Grades.Name(grade), Theme.GradeColor(grade)),
                $"Grade {Grades.Name(g)} · {Grades.All[g].Blurb}", Theme.Ink,
                new[] { new Line($"{F.Tank![g]:0.#} oz · {bottles} cup{(bottles == 1 ? "" : "s")} · {G.BottlePrice(g)} coins a cup", Theme.InkSoft) },
                new[] { new Btn($"##sell1{g}", "Sell 1", "cream", bottles > 0, tip), new Btn($"##sellall{g}", "Sell all", "honey", bottles > 0, tip) }, width);
            if (clicked == 0) G.SellBottles(g, 1);
            else if (clicked == 1) G.SellBottles(g, bottles);
        }

        Gap(4);
        ImGui.TextColored(Theme.InkSoft, $"In the tank: {G.TankTotal:0.#} oz.  Price bonus: {(G.PriceBonus - 1) * 100:0}%");
    }

    private void DrawOrders(float width)
    {
        var orders = F.Orders!;
        if (orders.Count == 0) Ui.Wrapped("No orders right now.", Theme.InkSoft, width);

        foreach (var o in orders.ToList())
        {
            var have = G.OzAtOrAbove(o.MinGrade);
            var can = G.CanFill(o);
            var gradeText = o.MinGrade == 0 ? "any grade" : $"Grade {Grades.Name(o.MinGrade)} or better";
            var clicked = ListRow(Ico("bottle"), o.Customer, Theme.Ink,
                new[]
                {
                    new Line($"{o.Oz} oz, {gradeText}", Theme.InkSoft),
                    new Line($"Pays {o.Coins} coins · {o.Xp} xp", Theme.HoneyDeep),
                    new Line($"You have {Math.Min(have, o.Oz):0} of {o.Oz} oz", can ? Theme.SageDeep : Theme.InkSoft),
                },
                new[]
                {
                    new Btn($"##decl{o.Id}", "Pass", "cream", true, "Turn it down. A new order comes in 20 minutes."),
                    new Btn($"##fill{o.Id}", "Deliver", "sage", can, can ? null : "Not enough milk of that grade yet."),
                }, width, have / o.Oz, can ? Theme.SageDeep : Theme.BlueDeep);
            if (clicked == 0) G.Decline(o);
            else if (clicked == 1) G.Fill(o);
        }

        if (orders.Count < 3)
        {
            var wait = F.NextOrderUtc - DateTime.UtcNow;
            ImGui.TextColored(Theme.InkSoft, wait.TotalSeconds > 0 ? $"Next order in {Mins(wait)}." : "A new order is on its way.");
        }

        DrawNetOrders(width);
    }

    // =====================================================================
    // Shop
    // =====================================================================

    private void DrawShop(Vector2 min, Vector2 max)
    {
        var mid = (min.X + max.X) / 2;
        Panel("##feedshop", min, new Vector2(mid - 7, max.Y), "Feed and treats", DrawFeedShop);
        Panel("##upgrades", new Vector2(mid + 7, min.Y), max, "Upgrades", DrawUpgradeShop, true);
    }

    private void DrawFeedShop(float width)
    {
        Ui.Wrapped("Bought anywhere, eaten only in your dairy. Sell back what you don't need for half price, or hide it from your lists.", Theme.InkSoft, width);
        Gap(6);
        var hiddenCount = 0;
        foreach (var item in Catalog.Feed)
        {
            var hidden = F.HiddenFeed!.Contains(item.Id);
            if (hidden) hiddenCount++;
            if (hidden && !showHiddenFeed) continue;

            var locked = G.Level < item.Unlock;
            var have = G.Count(item.Id);
            var p1 = G.BuyFeedProblem(item, 1);
            var p5 = G.BuyFeedProblem(item, 5);
            var back = DairyGame.SellBackPrice(item);
            var lines = locked
                ? new[] { new Line($"Unlocks at level {item.Unlock}", Theme.InkSoft) }
                : new[]
                {
                    new Line($"{item.Price} coins · you have {have}" + (have > 0 ? $" · sells back for {back}" : ""), Theme.InkSoft),
                    new Line(Effects(item), Theme.SageDeep),
                    new Line(item.Quality > 0
                        ? G.QualityTreatWait > TimeSpan.Zero
                            ? $"Quality treat · one every {Grades.QualityTreatHours:0} h · next in {DairyGame.Wait(G.QualityTreatWait)}"
                            : $"Quality treat · one every {Grades.QualityTreatHours:0} h · ready now"
                        : "", Theme.InkSoft),
                };

            var buttons = new List<Btn>();
            if (!locked)
            {
                buttons.Add(new Btn($"##buy1{item.Id}", "Buy 1", "honey", p1 == null, p1 ?? item.Desc));
                buttons.Add(new Btn($"##buy5{item.Id}", $"5 for {item.Price * 5}", "cream", p5 == null, p5));
                if (have > 0)
                {
                    buttons.Add(new Btn($"##sell1{item.Id}", $"Sell 1 - {back}", "cream", true, $"Sell one back for {back} coins (half price)."));
                    if (have > 1) buttons.Add(new Btn($"##sellall{item.Id}", $"Sell all - {back * have}", "cream", true, $"Sell all {have} back for {back * have} coins."));
                }
            }

            buttons.Add(new Btn($"##hide{item.Id}", hidden ? "Show" : "Hide", "cream", true,
                hidden ? "Put it back in your Shop and Care lists." : "Hide it from your Shop and Care lists. Find it again under Show hidden."));

            var clicked = ListRow(Ico(item.Icon, locked || hidden ? 0.5f : 1f), item.Name + (hidden ? "  ·  hidden" : ""), locked || hidden ? Theme.InkSoft : Theme.Ink,
                lines, buttons, width);
            if (clicked < 0) continue;
            var id = buttons[clicked].Id;
            if (id.StartsWith("##buy1")) G.BuyFeed(item, 1);
            else if (id.StartsWith("##buy5")) G.BuyFeed(item, 5);
            else if (id.StartsWith("##sell1")) G.SellFeed(item, 1);
            else if (id.StartsWith("##sellall")) G.SellFeed(item, have);
            else if (id.StartsWith("##hide")) G.SetFeedHidden(item, !hidden);
        }

        if (hiddenCount > 0)
        {
            Gap(4);
            ImGui.Checkbox($"Show hidden ({hiddenCount})##showhiddenfeed", ref showHiddenFeed);
        }
    }

    private void DrawUpgradeShop(float width)
    {
        var owned = Catalog.Upgrades.Where(u => G.Owned(u.Id) >= u.MaxLevel).ToList();
        foreach (var u in Catalog.Upgrades)
        {
            var have = G.Owned(u.Id);
            var maxed = have >= u.MaxLevel;
            if (maxed && !showOwnedUpgrades) continue;
            var problem = G.UpgradeProblem(u);
            var levelText = u.MaxLevel > 1 ? $" {Math.Min(have + 1, u.MaxLevel)}/{u.MaxLevel}" : "";
            var desc = u.Id switch
            {
                "bottle" => maxed ? $"{G.BottleName}, {G.BottleSize:0} oz each"
                    : $"Now {G.BottleName} ({G.BottleSize:0} oz) → {Catalog.BottleNames[G.BottleTier + 1]} ({Catalog.BottleSizes[G.BottleTier + 1]:0} oz)",
                "rack" => maxed ? $"{G.BottleCount} bottles" : $"Now {G.BottleCount} bottle{(G.BottleCount == 1 ? "" : "s")} → {G.BottleCount + 1}. {u.Desc}",
                _ => u.Desc,
            };
            var sub = maxed ? "Owned · " + desc
                : G.Level < u.Unlocks[have] ? $"Unlocks at level {u.Unlocks[have]} · {desc}"
                : $"{u.Prices[have]} coins · {desc}";
            if (Row($"##up{u.Id}", u.Icon, u.Name + levelText + (u.Cosmetic ? "  ·  look" : ""), sub, maxed ? "Owned" : "Buy", "blue", problem == null,
                    maxed ? u.Desc : problem ?? u.Desc, width))
                G.BuyUpgrade(u);
        }

        if (owned.Count > 0)
        {
            Ui.Wrapped("Owned: " + string.Join(", ", owned.Select(u => u.Name)), Theme.SageDeep, width);
            ImGui.Checkbox("List owned upgrades##showowned", ref showOwnedUpgrades);
        }

        SubHeading("Your stalls", width);
        if (F.Stalls!.Count == 0) Ui.Wrapped("No stalls yet. Set one up on the Dairy tab.", Theme.InkSoft, width);
        foreach (var s in F.Stalls)
        {
            var problem = G.StallUpgradeProblem(s);
            var next = Math.Min(4, s.Tier + 1);
            var sub = s.Tier >= 4 ? $"{Catalog.StallSpeed[4]:0.##} oz/s · {Catalog.StallYield[4]:0.00}x yield"
                : $"Now {Catalog.StallSpeed[s.Tier]:0.##} oz/s, {Catalog.StallYield[s.Tier]:0.00}x · next {Catalog.StallSpeed[next]:0.##} oz/s, {Catalog.StallYield[next]:0.00}x for {Catalog.StallTierPrice[next]} coins";
            if (Row($"##stallup{s.Id}", "gear", $"{s.Name} · {Catalog.StallTierNames[s.Tier]}", sub, s.Tier >= 4 ? "Maxed" : "Upgrade", "blue",
                    problem == null, problem, width))
                G.UpgradeStall(s);
        }
    }

    // =====================================================================
    // Dairy (setup)
    // =====================================================================

    private void DrawDairy(Vector2 min, Vector2 max)
    {
        if (G.Networked)
        {
            DrawNetDairy(min, max);
            return;
        }

        var mid = (min.X + max.X) / 2;
        var splitY = min.Y + (max.Y - min.Y) * 0.5f;
        Panel("##place", min, new Vector2(mid - 7, splitY - 7), "Your dairy", DrawPlace);
        Panel("##stalls", new Vector2(min.X, splitY + 7), new Vector2(mid - 7, max.Y), "Stalls", DrawStalls);
        Panel("##hands", new Vector2(mid + 7, min.Y), max, "Farmhands", DrawFarmhands, true);
    }

    private void DrawPlace(float width)
    {
        var d = F.Dairy;
        if (d == null)
        {
            Ui.Wrapped("Your dairy is a house you go inside: yours, your FC's, or a friend's. Care, rest and milking only work in there.", Theme.Ink, width);
        }
        else
        {
            ImGui.TextColored(Theme.Ink, $"{d.Name}");
            ImGui.TextColored(Theme.InkSoft, $"{Housing.Describe(d.Ward, d.Plot)}{(d.WorldName.Length > 0 ? " · " + d.WorldName : "")}");
            ImGui.TextColored(G.Where.InDairy ? Theme.SageDeep : Theme.RoseDeep, G.Where.Why);
        }

        Gap(6);
        ImGui.SetNextItemWidth(Math.Min(240, width - 180));
        ImGui.InputText("##dairyname", ref dairyName, 40);
        ImGui.SameLine();
        var problem = G.DairyHereProblem();
        if (Ui.Button("##registerdairy", d == null ? "Register here" : "Move here", new Vector2(160, 30), "rose", problem == null,
                problem ?? (d == null ? "Make the house you're in your dairy." : "Move your dairy to this house. Your stalls are cleared.")))
            G.RegisterDairyHere(dairyName);
        if (d != null && dairyName != d.Name && dairyName.Trim().Length > 0)
        {
            if (Ui.Button("##rename", "Rename", new Vector2(110, 30), "cream")) G.RenameDairy(dairyName);
        }

        Gap(6);
        var radius = F.DairyRadius;
        ImGui.SetNextItemWidth(Math.Min(260, width - 10));
        if (ImGui.SliderFloat("##radius", ref radius, 0f, 40f, radius < 0.5f ? "Whole house counts" : "Within %.0f y of where you registered"))
        {
            F.DairyRadius = radius < 0.5f ? 0 : radius;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Limit the dairy to part of the house, like one room. Measured from where you stood when you registered.");
    }

    private void DrawStalls(float width)
    {
        Ui.Wrapped($"A stall is a seat in your dairy: a chair, bench or bed. Sit on it and press Add. You can have {G.MaxStalls}.", Theme.InkSoft, width);
        Gap(6);
        foreach (var s in G.Stalls.ToList())
        {
            var here = G.Where.AtStall == s;
            if (Row($"##rmstall{s.Id}", "bucket", s.Name + (here ? "  ·  you're here" : ""),
                    $"{Catalog.StallTierNames[s.Tier]} · milked you {s.Sessions} time(s)", "Remove", "cream", true, "Forget this stall.", width))
                G.RemoveStall(s);
        }

        var problem = G.StallHereProblem();
        Gap(4);
        if (Ui.Button("##addstall", "Add the seat I'm on", new Vector2(220, 36), "rose", problem == null, problem)) G.AddStallHere();
    }

    private void DrawFarmhands(float width)
    {
        Ui.Wrapped("Only a registered farmhand of your dairy can hand milk you. They don't need the plugin: you see their emotes, so they just stand close, " +
                   "target you and use a milking emote.", Theme.InkSoft, width);
        Gap(6);
        var reg = G.RegisterProblem();
        var hasTest = F.Farmhands!.Any(f => f.IsTest);
        var topButtons = new List<Btn> { new("##addtarget", "Register my target", "sage", reg == null, reg ?? "Target the player you want, then press this.") };
        if (G.Dev)
            topButtons.Add(new Btn("##addtest", "Add test farmhand", "cream", !hasTest, hasTest ? "Already added." : "Developer: a pretend farmhand that milks you every few seconds."));
        var top = Buttons(topButtons, width);
        if (top == 0) G.AddFarmhandFromTarget();
        else if (top == 1) G.AddTestFarmhand();
        Gap(4);

        foreach (var fh in F.Farmhands!.Where(f => G.Dev || !f.IsTest).ToList())
        {
            string seen;
            Vector4 seenColor;
            if (fh.IsTest)
            {
                seen = "Always ready (testing only).";
                seenColor = Theme.SageDeep;
            }
            else
            {
                var s = G.Sight(fh);
                if (!s.Present)
                {
                    seen = "Not nearby.";
                    seenColor = Theme.InkSoft;
                }
                else
                {
                    var counts = s.Emote != 0 && G.HandEmoteIds().Contains(s.Emote);
                    seen = $"{s.Distance:0.0} y away · {(s.TargetingYou ? "targeting you" : "not targeting you")} · emote: {Emotes.Name(s.Emote)}{(counts ? " (milks)" : "")}";
                    seenColor = s.Distance <= G.HandRange ? Theme.SageDeep : Theme.RoseDeep;
                }
            }

            var avatar = fh.IsTest ? "test" : "farmhand";
            var buttons = fh.IsTest
                ? new[] { new Btn($"##rmhand{fh.Name}{fh.WorldId}", "Remove", "cream") }
                : new[]
                {
                    new Btn($"##rec{fh.Name}{fh.WorldId}", "Use their emote", "cream", true, "Have them do an emote, then press this to make it a milking emote."),
                    new Btn($"##rmhand{fh.Name}{fh.WorldId}", "Remove", "cream"),
                };
            var clicked = ListRow(c => Ui.Avatar(ImGui.GetWindowDrawList(), avatar, c, 20), fh.Display, Theme.Ink,
                new[] { new Line($"Milked you {fh.Sessions} time(s), {fh.OzMilked:0} oz", Theme.InkSoft), new Line(seen, seenColor) }, buttons, width);
            if (clicked == buttons.Length - 1) G.RemoveFarmhand(fh);
            else if (clicked == 0 && !fh.IsTest) G.RecordEmote(fh);
        }

        DrawEmotes(width);
    }

    private void DrawEmotes(float width)
    {
        SubHeading("Milking emotes", width);
        string? remove = null;
        foreach (var e in F.HandEmotes!)
        {
            var id = Emotes.Find(e);
            ImGui.TextColored(id == 0 ? Theme.Warn : Theme.Ink, id == 0 ? $"{e}  (the game has no such emote)" : $"{e}  ·  {Emotes.Name(id)}");
            ImGui.SameLine(width - 70);
            if (ImGui.SmallButton($"Remove##em{e}")) remove = e;
        }

        if (remove != null && F.HandEmotes.Count > 1)
        {
            F.HandEmotes.Remove(remove);
            plugin.SaveConfig();
        }

        ImGui.SetNextItemWidth(160);
        ImGui.InputText("##newemote", ref newEmote, 30);
        ImGui.SameLine();
        if (ImGui.Button("Add emote") && newEmote.Trim().Length > 1)
        {
            var cmd = newEmote.Trim().StartsWith('/') ? newEmote.Trim() : "/" + newEmote.Trim();
            if (!F.HandEmotes.Contains(cmd)) F.HandEmotes.Add(cmd);
            newEmote = "";
            plugin.SaveConfig();
        }

    }

    // =====================================================================
    // Progress
    // =====================================================================

    private void DrawProgress(Vector2 min, Vector2 max)
    {
        var third = (max.X - min.X - 28) / 3;
        Panel("##level", min, new Vector2(min.X + third, max.Y), "Level", DrawLevel);
        Panel("##daily", new Vector2(min.X + third + 14, min.Y), new Vector2(min.X + third * 2 + 14, max.Y), "Today", DrawDaily, true);
        Panel("##awards", new Vector2(max.X - third, min.Y), max, "Awards", DrawAwards);
    }

    private void DrawLevel(float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var (lvl, into, need) = Leveling.FromXp(F.Xp);
        var p = ImGui.GetCursorScreenPos();
        Ui.Seal(dl, p + new Vector2(44, 44), 42, $"{lvl}", Theme.RoseDeep);
        Ui.Text(dl, fonts.Heading, Leveling.Rank(lvl), p + new Vector2(100, 14), Theme.Ink);
        Ui.Text(dl, null, need == 0 ? "Max level" : $"{into} / {need} xp to level {lvl + 1}", p + new Vector2(100, 44), Theme.InkSoft);
        Ui.Bar(dl, p + new Vector2(100, 68), p + new Vector2(width, 76), need == 0 ? 1 : (float)into / need, Theme.RoseDeep);
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(width, 96));
        var nextRank = Leveling.NextRank(lvl);
        if (nextRank != null) ImGui.TextColored(Theme.InkSoft, $"Next rank: {nextRank}");

        SubHeading("Milk quality", width);
        QualityBar(width);

        SubHeading("Coming up", width);
        var shown = 0;
        for (var l = lvl + 1; l <= Leveling.MaxLevel && shown < 5; l++)
        {
            var u = Leveling.UnlocksAt(l).ToList();
            if (u.Count == 0) continue;
            ImGui.TextColored(Theme.Ink, $"Lv {l}");
            ImGui.SameLine(56);
            Ui.Wrapped(string.Join(", ", u), Theme.InkSoft, width - 56);
            shown++;
        }

        if (shown == 0) ImGui.TextColored(Theme.InkSoft, "Everything is unlocked.");

        SubHeading("How to earn xp", width);
        Ui.Wrapped("Stall milking: 5 xp per oz. Hand milking: 7.5 xp per oz + 10. Orders, daily goals and care tasks give xp too.", Theme.InkSoft, width);

        SubHeading("Totals", width);
        var st = F.Stats;
        void Stat(string k, string v)
        {
            ImGui.TextColored(Theme.InkSoft, k);
            ImGui.SameLine(width * 0.6f);
            ImGui.TextColored(Theme.Ink, v);
        }

        Stat("Milk given", $"{st.TotalOz:N0} oz");
        Stat("Best milking", $"{st.BestSessionOz:0.#} oz");
        Stat("Best grade", st.BestGrade < 0 ? "-" : Grades.Name(st.BestGrade));
        Stat("Stall milkings", $"{st.StallSessions}");
        Stat("Hand milkings", $"{st.HandSessions}");
        Stat("Orders filled", $"{st.OrdersFilled}");
        Stat("Bottles sold", $"{st.BottlesSold}");
        Stat("Coins earned", $"{st.CoinsEarned:N0}");
        Stat("Care tasks", $"{st.CareTasks}");
        Stat("Milk dumped", st.TimesDumped == 0 ? "never" : $"{st.TimesDumped} time(s), {st.OzDumped:0} oz");
        if (F.EditsCaught > 0) Stat("Save edits undone", $"{F.EditsCaught}");
    }

    private void DrawDaily(float width)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.TextColored(Theme.InkSoft, $"New goals in {Mins(G.UntilReset)}.");
        Gap(4);
        var goals = G.Goals();
        for (var i = 0; i < goals.Count; i++)
        {
            var g = goals[i];
            var done = g.Have >= g.Need;
            var claimed = G.Claimed(i);
            var tip = claimed ? null : done ? $"+{DairyGame.GoalCoins} coins, +{DairyGame.GoalXp} xp" : "Not done yet.";
            if (ListRow(null, g.Name, claimed ? Theme.InkSoft : Theme.Ink, new[] { new Line($"{Math.Min(g.Have, g.Need):0} / {g.Need:0}", Theme.InkSoft) },
                    new[] { new Btn($"##claim{i}", claimed ? "Claimed" : "Claim", "sage", done && !claimed, tip) }, width,
                    g.Have / g.Need, done ? Theme.SageDeep : Theme.BlueDeep) == 0)
                G.Claim(i);
        }

        Ui.Wrapped($"Finish all four for {DairyGame.AllGoalsCoins} bonus coins.", Theme.InkSoft, width);

        SubHeading("Recent", width);
        foreach (var line in F.Log!.Take(14)) Ui.Wrapped(line, Theme.InkSoft, width);
    }

    private void DrawAwards(float width)
    {
        ImGui.TextColored(Theme.InkSoft, $"{F.Awards.Count} of {Awards.All.Count} · 25 coins each");
        Gap(6);
        foreach (var a in Awards.All)
        {
            var have = F.Awards.Contains(a.Id);
            ListRow(Ico("ribbon", have ? 1f : 0.3f), a.Name, have ? Theme.Ink : Theme.InkSoft, new[] { new Line(a.Desc, Theme.InkSoft) }, Array.Empty<Btn>(), width);
        }
    }

    // =====================================================================
    // Settings
    // =====================================================================

    private void DrawSettings(Vector2 min, Vector2 max)
    {
        var mid = (min.X + max.X) / 2;
        Panel("##prefs", min, new Vector2(mid - 7, max.Y), "Settings", DrawPrefs);
        Panel("##dev", new Vector2(mid + 7, min.Y), max, G.Dev ? "Developer" : "Help", DrawDev, true);
    }

    private void DrawPrefs(float width)
    {
        var changed = false;
        var b = F.ShowHud;
        if (ImGui.Checkbox("Show the milk bottle HUD (/vdairy hud)", ref b)) { F.ShowHud = b; changed = true; }
        b = F.LockHud;
        if (ImGui.Checkbox("Lock the HUD in place", ref b)) { F.LockHud = b; changed = true; }
        b = F.ChatEcho;
        if (ImGui.Checkbox("Echo events to my chat log (only you see it)", ref b)) { F.ChatEcho = b; changed = true; }
        b = F.NetEnabled;
        if (ImGui.Checkbox("Join the Velvet Dairy Network (dairies, invites, deliveries, online count)", ref b)) { F.NetEnabled = b; changed = true; }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Shares only what the network needs: a random id for this install, your dairy's listing, and your character name with the dairy you join.");
        b = F.ShowOwnerHud;
        if (ImGui.Checkbox("Show the owner HUD when I own a dairy", ref b)) { F.ShowOwnerHud = b; changed = true; }

        b = F.SoundOn;
        if (ImGui.Checkbox("Milking sounds (only you hear them)", ref b)) { F.SoundOn = b; changed = true; }
        var vol = F.SoundVolume;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Sound volume", ref vol, 0f, 1f, "%.2f")) { F.SoundVolume = vol; changed = true; }
        ImGui.SameLine();
        if (ImGui.SmallButton("Test")) plugin.Sounds.Play("pump_loop.wav");

        // Your own recordings
        if (ImGui.Button("Open my sounds folder")) plugin.Sounds.OpenCustomFolder();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Put your own .wav or .mp3 files here, named pump_start, pump_loop, pump_stop or squirt.\n" +
                             "They replace the built-in sounds (the loop plays over and over while a stall milks you).\n" +
                             plugin.Sounds.CustomDir);
        ImGui.SameLine();
        ImGui.TextColored(Theme.InkSoft, string.Join("  ", Sounds.Names.Select(n => $"{n}: {(plugin.Sounds.IsCustom(n) ? "yours" : "built-in")}")));
        foreach (var n in Sounds.Names)
        {
            if (ImGui.SmallButton($"Play {n.Replace('_', ' ')}##snd{n}")) plugin.Sounds.Play(n + ".wav");
            ImGui.SameLine();
        }

        ImGui.NewLine();


        SubHeading("Privacy and safety", width);
        Ui.Wrapped("Everything stays in this window and your own chat log. Nothing is posted in public chat, and the plugin never moves your character or does emotes for you. " +
                   "Farmhands are only ever people you registered yourself or your dairy invited. " +
                   "The network only shows your dairy's address when you make it public, and your name only if you choose to show it.", Theme.InkSoft, width);

        Gap(10);
        if (ImGui.Button("Withdraw 18+ confirmation"))
        {
            F.Confirmed18 = false;
            ticked18 = false;
            G.Stop();
            changed = true;
        }

        if (changed) plugin.SaveConfig();
    }

    /// <summary>Developer-only: testing buttons and the settings players can't change.</summary>
    private void DrawDevTools(float width)
    {
        var changed = false;
        var why = Plugin.PluginInterface.IsDev ? "Dev build (loaded from Dev Plugin Locations)." : "Unlocked with your developer phrase. \"/vdairy dev off\" turns it off.";
        Ui.Wrapped("Developer mode. " + why + " Players never see this panel.", Theme.BlueDeep, width);

        SubHeading("Shortcuts", width);
        if (ImGui.Button("Fill me up")) G.DevFillUp();
        ImGui.SameLine();
        if (ImGui.Button("+500 coins")) G.DevCoins();
        ImGui.SameLine();
        if (ImGui.Button("Reset needs to 80")) G.DevResetNeeds();
        if (ImGui.Button("Fake a farmhand stroke")) G.DevStroke();
        ImGui.SameLine();
        if (ImGui.Button("Add test farmhand")) G.AddTestFarmhand();
        if (ImGui.Button("Spoil my milk now")) G.DevSpoil();
        ImGui.SameLine();
        if (ImGui.Button("Spoils in 1 minute")) G.DevNearlySpoiled();
        if (ImGui.Button("+100 milk quality")) G.DevQuality(100);
        ImGui.SameLine();
        if (ImGui.Button("+1 level")) G.DevLevel();

        SubHeading("Game rules (players always get the defaults)", width);
        var b = F.Paused;
        if (ImGui.Checkbox("Pause the game (needs and milk stop changing)", ref b))
        {
            F.Paused = b;
            if (b) G.Stop("Paused.");
            changed = true;
        }

        b = F.AutoStall;
        if (ImGui.Checkbox("Sitting in a stall starts milking by itself (default on)", ref b)) { F.AutoStall = b; changed = true; }
        var speed = F.MilkSpeed;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Milk speed (default 1x)", ref speed, 0.25f, 20f, "%.2fx")) { F.MilkSpeed = speed; changed = true; }
        var stall = F.StallRange;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Stall seat range (default 1.6 y)", ref stall, 0.5f, 4f, "%.1f")) { F.StallRange = stall; changed = true; }
        var range = F.HandRange;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Farmhand range (default 3 y)", ref range, 1f, 8f, "%.1f")) { F.HandRange = range; changed = true; }
        b = F.HandNeedsTarget;
        if (ImGui.Checkbox("Farmhand must be targeting me (default on)", ref b)) { F.HandNeedsTarget = b; changed = true; }
        var srv = F.NetServerOverride;
        ImGui.SetNextItemWidth(320);
        if (ImGui.InputText("Test network server (blank = live)", ref srv, 200)) { F.NetServerOverride = srv; changed = true; }
        ImGui.TextColored(Theme.InkSoft, $"Network: {G.Net.Status}  ·  {G.Net.Api ?? "no address"}");
        if (ImGui.Button("Reset rules to defaults"))
        {
            F.Paused = false;
            F.AutoStall = true;
            F.MilkSpeed = 1f;
            F.StallRange = 1.6f;
            F.HandRange = 3f;
            F.HandNeedsTarget = true;
            changed = true;
        }

        if (changed) plugin.SaveConfig();
    }

    private void DrawDev(float width)
    {
        if (G.Dev) DrawDevTools(width);

        SubHeading("What the plugin sees", width);
        var w = G.Where;
        var me = Plugin.ObjectTable.LocalPlayer;
        var (ward, plot) = Housing.Current();
        ImGui.TextColored(Theme.Ink, $"In a house: {(w.InHouse ? Housing.Describe(ward, plot) : "no")}   Territory {Plugin.ClientState.TerritoryType}");
        ImGui.TextColored(Theme.Ink, $"In your dairy: {(w.InDairy ? "yes" : "no")}  ·  {w.Why}");
        ImGui.TextColored(Theme.Ink, $"Seated: {(w.Seated ? "yes" : "no")} (mode {Emotes.Mode(me)})  ·  Stall: {w.AtStall?.Name ?? "none"}");
        ImGui.TextColored(Theme.Ink, $"Your emote: {Emotes.Name(Emotes.Current(me))}");
        if (me != null) ImGui.TextColored(Theme.Ink, $"Position: {me.Position.X:0.0}, {me.Position.Y:0.0}, {me.Position.Z:0.0}");

        SubHeading("How to play", width);
        Ui.Wrapped("1. Dairy tab: go inside a house and press Register here.\n" +
                   "2. Sit on a chair or bed there and press Add the seat I'm on. That's your stall.\n" +
                   "3. Your milk fills up over time, faster while you rest in the dairy. Keep your needs up with the Care list.\n" +
                   "4. Sit in your stall to be milked, or have a registered farmhand milk you by hand for a better grade.\n" +
                   "5. Creamery tab: deliver orders or sell cups. Spend coins in the Shop on feed, bigger and extra bottles, and stall upgrades.", Theme.Ink, width);

        SubHeading("Credits", width);
        Ui.Wrapped("Milking machine sound: \"Milk Pump Step02\" by Erdie, freesound.org/s/49877 (cut into start, loop and stop).", Theme.InkSoft, width);
    }
}
