using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// The farmers' kitchen: every recipe in a short searchable list on the left (grouped Ingredients / Treats), the chosen
/// recipe's details and the Bake button on the right, and what's baking now across the top. Built to hold many recipes.
/// </summary>
public sealed class KitchenWindow : Window
{
    private readonly Plugin plugin;
    private string selected = "";
    private string search = "";
    private readonly Dictionary<string, int> amount = new();

    private FarmSave F => plugin.Config.Farm;
    private DairyGame G => plugin.Game;

    public KitchenWindow(Plugin plugin) : base("Kitchen##VelvetDairyKitchen", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(660, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(500, 320), MaximumSize = new Vector2(1400, 1200) };
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public static string Group(Catalog.Recipe r) => r.ToShelf ? "Treats" : "Ingredients";

    public override void Draw()
    {
        DrawBaking();
        ImGui.TextColored(Theme.InkSoft, $"In your jugs: {G.Carried:0.#} oz" +
            string.Concat(Catalog.Ingredients.Where(i => G.Count(i.Id) > 0).Select(i => $" · {i.Name.ToLowerInvariant()} {G.Count(i.Id)}")));
        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail();
        var listW = Math.Clamp(avail.X * 0.36f, 190f, 280f);
        if (ImGui.BeginChild("##kitchenlist", new Vector2(listW, avail.Y), true)) DrawList(listW);
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##kitchenrecipe", new Vector2(0, avail.Y), true)) DrawRecipe(ImGui.GetContentRegionAvail().X);
        ImGui.EndChild();
    }

    /// <summary>What's baking: a progress bar for the current one, the time left for the batch, and Stop.</summary>
    private void DrawBaking()
    {
        if (G.Baking is not { } b)
        {
            ImGui.TextColored(Theme.InkSoft, "Nothing baking. Pick a recipe.");
            return;
        }

        var job = F.Bake!;
        ImGui.TextColored(Theme.Ink, $"{b.Recipe.Name} · {job.Done + 1} of {job.Count}");
        ImGui.SameLine();
        ImGui.TextColored(G.Where.InDairy ? Theme.InkSoft : Theme.RoseDeep,
            G.Where.InDairy ? $"{Clock(b.Left)} left · {job.Done} done" : "Paused: go back into the dairy to keep going");
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Theme.HoneyDeep);
        ImGui.ProgressBar(b.Current, new Vector2(ImGui.GetContentRegionAvail().X - 90, 14), "");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        if (Ui.Button("##kitchenstop", "Stop", new Vector2(80, 22), "cream", true, "Stop after keeping what's finished. Milk for unfinished ones stays in your jugs."))
            G.CancelBake();
    }

