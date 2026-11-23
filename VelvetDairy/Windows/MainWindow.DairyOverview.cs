using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// Dairy > Overview (0.9.99): four totals across the top (level, fund, tanks, the week's contract), a line showing what
/// each dairy level unlocks, then your dairy on the left and, beside it, what the fund can buy next (the owner) above
/// the level, fund, donations and tanks.
/// </summary>
public sealed partial class MainWindow
{
    private void DrawDairyOverview(Vector2 min, Vector2 max)
    {
        var p = G.Net.Progress;
        const float gap = 14f;
        var y = min.Y;
        if (p != null)
        {
            const float tileH = 92f;
            var tw = (max.X - min.X - gap * 3) / 4;
            var tank = p.Tank;
            var c = p.Contract;
            var tiles = new (string Title, string Big, string Small, float Bar, Vector4 Color)[]
            {
                ("Dairy level", $"{p.Level}", p.NextXp is { } n ? $"{p.Xp:N0} / {n:N0} xp" : "Max level", p.LevelProgress, Theme.SageDeep),
                ("Dairy fund", $"{p.Fund:N0}", "coins to spend", -1, Theme.HoneyDeep),
                ("Tanks", Units.Oz(tank?.Total ?? 0, "0"), $"of milk · {Units.Oz(tank?.Seed ?? 0, "0.#")} of seed", -1, Theme.BlueDeep),
                ("This week's contract", c == null ? "..." : c.Completed ? "Done!" : $"{Units.Num(c.OzDone, "0")} / {Units.Num(c.OzNeed)}",
                    c == null ? "" : c.Completed ? "Reward paid at the end of the week" : $"{Units.Unit} shipped to the Grand Creamery", c == null ? -1 : c.OzNeed > 0 ? Math.Clamp(c.OzDone / c.OzNeed, 0f, 1f) : 1f, Theme.BlueDeep),
            };
            var dl = ImGui.GetWindowDrawList();
            for (var i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                var a = new Vector2(min.X + i * (tw + gap), y);
                var b = a + new Vector2(tw, tileH);
                Ui.Card(dl, a, b);
                Ui.Eyebrow(dl, t.Title, a + new Vector2(18, 12), tw - 36, Theme.RoseDeep);
                Ui.Text(dl, fonts.Heading, t.Big, a + new Vector2(18, 36), Theme.Ink);
                Ui.Text(dl, fonts.Small, t.Small, a + new Vector2(18, 64), Theme.InkSoft);
                if (t.Bar >= 0) Ui.Bar(dl, a + new Vector2(tw * 0.5f, 46), a + new Vector2(tw - 18, 52), t.Bar, t.Color);
                if (ImGui.IsMouseHoveringRect(a, b) && Ui.Measure(fonts.Small, t.Small).X > tw - 36) ImGui.SetTooltip(t.Small);
            }

            y += tileH + gap;
            y = DrawUnlockLine(new Vector2(min.X, y), max.X, p) + gap;
        }

        var mid = (min.X + max.X) / 2;
        var top = new Vector2(min.X, y);
        Panel("##netplace", top, new Vector2(mid - gap / 2, max.Y), "Your dairy", DrawNetPlace);
        Panel("##netlevel", new Vector2(mid + gap / 2, top.Y), max, G.IsOwner ? "Spend the fund" : "Level, fund and tank", w =>
        {
            if (G.IsOwner)
            {
                DrawSpendFund(w);
                Gap(6);
            }

            DrawDairyLevel(w);
        }, true);
    }

    /// <summary>What each dairy level unlocks, as a line from 1 to 20 with a mark at each unlock. Returns the bottom.</summary>
    private float DrawUnlockLine(Vector2 min, float maxX, Network.DairyProgress p)
    {
        const float h = 118f, maxLevel = 20f;
        var dl = ImGui.GetWindowDrawList();
        var max = new Vector2(maxX, min.Y + h);
        Ui.Card(dl, min, max);
        Ui.Eyebrow(dl, "What each dairy level unlocks", min + new Vector2(18, 12), maxX - min.X - 36, Theme.RoseDeep);

        // Unlocks by level: the feed bin's feeds, the stall tiers and the Velvet pump
        var marks = new SortedDictionary<int, List<string>>();
        void Add(int level, string what)
        {
            if (!marks.TryGetValue(level, out var l)) marks[level] = l = new List<string>();
            l.Add(what);
        }

        foreach (var f in p.FeedBinShop) Add(Math.Max(1, f.Level), f.Name);
        Add(5, "Velvet stalls");
        Add(12, "Gilded stalls");
        Add(p.PumpLevel, "Velvet pump");
        Add(20, "Top level");

        var x0 = min.X + 60;
        var x1 = maxX - 60;
        var ly = min.Y + 46;
        var done = Math.Clamp((p.Level - 1) / (maxLevel - 1), 0f, 1f);
        dl.AddRectFilled(new Vector2(x0, ly - 2), new Vector2(x1, ly + 2), Ui.U(Theme.Linen), 2);
        dl.AddRectFilled(new Vector2(x0, ly - 2), new Vector2(x0 + (x1 - x0) * done, ly + 2), Ui.U(Theme.SageDeep), 2);
        var next = marks.Keys.FirstOrDefault(l => l > p.Level);
        var slot = (x1 - x0) / Math.Max(1, marks.Count) + 10;
        foreach (var (level, what) in marks)
        {
            var mx = x0 + (x1 - x0) * Math.Clamp((level - 1) / (maxLevel - 1), 0f, 1f);
            var owned = p.Level >= level;
            var col = owned ? Theme.SageDeep : level == next ? Theme.RoseDeep : Theme.StoneDeep;
            if (owned) dl.AddCircleFilled(new Vector2(mx, ly), 8, Ui.U(col));
            else
            {
                dl.AddCircleFilled(new Vector2(mx, ly), 8, Ui.U(Theme.Paper));
                dl.AddCircle(new Vector2(mx, ly), 8, Ui.U(col), 0, 2.5f);
            }

            Ui.Text(dl, fonts.Small, $"Lv {level}", new Vector2(mx, ly + 14), col, 0.5f, 0f);
            var lineH = Ui.Measure(fonts.Small, "Ag").Y;
            var ty = ly + 14 + lineH;
            foreach (var line in WrapText(string.Join(" · ", what), Math.Min(slot, 120)))
            {
                Ui.Text(dl, fonts.Small, line, new Vector2(mx, ty), owned ? Theme.SageDeep : Theme.InkSoft, 0.5f, 0f);
                ty += lineH;
            }
        }

        return max.Y;
    }

    /// <summary>The owner's next buys from the fund: the Velvet pump, the next feed for the bin, a new stall and stall tiers.</summary>
    private void DrawSpendFund(float width)
    {
        var p = G.Net.Progress;
        if (p == null) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // The Velvet pump
        var running = p.PumpUntil > now;
        var pumpWhy = p.Level < p.PumpLevel ? $"Needs dairy level {p.PumpLevel}." : running ? null : p.Fund < p.PumpCost ? $"The fund needs {p.PumpCost:N0} coins." : null;
        var pumpLine = running ? $"Running: {DairyGame.Wait(TimeSpan.FromSeconds(p.PumpUntil - now))} left. Starting it again adds 2 hours."
            : $"Everyone's milk and seed +15% for {p.PumpMinutes / 60} hours · {p.PumpCost:N0} coins";
        if (ListRow(Ico("milk"), "Velvet pump", Theme.Ink, new[] { new Line(pumpLine, running ? Theme.SageDeep : Theme.InkSoft) },
                new[] { new Btn("##spendpump", running ? "Add 2h" : "Start", "honey", pumpWhy == null && p.Fund >= p.PumpCost && p.Level >= p.PumpLevel, pumpWhy ?? $"{p.PumpCost:N0} from the fund.") }, width) == 0)
            G.StartPump();

        // The next feed for the bin
        var feed = p.FeedBinShop.Where(f => !p.FeedBin.Contains(f.Item)).OrderBy(f => f.Level).FirstOrDefault();
        if (feed != null)
        {
            var why = p.Level < feed.Level ? $"Needs dairy level {feed.Level}." : p.Fund < feed.Price ? $"The fund needs {feed.Price:N0} coins." : null;
            if (ListRow(Ico("grain"), $"{feed.Name} for the feed bin", Theme.Ink, new[] { new Line($"A bigger Fed bonus for everyone farmers feed · {feed.Price:N0} coins", Theme.InkSoft) },
                    new[] { new Btn("##spendfeed", p.Level < feed.Level ? $"Level {feed.Level}" : "Buy", "honey", why == null, why ?? $"{feed.Price:N0} from the fund.") }, width) == 0)
                G.BuyFeedBin(feed.Item, feed.Name);
        }

        // Stalls: a new one, and better tiers (both on the Stalls page, where you pick the seat or the stall)
        if (ListRow(Ico("bucket"), "Stalls", Theme.Ink, new[]
                {
                    new Line($"A new stall {p.NextStallPrice:N0} coins · tiers {string.Join(", ", p.TierPrices.Skip(2).Select(x => x.ToString("N0")))} · best now: {Catalog.StallTierNames[Math.Clamp(p.MaxTier, 1, 4)]}", Theme.InkSoft),
                },
                new[] { new Btn("##spendstalls", "Stalls", "cream", true, "Add a stall or raise one's tier on Dairy > Stalls.") }, width) == 0)
            subTabs["dairynet"] = 2;
    }
}
