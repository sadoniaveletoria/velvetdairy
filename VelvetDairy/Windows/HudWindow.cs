using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>Small always-on card: milk bottle, grade, needs and what's happening.</summary>
public sealed class HudWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private float shown = -1;
    private int seenDump;

    private FarmSave F => plugin.Config.Farm;
    private DairyGame G => plugin.Game;

    private static readonly Vector2 HudSize = new(330, 168);
    private const float ButtonRow = 38f;   // farmers get a row of buttons under the card: Herd Care and Kitchen

    private bool ShowHandMilk => G.IsFarmer;
    private bool ShowKitchen => G.CanCarry;

    public HudWindow(Plugin plugin, Art art, Fonts fonts)
        : base("Eorzea Milkers HUD##VelvetDairyHud",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        Size = HudSize;
        SizeCondition = ImGuiCond.Always;
        Position = new Vector2(60, 320);
        PositionCondition = ImGuiCond.FirstUseEver;
        IsOpen = true;
        RespectCloseHotkey = false;
    }

    public override bool DrawConditions() =>
        F.ShowHud && F.Confirmed18 && Plugin.ObjectTable.LocalPlayer != null && !DairyGame.IsLalafell();

    public override void PreDraw()
    {
        Size = HudSize + new Vector2(0, ShowHandMilk || ShowKitchen ? ButtonRow : 0);
        if (F.LockHud) Flags |= ImGuiWindowFlags.NoMove;
        else Flags &= ~ImGuiWindowFlags.NoMove;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

    /// <summary>Shortens a line with "..." so it always stays inside the card.</summary>
    private string Fit(string text, float width)
    {
        if (Ui.Measure(fonts.Small, text).X <= width) return text;
        while (text.Length > 4 && Ui.Measure(fonts.Small, text + "...").X > width) text = text[..^1];
        return text.TrimEnd(' ', '·') + "...";
    }

    public override void Draw()
    {
        // Clicking in Eorzea Milkers's own windows counts as being awake (mouse movement alone doesn't)
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) G.Touch();

        var dl = ImGui.GetWindowDrawList();
        var o = ImGui.GetWindowPos();
        var whole = ImGui.GetWindowSize();
        Ui.Card(dl, o, o + whole, false, G.Level >= 75);   // cow level 75: velvet frame

        // Cow level 50: your title in gold along the top edge (with the crown when you have it)
        if (G.Level >= 50 || G.Crowned)
        {
            var title = G.Level >= 50 ? Leveling.Rank(G.Level) : Leveling.FarmerRank(G.FarmerLevel) ?? "";
            var tw = Ui.Measure(fonts.Small, title).X;
            var cw = G.Crowned ? 16f : 0f;
            var tx = o.X + (whole.X - tw - cw) / 2;
            if (G.Crowned) Ui.Crown(dl, new Vector2(tx, o.Y + 4), 12);
            Ui.Text(dl, fonts.Small, title, new Vector2(tx + cw, o.Y + 2), Theme.HoneyDeep);
        }
        var size = HudSize;   // the bottle, needs and status keep their place; buttons go below

        var cow = F.Cow;
        // A bull who isn't a cow shows his seed vials here instead of milk bottles; a cow who's also a bull gets a small vial beside the bottle
        var seedOnly = !G.ProducesMilk && G.ProducesSeed;
        var amount = seedOnly ? cow.Seed : cow.Milk;
        var cap = seedOnly ? G.SeedCapacity : G.Capacity;
        var spoiled = seedOnly ? G.SeedSpoiled : G.Spoiled;
        var overfull = seedOnly ? G.SeedFull : G.Overfull;
        var serial = G.DumpSerial + G.SeedDumpSerial * 1000;
        if (seenDump != serial)
        {
            seenDump = serial;
            shown = amount;
        }

        shown = shown < 0 ? amount : shown + (amount - shown) * Math.Min(1f, ImGui.GetIO().DeltaTime * 4f);

        // Bottle
        var bh = size.Y - 34;
        var bmin = o + new Vector2(20, 17);
        var flow = G.Active != null ? Ui.Flow.Draining
            : (seedOnly ? G.SeedComing : G.MilkComing) ? Ui.Flow.Filling
            : Ui.Flow.Still;
        var bSize = seedOnly ? G.VialSize : G.BottleSize;
        var bCount = G.BottleCount;
        var full = Math.Min(bCount, (int)(shown / bSize + 0.001f));
        var frac = full >= bCount ? 1f : Math.Clamp((shown - full * bSize) / bSize, 0f, 1f);
        Ui.Bottle(dl, bmin, new Vector2(bh / 2, bh), frac, flow, spoiled ? MainWindow.SourMilk : seedOnly ? MainWindow.SeedTint : (Vector4?)null, G.BottleTier);
        var milkAndSeed = G.ProducesMilk && G.ProducesSeed;
        if (milkAndSeed)
        {
            // The seed vial: small, beside the bottle
            var vf = Math.Clamp(cow.Seed / Math.Max(0.001f, G.SeedCapacity), 0f, 1f);
            var vmin = bmin + new Vector2(bh / 2 + 4, bh * 0.45f);
            Ui.Bottle(dl, vmin, new Vector2(bh * 0.55f / 2, bh * 0.55f), vf, G.SeedComing ? Ui.Flow.Filling : Ui.Flow.Still, G.SeedSpoiled ? MainWindow.SourMilk : MainWindow.SeedTint, G.BottleTier);
            if (ImGui.IsMouseHoveringRect(vmin, vmin + new Vector2(bh * 0.55f / 2, bh * 0.55f)))
                ImGui.SetTooltip(Units.Text($"Seed: {cow.Seed:0.##} / {G.SeedCapacity:0.#} oz · Grade {Grades.Name(G.SeedGrade)}" + (G.SeedSpoiled ? "\nSpoiled: dump it." : G.SeedFull ? "\nFull: get milked." : "")));
        }

        // "+xp" floats up beside the bottle when making milk ticks XP in
        var tick = G.LastXpTick;
        var age = G.Now - tick.At;
        if (tick.Amount > 0 && age < 3)
            Ui.Text(dl, fonts.Small, $"+{tick.Amount} xp", bmin + new Vector2(bh / 2 + 2, bh * 0.25f - (float)age * 10f),
                Theme.Alpha(Theme.HoneyDeep, (float)(1 - age / 3)));

        // Milk and grade
        var x = bmin.X + bh / 2 + 14 + (milkAndSeed ? bh * 0.55f / 2 + 4 : 0);
        var grade = seedOnly ? G.SeedGrade : G.CurrentGrade;
        var amountText = Units.NumLike(amount, cap, seedOnly ? "0.##" : "0.#");
        Ui.Text(dl, fonts.Heading, amountText, new Vector2(x, o.Y + 18), Theme.Ink);
        var mw = Ui.Measure(fonts.Heading, amountText).X;
        var capText = Units.Oz(cap, seedOnly ? "0.#" : "0") + (seedOnly ? " seed" : "");
        Ui.Text(dl, fonts.Small, bCount > 1 ? $"/ {capText} · {full} of {bCount} full" : $"/ {capText}", new Vector2(x + mw + 4, o.Y + 24), Theme.InkSoft);
        // The grade seal sits on the jug's shoulder, like the Barn's, so it never covers the text
        Ui.Seal(dl, new Vector2(bmin.X + bh / 2 - 4, bmin.Y + bh * 0.2f), 15, Grades.Name(grade), Theme.GradeColor(grade));

        // Needs as four thin bars
        var y = o.Y + 52;
        var bw = size.X - (x - o.X) - 26;
        var needs = new (string Label, float V, Vector4 C)[]
        {
            ("Fed", cow.Hunger, Theme.HoneyDeep), ("Watered", cow.Thirst, Theme.BlueDeep),
            ("Comfy", cow.Comfort, Theme.RoseDeep), ("Happy", cow.Mood, Theme.SageDeep),
        };
        var half = (bw - 10) / 2;
        for (var i = 0; i < needs.Length; i++)
        {
            var bx = x + (i % 2) * (half + 10);
            var by = y + (i / 2) * 28;
            Ui.Text(dl, fonts.Small, needs[i].Label, new Vector2(bx, by), Theme.InkSoft);
            Ui.Bar(dl, new Vector2(bx, by + 16), new Vector2(bx + half, by + 21), needs[i].V / 100f, Theme.NeedColor(needs[i].V, needs[i].C));
        }

        // Status line
        string status;
        Vector4 col;
        var s = G.Active;
        if (G.Paused)
        {
            status = "Paused";
            col = Theme.InkSoft;
        }
        else if (s != null)
        {
            status = seedOnly ? $"Milking · {Units.Oz(s.ExtractedSeed * s.SeedYield, "0.##")} seed" : $"Milking · {Units.Oz(s.Extracted * s.Yield, "0.#")}" + (s.ExtractedSeed > 0 ? $" + {Units.Oz(s.ExtractedSeed * s.SeedYield, "0.##")} seed" : "");
            col = Theme.BlueDeep;
            Ui.Bar(dl, new Vector2(x, o.Y + size.Y - 26), new Vector2(x + bw, o.Y + size.Y - 22), s.Progress, Theme.BlueDeep);
        }
        else if (G.Spoiled || G.SeedSpoiled)
        {
            status = G.Spoiled && G.SeedSpoiled ? "Milk and seed spoiled!" : G.Spoiled ? "Spoiled!" : "Seed spoiled!";
            col = Theme.Warn;
        }
        else if (overfull || (milkAndSeed && G.SeedFull))
        {
            var left = overfull ? seedOnly ? G.SeedSpoilsIn : G.SpoilsIn : G.SeedSpoilsIn;
            status = left.HasValue && left.Value.TotalHours < 24 ? $"Full! Spoils in {(left.Value.TotalHours >= 1 ? $"{(int)left.Value.TotalHours}h {left.Value.Minutes}m" : $"{left.Value.Minutes}m")}" : "Full, get milked";
            col = Theme.Warn;
        }
        else if (G.Wake == DairyGame.Wakefulness.Asleep)
        {
            status = "Asleep · move to wake up";
            col = Theme.Warn;
        }
        else if (G.Wake == DairyGame.Wakefulness.Dozing)
        {
            status = "Dozing · half-speed milk";
            col = Theme.HoneyDeep;
        }
        else if (G.Tired && G.Producer)
        {
            status = "Tired · half speed today";
            col = Theme.HoneyDeep;
        }
        else if (G.InScene)
        {
            // A Corruption scene: who with and arousal, with a bar where the milking bar goes
            status = G.SceneLine;
            col = Theme.RoseDeep;
            if (G.ArousalNow.Ready)
                Ui.Bar(dl, new Vector2(x, o.Y + size.Y - 26), new Vector2(x + bw, o.Y + size.Y - 22), G.ArousalNow.Value / 100f, Theme.RoseDeep);
        }
        else if (G.BredLeft > TimeSpan.Zero)
        {
            status = $"Bred · +{G.BredBonusNow * 100:0.#}% for {(int)G.BredLeft.TotalMinutes + 1} min";
            col = Theme.RoseDeep;
        }
        else if (G.Pregnant && G.IsCow)
        {
            status = $"Pregnant · {DairyGame.StageName(G.Preg.Stage)}";
            col = Theme.RoseDeep;
        }
        else if (G.Where.InDairy)
        {
            status = G.Where.Seated ? "Resting in the dairy" : "In the dairy";
            col = Theme.SageDeep;
        }
        else
        {
            status = "Away from the dairy";
            col = Theme.RoseDeep;
        }

        // Farmers and bulls don't make milk: say what they are instead
        if (!G.Producer && !G.Paused && s == null)
        {
            status = $"{char.ToUpperInvariant(G.Role[0])}{G.Role[1..]}{(G.DairyName.Length > 0 ? $" at {G.DairyName}" : "")}{(G.Where.InDairy ? " · here" : "")}";
            col = G.Where.InDairy ? Theme.SageDeep : Theme.InkSoft;
        }

        // At a Grand Creamery: say so the whole time you're there
        if (s == null && !G.Paused && G.CreameryHere is { } here)
        {
            var others = G.CreameryNow?.Here.Count ?? 0;
            status = $"At {here.Name}" + (others > 0 ? $" · {others} other{(others == 1 ? "" : "s")} here" : "");
            col = Theme.BlueDeep;
        }

        // A newer version is out
        if (G.Net.UpdateVersion is { } nv && s == null && !G.Spoiled && !G.SeedSpoiled && !overfull)
        {
            status = $"Update {nv} is out · /xlplugins";
            col = Theme.BlueDeep;
        }

        // Latest message briefly replaces the status
        var now = ImGui.GetTime();
        if (Toasts.Items.Count > 0 && now - Toasts.Items[0].T0 < 4)
        {
            status = Toasts.Items[0].Text;
            col = Toasts.Items[0].Kind == 2 ? Theme.Warn : Theme.Ink;
        }

        var dumpable = G.Spoiled || G.SeedSpoiled;
        var textRight = o.X + size.X - 20 - (dumpable ? 112 : 0);
        dl.PushClipRect(new Vector2(x, o.Y), new Vector2(textRight, o.Y + size.Y), true);
        Ui.Text(dl, fonts.Small, Fit(status, textRight - x), new Vector2(x, o.Y + size.Y - 46), col);
        dl.PopClipRect();

        // Spoiled milk: the only way forward is to pour it out
        if (dumpable &&
            Ui.Button("##huddump", G.Spoiled ? "Dump milk" : "Dump seed", new Vector2(104, 30), "rose", true,
                G.Spoiled ? "Your milk spoiled. Pour it all out (no xp or coins)." : "Your seed spoiled. Pour it all out (no xp or coins).", new Vector2(o.X + size.X - 124, o.Y + size.Y - 54)))
        {
            if (G.Spoiled) G.DumpMilk();
            else G.DumpSeed();
        }

        // Farmers: Herd Care and Kitchen, each opening its own window
        if (ShowHandMilk || ShowKitchen)
        {
            var by = o.Y + size.Y - 10;
            var both = ShowHandMilk && ShowKitchen;
            var bw2 = both ? (size.X - 50) / 2 : size.X - 40;
            var bx = o.X + 20;
            if (ShowHandMilk)
            {
                if (Ui.Button("##hudhandmilk", "Herd Care", new Vector2(bw2, 30), "rose", true, "Your dairy's cows and bulls: milk, groom and feed them.", new Vector2(bx, by)))
                    plugin.Milking.Toggle();
                bx += bw2 + 10;
            }

            if (ShowKitchen)
            {
                var tip = G.Baking is { } bk ? $"{Catalog.Doing(bk.Recipe)} {Catalog.MadeName(bk.Recipe.Makes).ToLowerInvariant()} ({F.Bake!.Done + 1} of {F.Bake.Count})." : "Fill your jugs from the dairy tank and bake.";
                if (Ui.Button("##hudkitchen", G.Baking is { } kb ? $"Kitchen ({Catalog.Doing(kb.Recipe).ToLowerInvariant()})" : "Kitchen", new Vector2(bw2, 30), "honey", true, tip, new Vector2(bx, by)))
                    plugin.Kitchen.Toggle();
            }
        }

        // Double-click or right-click the card to open the main window (dragging it still moves it)
        if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered())
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) plugin.MainWindow.Toggle();
        }
    }
}
