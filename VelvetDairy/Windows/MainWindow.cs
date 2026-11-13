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

    public const int TabBarn = 0, TabRoles = 1, TabPantry = 2, TabShop = 3, TabCreamery = 4, TabDairy = 5, TabDairies = 6, TabProgress = 7, TabSettings = 8;

    private static readonly string[] Tabs = { "Barn", "Roles", "Pantry", "Shop", "Creamery", "Dairy", "Dairies", "Progress", "Settings" };
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
        return G.MilkComing ? Ui.Flow.Filling : Ui.Flow.Still;
    }

    private const float Pad = 18f;
    private string creameryName = "";
    private float creameryRadius = 8f;

    /// <summary>Opens the window on a tab (used by the HUDs).</summary>
    public void OpenTab(int i)
    {
        tab = i;
        IsOpen = true;
    }

    /// <summary>Opens the Shop on Feed & treats with this item highlighted and scrolled into view (from the Kitchen's Buy buttons).</summary>
    public void OpenShopFor(string itemId)
    {
        tab = TabShop;
        subTabs["shop"] = 0;
        focusFeed = itemId;
        focusFeedUntil = ImGui.GetTime() + 4;
        focusScrolled = false;
        if (F.HiddenFeed!.Contains(itemId)) showHiddenFeed = true;
        IsOpen = true;
        BringToFront();
    }

    private string focusFeed = "";
    private double focusFeedUntil;
    private bool focusScrolled;

    /// <summary>Spoiled milk is tinted a sour yellow.</summary>
    public static readonly Vector4 SourMilk = new(0.93f, 0.88f, 0.60f, 1f);

    public MainWindow(Plugin plugin, Art art, Fonts fonts)
        : base("Eorzea Milkers##VelvetDairyMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
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

    /// <summary>Opening it brings your dairy's info up to date (it's checked less often while nothing shows it).</summary>
    public override void OnOpen() => plugin.Net.MineWithin(1);

    /// <summary>Another character logged in: listen to their game and forget what was typed or loaded for the last one.</summary>
    public void OnCharacterChanged()
    {
        plugin.Game.Toast += Toasts.Add;
        dairyName = F.Dairy?.Name ?? "My Dairy";
        creameryName = "";
        listDescription = "";
        listLoaded = false;
        shownMilk = -1;
        lastWholeOz = -1;
        seenDump = 0;
        ticked18 = false;
        tab = TabBarn;
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
        // Clicking in Eorzea Milkers's own windows counts as being awake (mouse movement alone doesn't)
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) G.Touch();

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
                case TabBarn: DrawBarn(bodyMin, bodyMax); break;
                case TabPantry: DrawPantry(bodyMin, bodyMax); break;
                case TabShop: DrawShop(bodyMin, bodyMax); break;
                case TabCreamery: DrawCreamery(bodyMin, bodyMax); break;
                case TabDairy: DrawDairy(bodyMin, bodyMax); break;
                case TabDairies: DrawNetwork(bodyMin, bodyMax); break;
                case TabRoles: DrawRoles(bodyMin, bodyMax); break;
                case TabProgress: DrawProgress(bodyMin, bodyMax); break;
                default: DrawSettings(bodyMin, bodyMax); break;
            }
        }

        DrawToasts(ImGui.GetForegroundDrawList(), winMin, winMax);
    }

    private float DrawHeader(ImDrawListPtr dl, Vector2 o, float width)
    {
        const float h = 92f;

        // A cream band with a rose rule under it
        dl.AddRectFilled(o, o + new Vector2(width, h), Ui.U(Theme.Paper, 0.92f));
        dl.AddLine(o + new Vector2(0, h - 4), o + new Vector2(width, h - 4), Ui.U(Theme.RoseDeep, 0.55f), 2f);
        dl.AddLine(o + new Vector2(0, h - 1), o + new Vector2(width, h - 1), Ui.U(Theme.RoseDeep, 0.3f), 1f);

        Ui.Avatar(dl, "you", o + new Vector2(Pad + 30, 44), 30);
        var tx = o.X + Pad + 74;
        Ui.Text(dl, fonts.Label, "EORZEA", new Vector2(tx, o.Y + 14), Theme.RoseDeep);
        var brandW = Ui.Text(dl, fonts.Title, "MILKERS", new Vector2(tx - 1, o.Y + 34), Theme.Ink).X;

        // Your dairy and you in it: name, your roles, the dairy's level and this week's contract
        var mx = tx + Math.Max(150, brandW + 24);
        var member = F.NetMember;
        var dairyTitle = G.Networked ? member?.Dairy ?? "No dairy yet" : F.Dairy?.Name ?? "No dairy yet";
        Ui.Text(dl, fonts.Heading, dairyTitle, new Vector2(mx, o.Y + 14), Theme.Ink);
        var px = mx;
        void Pill(string text, Vector4 color, string? tip = null, float progress = -1)
        {
            var size = Ui.Measure(fonts.Small, text);
            var a0 = new Vector2(px, o.Y + 42);
            var a1 = a0 + new Vector2(size.X + 10, size.Y + 2);
            dl.AddRectFilled(a0, a1, Ui.U(color, 0.14f), 99);
            // A role's level: how far to the next one, as a fill inside the pill
            if (progress >= 0) dl.AddRectFilled(a0, new Vector2(a0.X + (a1.X - a0.X) * Math.Clamp(progress, 0f, 1f), a1.Y), Ui.U(color, 0.22f), 99);
            dl.AddRect(a0, a1, Ui.U(color, 0.55f), 99, ImDrawFlags.None, 1f);
            Ui.Text(dl, fonts.Small, text, a0 + new Vector2(5, 1), color);
            if (tip != null && ImGui.IsMouseHoveringRect(a0, a1)) ImGui.SetTooltip(tip);
            px = a1.X + 4;
        }

        foreach (var r in G.MyRoles)
        {
            var color = r == "farmer" ? Theme.SageDeep : r == "bull" ? Theme.HoneyDeep : Theme.RoseDeep;
            var xp = r switch { "cow" => F.Xp, "farmer" => F.FarmerXp, "bull" => F.BullXp, _ => -1 };
            if (xp < 0)
            {
                Pill(r.ToUpperInvariant(), color);
                continue;
            }

            var (lvl, into, need) = Leveling.FromXp(xp);
            var name = char.ToUpperInvariant(r[0]) + r[1..];
            var tip = lvl >= Leveling.MaxLevel ? $"{name} level {lvl} (the top level)"
                : $"{name} level {lvl}\n{into:N0} / {need:N0} xp to level {lvl + 1} ({(int)(100f * into / Math.Max(1, need))}%)";
            Pill($"{r.ToUpperInvariant()} {lvl}", color, tip, lvl >= Leveling.MaxLevel ? 1f : into / (float)Math.Max(1, need));
        }
        if (G.Networked && member != null) Pill($"DAIRY LV {G.DairyLevel}", Theme.BlueDeep);
        // Where you are: in the dairy (or its private place) or away from it
        if (member != null || F.Dairy != null)
        {
            if (G.Where.InDairy)
                Pill(G.InPrivatePlace ? "PRIVATE" : G.Where.Seated ? "RESTING" : "HERE", Theme.SageDeep,
                    (G.InPrivatePlace ? "In your dairy's private place. " : G.Where.Seated ? "Resting in your dairy. " : "In your dairy. ") + G.Where.Why);
            else if (G.CreameryHere is { } cr)
                Pill("CREAMERY", Theme.BlueDeep, $"At {cr.Name}: ship your contract milk on Dairy > Contract. Milking, care and chores only work inside your dairy.");
            else Pill("AWAY", Theme.InkSoft, G.Where.Why + " Milking, care and chores only work inside the dairy.");
        }

        var con = G.Networked ? G.Net.Progress?.Contract : null;
        if (con != null)
        {
            var left = NextMonday() - DateTime.UtcNow;
            Ui.Bar(dl, new Vector2(mx, o.Y + 66), new Vector2(mx + 240, o.Y + 72), con.Progress, con.Completed ? Theme.SageDeep : Theme.BlueDeep);
            Ui.Text(dl, fonts.Small, con.Completed ? "Weekly contract done!" : $"Contract {Units.Num(con.OzDone, "0")} / {Units.Num(con.OzNeed)} {Units.Unit} · {(int)left.TotalDays}d {left.Hours}h left",
                new Vector2(mx + 248, o.Y + 61), con.Completed ? Theme.SageDeep : Theme.InkSoft);
        }
        else if (G.Networked && member == null)
            Ui.Text(dl, fonts.Small, "Start your own dairy or get invited (Dairy tab)", new Vector2(mx, o.Y + 64), Theme.RoseDeep);
        else if (!G.Networked)
            Ui.Text(dl, fonts.Small, "Playing on your own", new Vector2(mx, o.Y + 64), Theme.InkSoft);

        // Coins and where you are, right aligned
        var right = o.X + width - Pad;
        var coins = $"{F.Coins:N0}";
        var cw = Ui.Measure(fonts.Heading, coins).X;
        Ui.Icon(dl, "coin", new Vector2(right - cw - 22, o.Y + 36), 16);
        Ui.Text(dl, fonts.Heading, coins, new Vector2(right, o.Y + 24), Theme.Ink, 1f);
        // How many people are playing right now
        if (G.Net.Last != null)
        {
            var online = $"{G.Net.Last.Online:N0} online";
            var ow = Ui.Measure(fonts.Small, online).X;
            var ox = right - cw - 40 - ow;
            dl.AddCircleFilled(new Vector2(ox - 9, o.Y + 35), 4f, Ui.U(Theme.SageDeep));
            Ui.Text(dl, fonts.Small, online, new Vector2(ox, o.Y + 27), Theme.SageDeep);
        }

        // "?" opens How to play
        var qc = new Vector2(right - cw - 40 - (G.Net.Last != null ? Ui.Measure(fonts.Small, $"{G.Net.Last.Online:N0} online").X + 34 : 0) - 14, o.Y + 35);
        ImGui.SetCursorScreenPos(qc - new Vector2(13, 13));
        if (ImGui.InvisibleButton("##guidebtn", new Vector2(26, 26))) plugin.Guide.Open();
        var qh = ImGui.IsItemHovered();
        if (qh) ImGui.SetTooltip("How to play (/milkers guide)");
        dl.AddCircleFilled(qc, 12f, Ui.U(qh ? Theme.Rose : Theme.Paper));
        dl.AddCircle(qc, 12f, Ui.U(Theme.RoseDeep, 0.6f), 0, 1.5f);
        Ui.Text(dl, fonts.Small, "?", qc, Theme.RoseDeep, 0.5f, 0.5f);

        // Links under the coins: Discord (and Donate later)
        var lx = right;
        foreach (var (id, label, color, tip, open) in HeaderLinks)
        {
            var bw = Ui.Measure(fonts.Label, label.ToUpperInvariant()).X + 36;
            lx -= bw;
            if (Ui.Button(id, label, new Vector2(bw, 26), color, true, tip, new Vector2(lx, o.Y + 56))) open();
            lx -= 8;
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

    /// <summary>Link buttons in the header's bottom-right corner, from the right edge inwards (a Donate button can go here later).</summary>
    private static readonly (string Id, string Label, string Color, string Tip, Action Open)[] HeaderLinks =
    {
        ("##headdiscord", "Discord", "blue", "The Eorzea Milkers Discord (18+): questions, finding a dairy, update news and testing builds. Opens in your browser.", Plugin.OpenDiscord),
    };

    // ----- Sub-tabs: a row of small pills under the main tabs -----

    private readonly Dictionary<string, int> subTabs = new();

    private int Sub(string id) => subTabs.TryGetValue(id, out var v) ? v : 0;

    /// <summary>Draws the sub-tab pills at the top of the body and returns where the content starts.</summary>
    private Vector2 SubTabs(string id, string[] labels, Vector2 min, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var sel = Math.Clamp(Sub(id), 0, labels.Length - 1);
        const float h = 30f;
        var x = min.X;
        for (var i = 0; i < labels.Length; i++)
        {
            var size = Ui.Measure(fonts.Small, labels[i]);
            var bw = size.X + 30;
            var p = new Vector2(x, min.Y);
            ImGui.SetCursorScreenPos(p);
            if (ImGui.InvisibleButton($"##sub{id}{i}", new Vector2(bw, h))) sel = i;
            var hov = ImGui.IsItemHovered();
            var on = sel == i;
            dl.AddRectFilled(p, p + new Vector2(bw, h), Ui.U(on ? Theme.Rose : Theme.Paper, on ? 0.95f : hov ? 0.95f : 0.75f), h / 2);
            dl.AddRect(p, p + new Vector2(bw, h), Ui.U(Theme.RoseDeep, on ? 0.7f : 0.3f), h / 2, ImDrawFlags.None, 1.2f);
            Ui.Text(dl, fonts.Small, labels[i], p + new Vector2(bw / 2, h / 2), on ? Theme.Ink : hov ? Theme.RoseDeep : Theme.InkSoft, 0.5f, 0.5f);
            x += bw + 8;
        }

        subTabs[id] = sel;
        return new Vector2(min.X, min.Y + h + 10);
    }

    /// <summary>A centered column at most maxWidth wide, for screens with one list.</summary>
    private static (Vector2 Min, Vector2 Max) Column(Vector2 min, Vector2 max, float maxWidth)
    {
        var w = Math.Min(max.X - min.X, maxWidth);
        var x = min.X + (max.X - min.X - w) / 2;
        return (new Vector2(x, min.Y), new Vector2(x + w, max.Y));
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
        // Keep buttons beside the text whenever the text still gets a sensible column (it wraps)
        var inline = buttons.Count == 0 || width - iconW - btnW - 14 >= (buttons.Count == 1 ? 80 : 120);
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
            Ui.Wrapped("Eorzea Milkers is an adult roleplay game. You play the cow: keep yourself fed and comfortable inside your dairy, " +
                       "get milked in your dairy's stalls or by a farmer you trust, and get paid for every ounce.", Theme.Ink, width);
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
        // Not a cow? The barn is your work instead: the herd, the stalls, your tank and your goals
        if (!G.ProducesMilk && G.Networked && (G.IsFarmer || F.NetMember?.IsOwner == true))
        {
            DrawFarmBarn(min, max);
            return;
        }

        var blocked = G.Blocked();
        var milkH = G.Active != null || !G.AutoStall ? 200f : 112f;
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

    private void DrawFarmBarn(Vector2 min, Vector2 max)
    {
        var mid = (min.X + max.X) / 2;
        Panel("##farmherd", min, new Vector2(mid - 7, max.Y), "Farm work", w =>
        {
            DrawFarmerButtons(w);
            DrawCleanStalls(w);
        }, true);
        Panel("##farmwork", new Vector2(mid + 7, min.Y), max, G.IsFarmer ? $"Farmer level {G.FarmerLevel}" : "Your work", w =>
        {
            DrawFarmerTankSummary(w);
            SubHeading("Today's goals", w);
            DrawDaily(w);
        }, true);
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
            ImGui.SetTooltip($"Grade {Grades.Name(grade)} ({Grades.All[grade].Blurb}) if you're milked now.\nHigher grades pay more, milk faster and give more milk.\nYour grade comes from your milk quality, and stays up as long as you're milked every few days." +
                (G.GradeSlip > 0 ? $"\nSlipped {G.GradeSlip} grade{(G.GradeSlip > 1 ? "s" : "")}: it's been a while since you were milked. Get milked to bring it back." : "") +
                (cow.Taint > 0 ? "\nOff taste from dumped milk: one grade lower until it wears off." : "") +
                "\nHand milking makes up one lost grade." + GradeCapNote());

        var y = bmin.Y + bh + 6;
        var cx = min.X + w / 2;
        Ui.Text(dl, fonts.Title, $"{cow.Milk:0.#}", new Vector2(cx - 6, y), Theme.Ink, 1f);
        Ui.Text(dl, fonts.Heading, $"/ {Units.Oz(cap, "0")}", new Vector2(cx, y + 14), Theme.InkSoft);
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
        else line = $"+{Units.Oz(G.ProductionPerMinute(), "0.00")} a minute";
        var ls = Ui.Text(dl, fonts.Label, line.ToUpperInvariant(), new Vector2(cx, y), col, 0.5f);
        if (!milking && ImGui.IsMouseHoveringRect(new Vector2(cx - ls.X / 2, y), new Vector2(cx + ls.X / 2, y + ls.Y)))
        {
            var tip = "Milk speed\n" + string.Join("\n", G.RateParts().Select(r => $"  {r.Name}: x{r.Multiplier:0.00}"));
            tip += "\nFed, Watered, Comfy and Happy each add a bonus while their bar lasts; all four make you Contented. Level, treats and resting add more.";
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
        if (cow.Taint > 0) extras.Add("Off taste");
        if (G.GradeSlip > 0) extras.Add($"Grade slipped {G.GradeSlip}");
        if (G.Where.InDairy && G.Where.Seated && !milking) extras.Add("Resting");
        if (G.Contented) extras.Add("Contented");
        if (G.BoostLeft > TimeSpan.Zero) extras.Add($"Treat boost {Mins(G.BoostLeft)}");
        if (extras.Count > 0) Ui.Text(dl, null, string.Join(" · ", extras), new Vector2(cx, y), Theme.SageDeep, 0.5f);
        y += 22;

        // Row of small bottles when you own more than one
        var label = count > 1 ? $"{G.BottleName} · bottle {current + 1} of {count}" : $"{G.BottleName} bottle · {Units.Oz(size, "0")}";
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
        var rows = new (string Icon, string Label, float Value, float Bonus, Vector4 Color, string Tip)[]
        {
            ("hay", "Fed", cow.Hunger, cow.FedBonus, Theme.HoneyDeep, "Eat feed from your pantry. Better food gives a bigger bonus (wheat grain +4% up to Golden mash +15%)."),
            ("water", "Watered", cow.Thirst, cow.WaterBonus, Theme.BlueDeep, "Drink at the trough (free, +4%) or have a Mineral block (+8%)."),
            ("cushion", "Comfy", cow.Comfort, cow.ComfortBonus, Theme.RoseDeep, "Sit on furniture in your dairy and press Rest, then stay seated 15 minutes: get up sooner and that rest doesn't count. Bedding upgrades make it bigger and last longer."),
            ("clover", "Happy", cow.Mood, cow.HappyBonus, Theme.SageDeep, "Have a treat: clover, salt lick, cream clover or a quality treat."),
        };

        foreach (var r in rows)
        {
            var left = r.Value / 100f * Catalog.NeedMinutes;
            var text = r.Value > 0 && r.Bonus > 0 ? $"+{r.Bonus * 100:0}% · {(left >= 1 ? $"{left:0}m" : "<1m")}" : "plain";
            var h = Ui.Gauge(dl, new Vector2(p.X, y), width, r.Icon, r.Label, r.Value, r.Color, text);
            if (ImGui.IsMouseHoveringRect(new Vector2(p.X, y), new Vector2(p.X + width, y + h))) ImGui.SetTooltip(r.Tip);
            y += h + 10;
        }

        ImGui.SetCursorScreenPos(new Vector2(p.X, y + 2));
        var notes = new List<(string, Vector4)>();
        var where = G.Where;
        if (G.Paused) notes.Add(("The game is paused (Developer settings).", Theme.InkSoft));
        if (!where.InDairy) notes.Add((where.Why + " Care and milking wait until you're back.", Theme.RoseDeep));
        if (G.FreshHoldLeft > TimeSpan.Zero) notes.Add(($"Dry-off tonic: your grade won't slip for {DairyGame.Wait(G.FreshHoldLeft)}.", Theme.SageDeep));
        else if (G.GradeSlip > 0) notes.Add(($"Your grade slipped {G.GradeSlip} step{(G.GradeSlip > 1 ? "s" : "")} from going days without milking. One milking brings it back.", Theme.RoseDeep));
        else if (G.NextSlipUtc is { } slip && slip - DateTime.UtcNow < TimeSpan.FromDays(1))
            notes.Add(($"Get milked within {DairyGame.Wait(slip - DateTime.UtcNow)} or your grade slips. Going away? A Dry-off tonic holds it.", Theme.RoseDeep));
        if (G.Spoiled) notes.Add(("Your milk spoiled from sitting too long. Dump it (under the bottle or on the HUD) before you can be milked.", Theme.Warn));
        else if (G.Overfull)
        {
            var left = G.SpoilsIn;
            notes.Add((left.HasValue ? $"You're full and it's getting uncomfortable. It spoils in {Mins(left.Value)} if you don't get milked." : "You're full and it's getting uncomfortable.", Theme.Warn));
        }

        if (cow.Taint > 0)
            notes.Add(("Your milk tastes off after the dump (one grade lower). Care tasks, milkings and creamery deliveries wear it off.", Theme.RoseDeep));
        if (notes.Count == 0) notes.Add((G.Contented ? "Contented! Every bonus is going." : "Each use adds 30 minutes to a bar (up to an hour), then it drains as the bonus wears off. Bars wait while you're asleep or away, and running out never hurts.", Theme.SageDeep));
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
        var wcd = G.DrinkWait;
        if (Row("##drink", "water", "Drink", wcd > TimeSpan.Zero ? $"Ready in {DairyGame.Wait(wcd)}" : $"Free · Watered +{Catalog.TroughBonus * 100:0}%, +30m", "Drink", "blue",
                ok && wcd <= TimeSpan.Zero, problem, width))
            G.Drink();

        var rcd = G.RestWait;
        var restProblem = G.RestProblem();
        var settle = G.RestSettleLeft;
        if (Row("##rest", "cushion", "Rest", settle > TimeSpan.Zero ? $"Stay seated {DairyGame.Wait(settle)} to keep it" : rcd > TimeSpan.Zero ? $"Ready in {DairyGame.Wait(rcd)}" : restProblem != null && ok ? "Sit on furniture first" : $"Free · Comfy +{G.ComfyNow * 100:0}%, +30m", "Rest", "rose",
                restProblem == null && rcd <= TimeSpan.Zero, restProblem, width))
            G.Rest();

        var shown = 0;
        foreach (var item in Catalog.Feed)
        {
            if (G.Level < item.Unlock || F.HiddenFeed!.Contains(item.Id)) continue;
            var have = G.Count(item.Id);
            if (have <= 0) continue;   // Care only lists what you have right now (the Pantry can show the rest)
            shown++;
            var wait = item.HoldDays > 0 ? TimeSpan.Zero : G.FeedWait(item);
            var qwait = item.Quality > 0 ? G.QualityTreatWait : TimeSpan.Zero;
            var qualityHeld = qwait > wait;
            if (qualityHeld) wait = qwait;
            var waiting = wait > TimeSpan.Zero;
            var tip = problem ?? (have <= 0 ? "Buy more in the Shop." : qualityHeld ? $"Quality treats share a {Grades.QualityTreatHours:0}-hour cooldown." :
                waiting ? $"Eating, drinking, resting and treats each wait {Catalog.NeedCooldownMinutes:0} minutes between uses." : item.Desc);
            var sub = waiting ? $"{have} left · ready in {DairyGame.Wait(wait)}" : $"{have} left · {Effects(item)}";
            if (Row($"##feed{item.Id}", item.Icon, item.Name, sub, "Eat", "honey", ok && have > 0 && !waiting, tip, width))
                G.UseFeed(item.Id);
        }

        if (shown == 0) Ui.Wrapped("Your pantry is empty. Buy feed in the Shop.", Theme.InkSoft, width);
        Gap(4);
        if (ImGui.SmallButton("Open pantry##carepantry")) tab = TabPantry;
        ImGui.SameLine();
        if (ImGui.SmallButton("Shop##careshop")) tab = TabShop;
    }

    /// <summary>Tooltip line about how far your milk quality lets your grade go.</summary>
    private string GradeCapNote()
    {
        var cap = G.GradeCap;
        var next = G.NextQuality is { } need ? $" Grade {Grades.Name(cap + 1)} unlocks at {need:0} quality (you have {F.Quality:0})." : "";
        return $"\nBest grade your milk quality allows: {Grades.Name(cap)}.{next}";
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
                             $"Quality treats in the Shop add more (one every hour): Cream biscuit +20, Golden alfalfa +50, Star clover +100.");
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(width, 60));
    }

    private static string Effects(FeedItem f)
    {
        var parts = new List<string>();
        if (f.Fed != 0) parts.Add($"Fed +{f.Fed * 100:0}%");
        if (f.Water != 0) parts.Add($"Watered +{f.Water * 100:0}%");
        if (f.Happy != 0) parts.Add($"Happy +{f.Happy * 100:0}%");
        if (f.Fed != 0 || f.Water != 0 || f.Happy != 0) parts.Add("+30m");
        if (f.BoostMinutes != 0) parts.Add($"boost {f.BoostMinutes}m");
        if (f.HoldDays != 0) parts.Add($"no grade slip for {f.HoldDays} days");
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
                    : $"{s.Hand.Name} is milking you while they keep their Milk emote going (within {G.HandRange:0.#} y{(G.HandNeedsTarget ? ", targeting you" : "")}).";
            if (ListRow(Ico(s.Kind == SessionKind.Stall ? "bucket" : "ribbon"), who, Theme.Ink,
                    new[] { new Line($"{Units.Oz(s.Extracted * s.Yield, "0.#")} collected · Grade {Grades.Name(s.Grade)} cow", Theme.GradeColor(s.Grade)), new Line(hint, Theme.InkSoft) },
                    new[] { new Btn("##stop", "Stop", "cream") }, width, s.Progress, Theme.BlueDeep) == 0)
                G.Stop();
            return;
        }

        // Not being milked: one short line, plus Start buttons only when you've turned off starting by itself
        var how = !G.Where.InDairy ? "Not being milked. Go to your dairy to get milked."
            : G.Stalls.Count == 0 ? "Not being milked. Your dairy has no stalls yet; a farmer can still hand milk you."
            : "Not being milked. Sit on a stall, or have a farmer stand close and press Milk on you.";
        ImGui.TextColored(Theme.InkSoft, how);
        var buttons = new List<Btn>();
        var starts = new List<Action>();
        if (!G.AutoStall)
        {
            var problem = G.StallProblem();
            buttons.Add(new Btn("##stall", "Start stall", "blue", problem == null, problem ?? "Starts the stall you're sitting on."));
            starts.Add(() => G.StartStall());
        }

        if (buttons.Count == 0) return;
        Gap(4);
        var clicked = Buttons(buttons, width);
        if (clicked >= 0) starts[clicked]();
    }

    // =====================================================================
    // Creamery
    // =====================================================================

    private void DrawCreamery(Vector2 min, Vector2 max)
    {
        var labels = G.CanCarry ? new[] { "Your pay", "Customer orders", "Deliveries" } : new[] { "Your pay" };
        var top = SubTabs("creamery", labels, min, max.X - min.X);
        var (cmin, cmax) = Column(top, max, 760);
        switch (G.CanCarry ? Sub("creamery") : 0)
        {
            case 0: Panel("##pay", cmin, cmax, "Your pay", DrawPay); return;
            case 1: Panel("##orders", cmin, cmax, "Creamery orders", DrawOrders, true); return;
            default: Panel("##netorders", cmin, cmax, "Deliveries to other dairies", DrawNetOrders, true); return;
        }
    }

    /// <summary>What the dairy pays per oz, by grade, the moment you're milked.</summary>
    private void DrawPay(float width)
    {
        Ui.Wrapped("You don't sell milk yourself: the dairy pays you for every ounce the moment you're milked, at a stall or by a farmer's hand. " +
                   "The milk goes into the dairy's tank, and farmers and the owner deliver it to customers.", Theme.InkSoft, width);
        Gap(8);
        var cur = G.CurrentGrade;
        for (var g = Grades.Count - 1; g >= 0; g--)
        {
            var grade = g;
            ListRow(c => Ui.Seal(ImGui.GetWindowDrawList(), c, 22, Grades.Name(grade), Theme.GradeColor(grade)),
                $"Grade {Grades.Name(g)} · {Grades.All[g].Blurb}" + (g == cur ? "  ·  yours now" : ""), g == cur ? Theme.RoseDeep : Theme.Ink,
                new[] { new Line(Units.PerOz(G.PayRate(g)) + (g > 0 ? $" · milks {Catalog.GradeBoost[g] * 100:0}% faster, {Catalog.GradeBoost[g] * 100:0}% more milk" : ""), Theme.HoneyDeep) }, Array.Empty<Btn>(), width);
        }

        Gap(4);
        var bonus = (G.DairyPayBonus - 1) * 100;
        ImGui.TextColored(Theme.InkSoft, $"Dairy level bonus: +{bonus:0}% · your looks bonus: +{(G.PriceBonus - 1) * 100:0}%");
        if (G.Networked) ImGui.TextColored(Theme.InkSoft, "Each dairy level raises pay by 2%. Hand milking also pays your farmer a share (not taken from you).");
        ImGui.TextColored(Theme.InkSoft, $"Milk given today: {Units.Oz(F.DayOz, "0.#")}" + (F.DayOz >= Catalog.DailyPaidOz ? $" · paid for {Units.Oz(Catalog.DailyPaidOz, "N0")} (the most a day); more still gives xp" : ""));
        var rate = Catalog.TaperRate(F.DayEarned);
        ImGui.TextColored(rate < 1f ? Theme.HoneyDeep : Theme.InkSoft,
            rate >= 1f ? $"Earned today: {F.DayEarned:N0} · full pay until {Catalog.FullEarnings:N0}"
                : rate >= 0.5f ? $"Earned today: {F.DayEarned:N0} · pay is at half until {Catalog.HalfEarnings:N0}, then a tenth"
                : $"Earned today: {F.DayEarned:N0} · pay is at a tenth until the daily reset");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Everything the game pays you in a day (milking, hand milking shares, orders, goals, awards) counts together.\n" +
                             $"The first {Catalog.FullEarnings:N0} coins are paid in full, then half up to {Catalog.HalfEarnings:N0}, then a tenth.\n" +
                             "XP is never reduced, and coins from other players (shelf sales, buy orders) aren't counted. Resets with the daily reset.");
    }

    /// <summary>One line on what you carry, with a button to the Kitchen where jugs are filled.</summary>
    internal void JugsNote(float width)
    {
        ImGui.TextColored(Theme.Ink, $"Your jugs: {Units.Num(G.Carried, "0.#")} / {Units.Num(G.MaxCarry)} {Units.Unit}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Fill jugs##jugsnote")) plugin.Kitchen.IsOpen = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Jugs are filled from the dairy tank at the top of the Kitchen window.");
        Gap(4);
    }

    private void DrawOrders(float width)
    {
        var orders = F.Orders!;
        JugsNote(width);
        if (orders.Count == 0) Ui.Wrapped("No orders right now.", Theme.InkSoft, width);

        foreach (var o in orders.ToList())
        {
            var have = G.Carried;
            var can = G.CanFill(o);
            var clicked = ListRow(Ico("bottle"), o.Customer, Theme.Ink,
                new[]
                {
                    new Line($"{Units.Oz(o.Oz)} of milk", Theme.InkSoft),
                    new Line($"Pays {o.Coins} coins · {o.Xp} xp", Theme.HoneyDeep),
                    new Line($"You have {Units.Num(Math.Min(have, o.Oz), "0")} of {Units.Num(o.Oz)} {Units.Unit}", can ? Theme.SageDeep : Theme.InkSoft),
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

    }

    // =====================================================================
    // Shop
    // =====================================================================

    private void DrawShop(Vector2 min, Vector2 max)
    {
        var sub = SubTabs("shop", G.CanCarry ? new[] { "Feed & treats", "Upgrades", "Bottles", "Farmer tank" } : new[] { "Feed & treats", "Upgrades", "Bottles" }, min, max.X - min.X);
        var mid = (min.X + max.X) / 2;
        switch (Sub("shop"))
        {
            case 0:
                Panel("##feedshop", sub, new Vector2(mid - 7, max.Y), "Feed", w => DrawFeedShop(w, false));
                Panel("##treatshop", new Vector2(mid + 7, sub.Y), max, "Quality treats", w => DrawFeedShop(w, true), true);
                break;
            case 1:
                Panel("##upgrades", sub, new Vector2(mid - 7, max.Y), "Comfort and care", w => DrawUpgradeShop(w, false));
                Panel("##looks", new Vector2(mid + 7, sub.Y), max, "Things to wear", w => DrawUpgradeShop(w, true), true);
                break;
            case 2:
                Panel("##bottleshop", sub, new Vector2(mid - 7, max.Y), "Bottles", DrawBottleShop);
                Panel("##bottlenow", new Vector2(mid + 7, sub.Y), max, "Your bottles now", DrawBottleSummary, true);
                break;
            default:
                Panel("##tankshop", sub, new Vector2(mid - 7, max.Y), "Farmer tank", DrawFarmerTankShop);
                Panel("##tanknow", new Vector2(mid + 7, sub.Y), max, "What you carry now", DrawFarmerTankSummary, true);
                break;
        }
    }

    private static readonly string[] BottleUpgrades = { "bottle", "rack", "farmtank" };

    private void DrawFarmerTankShop(float width)
    {
        Ui.Wrapped("Farmers and owners carry milk from the dairy tank to customers, other dairies and the Grand Creamery. A bigger tank carries more per trip. New sizes unlock with your farmer level.",
            Theme.InkSoft, width);
        Gap(6);
        UpgradeRow(Catalog.Upgrades.First(u => u.Id == "farmtank"), width);
        Gap(6);
        SubHeading("Sizes", width);
        var tank = Catalog.Upgrades.First(u => u.Id == "farmtank");
        for (var i = 0; i < Catalog.FarmerTankSizes.Length; i++)
        {
            var owned = i <= G.FarmerTankTier;
            ImGui.TextColored(owned ? Theme.SageDeep : Theme.InkSoft,
                $"{Catalog.FarmerTankNames[i]} · {Units.Oz(Catalog.FarmerTankSizes[i])}" + (i == 0 ? " · everyone starts with it" : $" · farmer level {tank.Unlocks[i - 1]}, {tank.Prices[i - 1]:N0} coins"));
        }
    }

    private void DrawFarmerTankSummary(float width)
    {
        ImGui.TextColored(Theme.Ink, $"{G.FarmerTankName} · holds {Units.Oz(G.MaxCarry)}");
        ImGui.TextColored(Theme.InkSoft, $"Carrying {Units.Oz(G.Carried, "0.#")} · farmer level {G.FarmerLevel}");
        var cur = ImGui.GetCursorScreenPos();
        Ui.Bar(ImGui.GetWindowDrawList(), cur + new Vector2(0, 6), cur + new Vector2(Math.Min(width, 360), 14), G.MaxCarry > 0 ? Math.Clamp(G.Carried / G.MaxCarry, 0, 1) : 0, Theme.BlueDeep);
        ImGui.Dummy(new Vector2(width, 20));
        Ui.Wrapped("Fill it at your dairy (HUD > Kitchen). Farmer xp comes from hand milking, grooming, cleaning stalls, baking, filling jugs, deliveries and contracts.", Theme.InkSoft, width);
    }

    private void DrawBottleShop(float width)
    {
        Ui.Wrapped("Bigger bottles hold more each; extra bottles start filling when the one before is full.", Theme.InkSoft, width);
        Gap(6);
        foreach (var u in Catalog.Upgrades.Where(u => u.Id is "bottle" or "rack")) UpgradeRow(u, width);
    }

    private void DrawBottleSummary(float width)
    {
        ImGui.TextColored(Theme.Ink, $"{G.BottleCount} × {G.BottleName} ({Units.Oz(G.BottleSize, "0")})");
        ImGui.TextColored(Theme.InkSoft, $"Holds {Units.Oz(G.Capacity, "0")} in all");
        var rate = G.ProductionPerMinute();
        if (rate > 0) ImGui.TextColored(Theme.InkSoft, $"Fills from empty in about {DairyGame.Wait(TimeSpan.FromMinutes(G.Capacity / rate))} at your current speed");
        Gap(6);
        var cur = ImGui.GetCursorScreenPos();
        var bh = 150f;
        for (var i = 0; i < G.BottleCount; i++)
        {
            var full = Math.Clamp((F.Cow.Milk - i * G.BottleSize) / G.BottleSize, 0f, 1f);
            Ui.Bottle(ImGui.GetWindowDrawList(), cur + new Vector2(i * (bh / 2 + 10), 0), new Vector2(bh / 2, bh), full, Ui.Flow.Still, null, G.BottleTier);
        }

        ImGui.Dummy(new Vector2(width, bh + 6));
    }

    private void DrawFeedShop(float width, bool treats)
    {
        Ui.Wrapped(treats
                ? $"Treats raise your milk quality toward the next grade. You can have one every hour."
                : "Bought anywhere, eaten only in your dairy. Manage what you own in the Pantry.", Theme.InkSoft, width);
        Gap(6);
        var hiddenCount = 0;
        foreach (var item in Catalog.Feed)
        {
            if (item.Baked || (item.Quality > 0) != treats) continue;   // baked treats come off the dairy shelf
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
            }

            var rowTop = ImGui.GetCursorScreenPos();
            var focused = item.Id == focusFeed && ImGui.GetTime() < focusFeedUntil;
            if (focused && !focusScrolled)
            {
                ImGui.SetScrollHereY(0.2f);
                focusScrolled = true;
            }

            var clicked = ListRow(Ico(item.Icon, locked || hidden ? 0.5f : 1f), item.Name + (hidden ? "  ·  hidden" : ""), locked || hidden ? Theme.InkSoft : Theme.Ink,
                lines, buttons, width);
            if (focused)
            {
                // The item you came to buy: a soft honey glow that fades out
                var a = (float)Math.Clamp((focusFeedUntil - ImGui.GetTime()) / 4, 0, 1);
                ImGui.GetWindowDrawList().AddRect(rowTop - new Vector2(4, 2), new Vector2(rowTop.X + width + 4, ImGui.GetCursorScreenPos().Y), Ui.U(Theme.HoneyDeep, 0.9f * a), 8, ImDrawFlags.None, 2.5f);
            }
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
            ImGui.Checkbox($"Show hidden ({hiddenCount})##showhiddenfeed{treats}", ref showHiddenFeed);
        }
    }

    private void DrawUpgradeShop(float width, bool looks)
    {
        var list = Catalog.Upgrades.Where(u => u.Cosmetic == looks && !BottleUpgrades.Contains(u.Id) && !(u.Id == "stallkit" && G.Networked)).ToList();
        var owned = list.Where(u => G.Owned(u.Id) >= u.MaxLevel).ToList();
        foreach (var u in list)
        {
            if (G.Owned(u.Id) >= u.MaxLevel && !showOwnedUpgrades) continue;
            UpgradeRow(u, width);
        }

        if (owned.Count > 0)
        {
            Ui.Wrapped("Owned: " + string.Join(", ", owned.Select(u => u.Name)), Theme.SageDeep, width);
            ImGui.Checkbox($"List owned##showowned{looks}", ref showOwnedUpgrades);
        }
    }

    private void UpgradeRow(Upgrade u, float width)
    {
        {
            var have = G.Owned(u.Id);
            var maxed = have >= u.MaxLevel;
            var problem = G.UpgradeProblem(u);
            var levelText = u.MaxLevel > 1 ? $" {Math.Min(have + 1, u.MaxLevel)}/{u.MaxLevel}" : "";
            var desc = u.Id switch
            {
                "bottle" => maxed ? $"{G.BottleName}, {Units.Oz(G.BottleSize, "0")} each"
                    : $"Now {G.BottleName} ({Units.Oz(G.BottleSize, "0")}) → {Catalog.BottleNames[G.BottleTier + 1]} ({Units.Oz(Catalog.BottleSizes[G.BottleTier + 1], "0")})",
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
    }

    /// <summary>Stall upgrades (Dairy > Stalls). On the network only the owner, paid from the dairy fund.</summary>
    private void DrawStallUpgrades(float width)
    {
        if (G.Networked && !G.IsOwner) return;
        var prog = G.Networked ? G.Net.Progress : null;
        SubHeading(G.Networked ? "Dairy stalls (paid from the fund)" : "Your stalls", width);
        if (prog != null) ImGui.TextColored(Theme.HoneyDeep, $"Dairy fund: {prog.Fund:N0} coins · best tier at level {prog.Level}: {Catalog.StallTierNames[prog.MaxTier]}");
        if (G.Stalls.Count == 0) Ui.Wrapped("No stalls yet. Set one up on the Dairy tab.", Theme.InkSoft, width);
        foreach (var s in G.Stalls.ToList())
        {
            var problem = G.StallUpgradeProblem(s);
            var next = Math.Min(4, s.Tier + 1);
            var sub = s.Tier >= 4 ? $"{Units.Oz(Catalog.StallSpeed[4], "0.##")}/s · {Catalog.StallYield[4]:0.00}x yield"
                : $"Now {Units.Oz(Catalog.StallSpeed[s.Tier], "0.##")}/s, {Catalog.StallYield[s.Tier]:0.00}x · next {Units.Oz(Catalog.StallSpeed[next], "0.##")}/s, {Catalog.StallYield[next]:0.00}x for {(prog != null ? $"{prog.TierPrice(next)} fund coins" : $"{Catalog.StallTierPrice[next]} coins")}";
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

        var top = SubTabs("dairylocal", new[] { "Overview", "Stalls", "Farmhands" }, min, max.X - min.X);
        var mid = (min.X + max.X) / 2;
        switch (Sub("dairylocal"))
        {
            case 0:
                var (cmin, cmax) = Column(top, max, 760);
                Panel("##place", cmin, cmax, "Your dairy", DrawPlace);
                break;
            case 1:
                Panel("##stalls", top, new Vector2(mid - 7, max.Y), "Stalls", DrawStalls);
                Panel("##stallups", new Vector2(mid + 7, top.Y), max, "Stall upgrades", DrawStallUpgrades, true);
                break;
            default:
                var (fmin, fmax) = Column(top, max, 760);
                Panel("##hands", fmin, fmax, "Farmhands", DrawFarmhands, true);
                break;
        }
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
        if (G.Networked)
        {
            var pr = G.Net.Progress;
            Ui.Wrapped(G.IsOwner
                ? $"A stall is a seat in your dairy or its private place: a chair, bench or bed. Sit on it and press Add. " +
                  $"Put as many as you like wherever you like (up to {G.MaxStalls}); the next costs {pr?.NextStallPrice ?? 0:N0} coins from the dairy fund, and each one costs more than the last."
                : "Your dairy's stalls. Sit on one to get milked.", Theme.InkSoft, width);
        }
        else Ui.Wrapped($"A stall is a seat in your dairy: a chair, bench or bed. Sit on it and press Add. You can have {G.MaxStalls}.", Theme.InkSoft, width);
        Gap(6);
        foreach (var s in G.Stalls.ToList())
        {
            var here = G.Where.AtStall == s;
            var inArea = !G.Networked || G.StallInArea(s);
            var owner = !G.Networked || G.IsOwner;
            var buttons = new List<Btn>();
            if (owner)
            {
                var mp = G.MoveStallProblem(s);
                buttons.Add(new Btn($"##mvstall{s.Id}", "Move here", "blue", mp == null, mp ?? "Move this stall to the seat you're sitting on. It keeps its name and tier."));
                buttons.Add(new Btn($"##rmstall{s.Id}", "Remove", "cream", true, "Forget this stall."));
            }

            var clicked = ListRow(Ico("bucket"), s.Name + (here ? "  ·  you're here" : ""), inArea ? Theme.Ink : Theme.InkSoft,
                new[]
                {
                    new Line($"{Catalog.StallTierNames[s.Tier]} · milked you {s.Sessions} time(s)", Theme.InkSoft),
                    new Line(inArea ? "" : "Outside the dairy's area: it won't work until you move it.", Theme.RoseDeep),
                }, buttons, width);
            if (clicked == 0) G.MoveStall(s);
            else if (clicked == 1) G.RemoveStall(s);
        }

        if (G.Networked && !G.IsOwner) return;
        var problem = G.StallHereProblem();
        Gap(4);
        if (Ui.Button("##addstall", "Add the seat I'm on", new Vector2(220, 36), "rose", problem == null, problem)) G.AddStallHere();
    }

    private void DrawFarmhands(float width)
    {
        Ui.Wrapped("Only a farmer of your dairy can hand milk you: they stand close, target you and press Milk (HUD > Herd Care). " +
                   "Just doing an emote near you never counts. It flows until you're empty.", Theme.InkSoft, width);
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
                new[] { new Line($"Milked you {fh.Sessions} time(s), {Units.Oz(fh.OzMilked, "0")}", Theme.InkSoft), new Line(seen, seenColor) }, buttons, width);
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
            if (cmd.Equals("/kneel", StringComparison.OrdinalIgnoreCase)) G.Notify("/kneel doesn't work for hand milking. Try /beesknees or another dance.", 2);
            else if (!F.HandEmotes.Contains(cmd)) F.HandEmotes.Add(cmd);
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
        Panel("##level", min, new Vector2(min.X + third, max.Y), "Your levels", DrawLevelsSummary);
        Panel("##daily", new Vector2(min.X + third + 14, min.Y), new Vector2(min.X + third * 2 + 14, max.Y), "Today", DrawDaily, true);
        Panel("##awards", new Vector2(max.X - third, min.Y), max, "Awards", DrawAwards);
    }

    /// <summary>A small level line for one role (each role levels on its own).</summary>
    private void DrawRoleLevel(string title, int xp, Vector4 color, string about, float width)
    {
        var (lvl, into, need) = Leveling.FromXp(xp);
        SubHeading($"{title} {lvl}", width);
        var p = ImGui.GetCursorScreenPos();
        Ui.Bar(ImGui.GetWindowDrawList(), p + new Vector2(0, 4), p + new Vector2(width, 12), need == 0 ? 1 : (float)into / need, color);
        ImGui.Dummy(new Vector2(width, 16));
        ImGui.TextColored(Theme.InkSoft, need == 0 ? "Max level" : $"{into} / {need} xp");
        Ui.Wrapped(about, Theme.InkSoft, width);
    }

    private void DrawCowLevel(float width)
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

        SubHeading("Coming up", width);
        var shown = 0;
        for (var l = lvl + 1; l <= Leveling.MaxLevel && shown < 5; l++)
        {
            var u = Leveling.UnlocksAt(l, G.Networked).ToList();
            if (u.Count == 0) continue;
            ImGui.TextColored(Theme.Ink, $"Lv {l}");
            ImGui.SameLine(56);
            Ui.Wrapped(string.Join(", ", u), Theme.InkSoft, width - 56);
            shown++;
        }

        if (shown == 0) ImGui.TextColored(Theme.InkSoft, "Everything is unlocked.");

        SubHeading("How to earn cow xp", width);
        Ui.Wrapped($"+1 xp for every {Units.Oz(0.2)} of milk you make (more at better grades), a bonus when you're emptied at a stall or by hand, daily goals and care.", Theme.InkSoft, width);
    }

    /// <summary>Progress tab: every role's level at a glance (details on the Roles tab), plus totals.</summary>
    private void DrawLevelsSummary(float width)
    {
        DrawRoleLevel("Cow level", F.Xp, Theme.RoseDeep, Leveling.Rank(G.Level), width);
        var nextPerk = Leveling.FarmerPerks.FirstOrDefault(p => p.Level > G.FarmerLevel);
        DrawRoleLevel("Farmer level", F.FarmerXp, Theme.SageDeep,
            (Leveling.FarmerRank(G.FarmerLevel) is { } ft ? ft + " · " : "") + $"{G.FarmerTankName} · carries {Units.Oz(G.MaxCarry)}" +
            (nextPerk.Name != null ? $" · next at {nextPerk.Level}: {nextPerk.Name}" : ""), width);
        DrawRoleLevel("Bull level", F.BullXp, Theme.HoneyDeep,
            Leveling.BullRank(G.BullLevel) is { } bt ? $"{bt} · titles at 25, 50, 75 and 100" : "Titles at 25, 50, 75 and 100. More is coming in new updates.", width);
        Gap(4);
        if (ImGui.SmallButton("Details on the Roles tab##toroles")) tab = TabRoles;

        SubHeading("Totals", width);
        var st = F.Stats;
        void Stat(string k, string v)
        {
            ImGui.TextColored(Theme.InkSoft, k);
            ImGui.SameLine(width * 0.6f);
            ImGui.TextColored(Theme.Ink, v);
        }

        Stat("Milk given", $"{Units.Oz(st.TotalOz, "N0")}");
        Stat("Best milking", $"{Units.Oz(st.BestSessionOz, "0.#")}");
        Stat("Best grade", st.BestGrade < 0 ? "-" : Grades.Name(st.BestGrade));
        Stat("Stall milkings", $"{st.StallSessions}");
        Stat("Hand milkings", $"{st.HandSessions}");
        Stat("Orders filled", $"{st.OrdersFilled}");
        Stat("Bottles sold", $"{st.BottlesSold}");
        Stat("Coins earned", $"{st.CoinsEarned:N0}");
        Stat("Care tasks", $"{st.CareTasks}");
        Stat("Milk dumped", st.TimesDumped == 0 ? "never" : $"{st.TimesDumped} time(s), {Units.Oz(st.OzDumped, "0")}");
        if (F.EditsCaught > 0) Stat("Save edits undone", $"{F.EditsCaught}");
    }

    private void DrawDaily(float width)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.TextColored(Theme.InkSoft, $"New goals in {Mins(G.UntilReset)}.");
        Gap(4);
        var goals = G.Goals();
        if (goals.Count == 0) Ui.Wrapped("Take a role (Roles tab) to get daily goals.", Theme.InkSoft, width);
        foreach (var farmer in new[] { false, true })
        {
            var set = goals.Where(x => x.Farmer == farmer).ToList();
            if (set.Count == 0) continue;
            if (goals.Any(x => x.Farmer != farmer)) SubHeading(farmer ? "Farmer goals" : "Cow goals", width);
            foreach (var g in set)
            {
                var done = g.Have >= g.Need;
                var claimed = G.Claimed(g);
                var tip = claimed ? null : done ? $"+{DairyGame.GoalCoins} coins, +{DairyGame.GoalXp} {(g.Farmer ? "farmer" : "cow")} xp" : "Not done yet.";
                if (ListRow(null, g.Name, claimed ? Theme.InkSoft : Theme.Ink, new[] { new Line($"{Math.Min(g.Have, g.Need):0} / {g.Need:0}", Theme.InkSoft) },
                        new[] { new Btn($"##claim{g.Bit}", claimed ? "Claimed" : "Claim", "sage", done && !claimed, tip) }, width,
                        g.Have / g.Need, done ? Theme.SageDeep : Theme.BlueDeep) == 0)
                    G.Claim(g);
            }

            Ui.Wrapped($"Finish all four {(farmer ? "farmer" : "cow")} goals for {DairyGame.AllGoalsCoins} bonus coins.", Theme.InkSoft, width);
        }

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
        if (ImGui.Checkbox("Show the milk bottle HUD (/milkers hud)", ref b)) { F.ShowHud = b; changed = true; }
        b = F.LockHud;
        if (ImGui.Checkbox("Lock the HUD in place", ref b)) { F.LockHud = b; changed = true; }
        b = F.ChatEcho;
        if (ImGui.Checkbox("Echo events to my chat log (only you see it)", ref b)) { F.ChatEcho = b; changed = true; }
        b = F.NetEnabled;
        if (ImGui.Checkbox("Join the Eorzea Milkers Network (dairies, invites, deliveries, online count)", ref b)) { F.NetEnabled = b; changed = true; }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Shares only what the network needs: a random id for this install, your dairy's listing, and your character name with the dairy you join.");
        b = F.ShowOwnerHud;
        if (ImGui.Checkbox("Show the owner HUD when I own a dairy", ref b)) { F.ShowOwnerHud = b; changed = true; }

        b = F.ShowStallMarkers;
        if (ImGui.Checkbox("Show stall markers (only you see them)", ref b)) { F.ShowStallMarkers = b; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A small green dot above each of your dairy's stalls while you're in the dairy, so you can see which seats milk you.");
        if (F.ShowStallMarkers)
        {
            var here = G.StallsHere().Count();
            ImGui.Indent();
            ImGui.TextColored(Theme.InkSoft, !G.Where.InDairy ? "Markers show once you're in your dairy."
                : here > 0 ? $"Marking {here} stall{(here == 1 ? "" : "s")} here{(G.InPrivatePlace ? " (private place)" : "")}."
                : G.Stalls.Count == 0 ? "Your dairy has no stalls yet." : $"None of your {G.Stalls.Count} stall(s) are in this part of the dairy.");
            ImGui.Unindent();
        }

        b = F.Metric;
        if (ImGui.Checkbox("Show milk in metric (ml and litres instead of ounces)", ref b)) { F.Metric = b; Units.Metric = b; changed = true; }

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
        Ui.Wrapped("Everything stays in this window and your own chat log. Nothing is posted in public chat, and the plugin never moves your character, and only does an emote for you when you press a button for it (like Milk on the farmer page). " +
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
        var why = Plugin.PluginInterface.IsDev ? "Dev build (loaded from Dev Plugin Locations)." : "Unlocked with your developer phrase. \"/milkers dev off\" turns it off.";
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
        if (ImGui.SliderFloat("Stall seat range (players: 0.9 y)", ref stall, 0.5f, 4f, "%.1f")) { F.StallRange = stall; changed = true; }
        // The Grand Creamery: where weekly contracts are delivered, for everyone
        ImGui.Spacing();
        Ui.Wrapped("Grand Creameries: weekly contract delivery points, up to 3 (one per Grand Company).", Theme.Ink, width);
        ImGui.TextColored(Theme.InkSoft, "Name");
        ImGui.SetNextItemWidth(Math.Min(260, width));
        ImGui.InputTextWithHint("##creameryname", "The Grand Creamery", ref creameryName, 40);
        ImGui.TextColored(Theme.InkSoft, "Radius");
        ImGui.SetNextItemWidth(Math.Min(260, width));
        ImGui.SliderFloat("##creameryradius", ref creameryRadius, 3f, 30f, "%.0f yalms");
        for (var slot = 0; slot < 3; slot++)
        {
            var cr = G.Creameries.FirstOrDefault(c => c.Slot == slot);
            ImGui.Spacing();
            Ui.Wrapped(cr != null ? $"{slot + 1}. {cr.Name} · {cr.Zone} · radius {cr.Radius:0} y" : $"{slot + 1}. (empty)", cr != null ? Theme.Ink : Theme.InkSoft, width);
            if (ImGui.SmallButton($"Set here##creamset{slot}") && G.Networked) G.SetCreameryHere(slot, creameryName, creameryRadius);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Puts this Grand Creamery where you're standing, with the name and radius above, for every player. Takes effect within a minute.");
            if (cr != null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##creamdel{slot}") && G.Networked) G.RemoveCreamery(slot);
            }
        }

        if (G.Creameries.Count == 0) ImGui.TextColored(Theme.InkSoft, "None set: contracts ship from each dairy.");
        ImGui.Spacing();

        var sleepy = F.DevCanSleep;
        if (ImGui.Checkbox("Let me doze and fall asleep (anti-AFK test)", ref sleepy)) { F.DevCanSleep = sleepy; changed = true; }
        ImGui.TextColored(Theme.InkSoft, $"Idle {(int)G.IdleSeconds}s · {G.Wake} · milk-making today {F.DayAwakeMinutes / 60f:0.0} h");
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
            F.StallRange = 0.9f;
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
        Ui.Wrapped("1. Join a dairy (Dairies tab, ask the owner for an invite) or start your own on the Dairy tab.\n" +
                   "2. Cows: milk fills up over time, faster with Fed, Watered, Comfy and Happy (Barn > Care).\n" +
                   "3. Get milked on one of your dairy's stalls, or by a farmer pressing Milk on you. You're paid by your grade.\n" +
                   "4. Farmers: Herd Care and Kitchen are on the HUD. Fill jugs in the Kitchen, then deliver on the Creamery tab.\n" +
                   "5. Spend coins in the Shop. The full guide is the ? at the top, or /milkers guide.", Theme.Ink, width);

        SubHeading("Having trouble?", width);
        Ui.Wrapped("Copy your support info and paste it in #bug-reports on the Discord: it has your setup and what the plugin did recently (no passwords or keys). Also: /milkers support.", Theme.Ink, width);
        if (ImGui.Button("Copy support info##helpsupport")) plugin.CopySupportInfo();

        SubHeading("Community", width);
        Ui.Wrapped("Questions, finding a dairy, update news and testing builds: join the Eorzea Milkers Discord (18+).", Theme.Ink, width);
        if (ImGui.Button("Join the Discord##helpdiscord")) Plugin.OpenDiscord();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Plugin.DiscordUrl);

        SubHeading("Credits", width);
        Ui.Wrapped("Milking machine sound: \"Milk Pump Step02\" by Erdie, freesound.org/s/49877 (cut into start, loop and stop).", Theme.InkSoft, width);
    }
}
