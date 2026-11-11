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

    /// <summary>Opening it brings your dairy's info up to date (it's checked less often while nothing shows it).</summary>
    public override void OnOpen() => plugin.Net.MineWithin(1);

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public static string Group(Catalog.Recipe r) => r.ToShelf ? "Treats" : "Ingredients";

    public override void Draw()
    {
        DrawJugs();
        ImGui.Separator();
        ImGui.Spacing();
        DrawBaking();
        G.Net.RefreshPantry();
        var stock = string.Join(" · ", Catalog.Ingredients.Where(i => G.IngredientCount(i.Id) > 0).Select(i => $"{i.Name.ToLowerInvariant()} {G.IngredientCount(i.Id)}"));
        ImGui.TextColored(Theme.InkSoft, stock.Length > 0 ? $"In the dairy pantry: {stock}" : "The dairy pantry is empty. Ingredients any farmer makes go there for everyone.");
        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail();
        var listW = Math.Clamp(avail.X * 0.36f, 190f, 280f);
        if (ImGui.BeginChild("##kitchenlist", new Vector2(listW, avail.Y), true)) DrawList(listW);
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##kitchenrecipe", new Vector2(0, avail.Y), true)) DrawRecipe(ImGui.GetContentRegionAvail().X);
        ImGui.EndChild();
    }

    /// <summary>Your jugs: what you carry, what the dairy tank has, and buttons to fill up from it.</summary>
    private void DrawJugs()
    {
        var frac = G.MaxCarry > 0 ? Math.Clamp(G.Carried / G.MaxCarry, 0f, 1f) : 0f;
        ImGui.TextColored(Theme.Ink, $"Your jugs: {G.Carried:0.#} / {G.MaxCarry} oz");
        ImGui.SameLine();
        ImGui.TextColored(Theme.InkSoft, $"· dairy tank {G.TankOz:0.#} oz");
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Theme.BlueDeep);
        ImGui.ProgressBar(frac, new Vector2(Math.Min(ImGui.GetContentRegionAvail().X, 260), 8), "");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        var p8 = G.TakeJugProblem(8);
        if (Ui.Button("##kjug8", "Fill 8 oz", new Vector2(84, 24), "cream", p8 == null, p8)) G.TakeJug(8);
        ImGui.SameLine();
        var p32 = G.TakeJugProblem(32);
        if (Ui.Button("##kjug32", "Fill 32 oz", new Vector2(90, 24), "cream", p32 == null, p32)) G.TakeJug(32);
        ImGui.SameLine();
        var up = G.FillUpOz;
        var pUp = up < 1 ? (G.Carried >= G.MaxCarry - 0.5f ? "Your jugs are full." : "The dairy tank is empty.") : G.TakeJugProblem(up);
        if (Ui.Button("##kjugup", "Fill up", new Vector2(80, 24), "blue", pUp == null, pUp ?? $"Take {up} oz")) G.TakeJug(up);
        ImGui.Spacing();
    }

    /// <summary>What's baking: a progress bar for the current one, the time left for the batch, and Stop.</summary>
    private void DrawBaking()
    {
        if (G.Baking is not { } b)
        {
            ImGui.TextColored(Theme.InkSoft, "Nothing in the works. Pick a recipe.");
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
                var locked = G.FarmerLevel < r.FarmerLevel;
                var dl = ImGui.GetWindowDrawList();
                var p = ImGui.GetCursorScreenPos();
                var h = ImGui.GetTextLineHeight();
                dl.AddCircleFilled(p + new Vector2(5, h / 2 + 1), 3.5f, ImGui.ColorConvertFloat4ToU32(ready ? Theme.SageDeep : Theme.InkSoft with { W = 0.4f }));
                ImGui.SetCursorScreenPos(p + new Vector2(14, 0));
                if (locked) ImGui.PushStyleColor(ImGuiCol.Text, Theme.InkSoft with { W = 0.6f });
                if (ImGui.Selectable(locked ? $"{r.Name}  (Lv {r.FarmerLevel})##kr{r.Id}" : $"{r.Name}##kr{r.Id}", selected == r.Id)) selected = r.Id;
                if (locked) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(ready ? "You can make this now." : G.MakeProblem(r) ?? "");
            }

            ImGui.Spacing();
        }
    }

    private void DrawRecipe(float width)
    {
        var r = Catalog.RecipeById(selected) ?? Catalog.Recipes.FirstOrDefault();
        if (r == null) return;
        if (r.ToShelf) G.Net.RefreshShelf();   // for the shelf price (at most every 30 seconds)
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        Ui.Icon(dl, Catalog.MadeIcon(r.Makes), p + new Vector2(18, 18), 18);
        ImGui.SetCursorScreenPos(p + new Vector2(44, 0));
        ImGui.TextColored(Theme.Ink, r.Name);
        ImGui.SetCursorScreenPos(p + new Vector2(44, ImGui.GetTextLineHeightWithSpacing()));
        ImGui.TextColored(Theme.InkSoft, $"{Group(r)} · {(r.ToShelf ? "goes on the dairy shelf" : "goes in the dairy pantry, for every farmer")}");
        ImGui.SetCursorScreenPos(p + new Vector2(0, 44));
        Ui.Wrapped(r.Desc, Theme.InkSoft, width);
        ImGui.Spacing();

        var max = G.MaxMake(r);
        var n = Math.Clamp(amount.GetValueOrDefault(r.Id, 1), 1, Math.Max(1, max));
        var made = Catalog.MadeName(r.Makes).ToLowerInvariant();

        // What it takes, against what you have
        ImGui.TextColored(Theme.RoseDeep, "NEEDS");
        if (r.MilkOz > 0) Need($"{r.MilkOz * n} oz milk", $"{G.Carried:0.#} oz in your jugs", G.Carried >= r.MilkOz * n);
        foreach (var (id, per) in r.Needs())
        {
            Need($"{per * n} {Catalog.MadeName(id).ToLowerInvariant()}",
                Catalog.IngredientById(id) != null ? $"{G.IngredientCount(id)} in the dairy pantry" : $"you have {G.Count(id)}",
                G.IngredientCount(id) >= per * n);
            // Things the Shop sells (like wheat grain) get a button that opens the Shop on them
            if (Catalog.FeedById(id) is { Price: > 0 } shopItem)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Buy##kbuy{id}")) plugin.MainWindow.OpenShopFor(id);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Open the Shop on {shopItem.Name} ({shopItem.Price} coins each).");
            }
        }
        if (r.FarmerLevel > 1) Need($"Farmer level {r.FarmerLevel}", $"you're {G.FarmerLevel}", G.FarmerLevel >= r.FarmerLevel);
        ImGui.Spacing();

        ImGui.TextColored(Theme.RoseDeep, "MAKES");
        ImGui.TextColored(Theme.Ink, $"{r.Amount * n} {made}{(r.ToShelf ? " for the shelf" : $" ({G.IngredientCount(r.Makes)} in the dairy pantry)")}");
        ImGui.TextColored(Theme.InkSoft, $"{Clock(TimeSpan.FromSeconds(Catalog.TotalSeconds(r, n)))} · +{r.Xp * n} farmer xp");
        if (n > 1) ImGui.TextColored(Theme.InkSoft, "Each one in a batch takes a little longer than the last.");
        ImGui.Spacing();

        // What it's good for, so you can tell whether it's worth making
        ImGui.TextColored(Theme.RoseDeep, "WHAT IT DOES");
        foreach (var line in Effects(r)) Ui.Wrapped(line, Theme.Ink, width);
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
        if (Ui.Button($"##kbake{r.Id}", Catalog.Verb(r), new Vector2(110, 28), "honey", why == null, why)) G.StartBake(r, n);
        if (why != null)
        {
            ImGui.Spacing();
            Ui.Wrapped(why, Theme.RoseDeep, width);
        }
    }

    /// <summary>The made item's bonuses, what it's used for, and (shelf items) what it sells for.</summary>
    private List<string> Effects(Catalog.Recipe r)
    {
        var lines = new List<string>();
        if (Catalog.FeedById(r.Makes) is { } f)
        {
            var parts = new List<string>();
            if (f.Fed > 0) parts.Add($"Fed +{f.Fed * 100:0}% milk speed");
            if (f.Water > 0) parts.Add($"Watered +{f.Water * 100:0}%");
            if (f.Happy > 0) parts.Add($"Happy +{f.Happy * 100:0}% milk speed");
            if (parts.Count > 0) lines.Add($"For cows: {string.Join(", ", parts)}, filling half the bar (30 minutes).");
            if (f.BoostMinutes > 0) lines.Add($"Milk boost: +{Catalog.TreatBonus * 100:0}% milk for {f.BoostMinutes} minutes.");
            if (f.Quality > 0) lines.Add($"Milk quality +{f.Quality:0}.");
            if (r.ToShelf)
            {
                var shelf = G.Net.Shelf.FirstOrDefault(x => x.Item == r.Makes);
                var price = shelf?.Price ?? 0;
                lines.Add(price > 0
                    ? $"Goes on the dairy shelf at {price} coins each (the owner sets it). You get {Math.Floor(price * 0.8):0} for each one a member buys; the fund gets the rest."
                    : "Goes on the dairy shelf. You get 80% of the price for each one a member buys; the fund gets the rest.");
                lines.Add("Farmers can also feed them to cows and bulls for free (Herd Care > Feed). Bulls get bull xp.");
            }
        }
        else
        {
            var usedIn = Catalog.Recipes.Where(x => x.Needs().Any(n => n.Id == r.Makes)).Select(x => x.Name.Replace("Bake ", "").Replace("Churn ", "")).ToList();
            lines.Add(usedIn.Count > 0 ? $"An ingredient: used to make {string.Join(", ", usedIn)}. It can't be eaten on its own." : "An ingredient for other recipes.");
        }

        lines.Add(r.Amount > 1 ? $"Each bake makes {r.Amount} and gives +{r.Xp} farmer xp." : $"+{r.Xp} farmer xp for each one.");
        return lines;
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