    private void DrawList(float width)
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##kitchensearch", "Search recipes", ref search, 40);
        ImGui.Spacing();
        var q = search.Trim();
        foreach (var group in new[] { "Ingredients", "Treats" })
        {
            var recipes = Catalog.Recipes.Where(r => Group(r) == group && (q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            if (recipes.Count == 0) continue;
            ImGui.TextColored(Theme.RoseDeep, group.ToUpperInvariant());
            foreach (var r in recipes)
            {
                if (selected.Length == 0) selected = r.Id;
                var ready = G.MakeProblem(r) == null;
                var dl = ImGui.GetWindowDrawList();
                var p = ImGui.GetCursorScreenPos();
                var h = ImGui.GetTextLineHeight();
                dl.AddCircleFilled(p + new Vector2(5, h / 2 + 1), 3.5f, ImGui.ColorConvertFloat4ToU32(ready ? Theme.SageDeep : Theme.InkSoft with { W = 0.4f }));
                ImGui.SetCursorScreenPos(p + new Vector2(14, 0));
                if (ImGui.Selectable($"{r.Name}##kr{r.Id}", selected == r.Id)) selected = r.Id;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(ready ? "You can make this now." : G.MakeProblem(r) ?? "");
            }

            ImGui.Spacing();
        }
    }

    private void DrawRecipe(float width)
    {
        var r = Catalog.RecipeById(selected) ?? Catalog.Recipes.FirstOrDefault();
        if (r == null) return;
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        Ui.Icon(dl, Catalog.MadeIcon(r.Makes), p + new Vector2(18, 18), 18);
        ImGui.SetCursorScreenPos(p + new Vector2(44, 0));
        ImGui.TextColored(Theme.Ink, r.Name);
        ImGui.SetCursorScreenPos(p + new Vector2(44, ImGui.GetTextLineHeightWithSpacing()));
        ImGui.TextColored(Theme.InkSoft, $"{Group(r)} · {(r.ToShelf ? "goes on the dairy shelf" : "goes in your pantry")}");
        ImGui.SetCursorScreenPos(p + new Vector2(0, 44));
        Ui.Wrapped(r.Desc, Theme.InkSoft, width);
        ImGui.Spacing();

        var max = G.MaxMake(r);
        var n = Math.Clamp(amount.GetValueOrDefault(r.Id, 1), 1, Math.Max(1, max));
        var made = Catalog.MadeName(r.Makes).ToLowerInvariant();

        // What it takes, against what you have
        ImGui.TextColored(Theme.RoseDeep, "NEEDS");
        Need($"{r.MilkOz * n} oz milk{(r.MinGrade > 0 ? $" (Grade {Grades.Name(r.MinGrade)}+)" : "")}", $"{G.OzAtOrAbove(r.MinGrade):0.#} oz in your jugs", G.OzAtOrAbove(r.MinGrade) >= r.MilkOz * n);
        if (r.UsesCount > 0)
            Need($"{r.UsesCount * n} {Catalog.MadeName(r.Uses).ToLowerInvariant()}", $"you have {G.Count(r.Uses)}", G.Count(r.Uses) >= r.UsesCount * n);
        if (r.FarmerLevel > 1) Need($"Farmer level {r.FarmerLevel}", $"you're {G.FarmerLevel}", G.FarmerLevel >= r.FarmerLevel);
        ImGui.Spacing();

        ImGui.TextColored(Theme.RoseDeep, "MAKES");
        ImGui.TextColored(Theme.Ink, $"{r.Amount * n} {made}{(r.ToShelf ? " for the shelf" : $" (you have {G.Count(r.Makes)})")}");
        ImGui.TextColored(Theme.InkSoft, $"{Clock(TimeSpan.FromSeconds(Catalog.TotalSeconds(r, n)))} · +{r.Xp * n} farmer xp");
        if (n > 1) ImGui.TextColored(Theme.InkSoft, "Each one in a batch takes a little longer than the last.");
        ImGui.Spacing();

        // How many, then Bake
        if (Ui.Button($"##kless{r.Id}", "-", new Vector2(30, 28), "cream", n > 1, "One fewer")) amount[r.Id] = n - 1;
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Ink, $" {n} ");
        ImGui.SameLine();
        if (Ui.Button($"##kmore{r.Id}", "+", new Vector2(30, 28), "cream", n < max, max <= n ? "That's all your jugs (and pantry) allow." : "One more")) amount[r.Id] = n + 1;
        ImGui.SameLine();
        if (Ui.Button($"##kmax{r.Id}", "Max", new Vector2(50, 28), "cream", max > 1 && n != max, $"As many as you can: {Math.Max(1, max)}")) amount[r.Id] = Math.Max(1, max);
        ImGui.SameLine();
        var why = G.MakeProblem(r, n);
        if (Ui.Button($"##kbake{r.Id}", "Bake", new Vector2(110, 28), "honey", why == null, why)) G.StartBake(r, n);
        if (why != null)
        {
            ImGui.Spacing();
            Ui.Wrapped(why, Theme.RoseDeep, width);
        }
    }

    private static void Need(string what, string have, bool ok)
    {
        // A colored dot (the game font may not have check marks)
        var p = ImGui.GetCursorScreenPos();
        var h = ImGui.GetTextLineHeight();
        ImGui.GetWindowDrawList().AddCircleFilled(p + new Vector2(5, h / 2 + 1), 4f, ImGui.ColorConvertFloat4ToU32(ok ? Theme.SageDeep : Theme.RoseDeep));
        ImGui.SetCursorScreenPos(p + new Vector2(14, 0));
        ImGui.TextColored(Theme.Ink, what);
        ImGui.SameLine();
        ImGui.TextColored(Theme.InkSoft, $"({have})");
    }

    private static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}
