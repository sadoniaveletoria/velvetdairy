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
            var tank = p.Tank;
            var c = p.Contract;
            y = DrawTiles(new Vector2(min.X, y), max.X, new Tile[]
            {
                new("Dairy level", $"{p.Level}", p.NextXp is { } n ? $"{p.Xp:N0} / {n:N0} xp" : "Max level", p.LevelProgress, Theme.SageDeep),
                new("Dairy fund", $"{p.Fund:N0}", "coins", -1, Theme.HoneyDeep),
                new("Tanks", Units.Oz(tank?.Total ?? 0, "0"), $"milk · {Units.Oz(tank?.Seed ?? 0, "0.#")} seed", -1, Theme.BlueDeep),
                new("Contract", c == null ? "..." : c.Completed ? "Done!" : $"{Units.Num(c.OzDone, "0")} / {Units.Num(c.OzNeed)}",
                    c == null ? "" : c.Completed ? "paid at the week's end" : $"{Units.Unit} shipped this week", c == null ? -1 : c.OzNeed > 0 ? Math.Clamp(c.OzDone / c.OzNeed, 0f, 1f) : 1f, Theme.BlueDeep),
            });
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

    /// <summary>One summary tile: a small title, a big number, a line under it and an optional bar (Bar &lt; 0: none).</summary>
    private readonly record struct Tile(string Title, string Big, string Small, float Bar, Vector4 Color);

    /// <summary>A row of summary tiles across the top of a page (the same look on every page). Returns where the page goes on below them.</summary>
    private float DrawTiles(Vector2 min, float maxX, IReadOnlyList<Tile> tiles)
    {
        const float gap = 14f, tileH = 126f;
        if (tiles.Count == 0) return min.Y;
        var tw = (maxX - min.X - gap * (tiles.Count - 1)) / tiles.Count;
        var dl = ImGui.GetWindowDrawList();
        for (var i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var a = new Vector2(min.X + i * (tw + gap), min.Y);
            var b = a + new Vector2(tw, tileH);
            Ui.Card(dl, a, b);
            // The same insets as the panels, so nothing touches the card's frame
            var inner = tw - 56;
            Ui.Eyebrow(dl, t.Title, a + new Vector2(28, 22), inner, Theme.RoseDeep);
            var big = t.Big;
            // A big number that doesn't fit drops to the body font rather than running out of the card
            var font = Ui.Measure(fonts.Heading, big).X <= inner ? fonts.Heading : null;
            var bigSize = Ui.Text(dl, font, big, a + new Vector2(28, 48), Theme.Ink);
            // The bar sits beside the big number (not under it), so the card stays short and nothing reaches its frame
            if (t.Bar >= 0 && inner - bigSize.X > 50)
                Ui.Bar(dl, a + new Vector2(28 + bigSize.X + 14, 48 + bigSize.Y / 2 - 3), a + new Vector2(28 + inner, 48 + bigSize.Y / 2 + 3), Math.Clamp(t.Bar, 0f, 1f), t.Color);
            TextWrapped(dl, fonts.Small, t.Small, a + new Vector2(28, 48 + bigSize.Y + 4), inner, Theme.InkSoft);
        }

        return min.Y + tileH + gap;
    }

    /// <summary>What each dairy level unlocks, as a row of chips that wraps (green: yours, rose: next, grey: later). Returns the bottom.</summary>
    private float DrawUnlockLine(Vector2 min, float maxX, Network.DairyProgress p)
    {
        var dl = ImGui.GetWindowDrawList();
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
        var next = marks.Keys.FirstOrDefault(l => l > p.Level);

        // Lay the chips out first, to know how tall the card is
        var inner = maxX - min.X - 56;
        const float chipH = 30f, chipGap = 8f;
        var chips = new List<(string Text, int Level, Vector2 At, float W)>();
        float cx = 0, cy = 0;
        foreach (var (level, what) in marks)
        {
            var text = $"Lv {level} · {string.Join(", ", what)}";
            var w = Ui.Measure(fonts.Small, text).X + 24;
            if (cx > 0 && cx + w > inner)
            {
                cx = 0;
                cy += chipH + chipGap;
            }

            chips.Add((text, level, new Vector2(cx, cy), w));
            cx += w + chipGap;
        }

        var h = 24 + 32 + cy + chipH + 24;
        var max = new Vector2(maxX, min.Y + h);
        Ui.Card(dl, min, max);
        var nextText = next > 0 ? $"Level {p.Level} · next unlock at level {next}" : $"Level {p.Level} · everything unlocked";
        Ui.Eyebrow(dl, $"What each dairy level unlocks · {nextText}", min + new Vector2(28, 22), inner, Theme.RoseDeep);
        var o = min + new Vector2(28, 24 + 32);
        foreach (var ch in chips)
        {
            var a = o + ch.At;
            var b = a + new Vector2(ch.W, chipH);
            var owned = p.Level >= ch.Level;
            var col = owned ? Theme.SageDeep : ch.Level == next ? Theme.RoseDeep : Theme.StoneDeep;
            dl.AddRectFilled(a, b, Ui.U(owned ? Theme.Sage : ch.Level == next ? Theme.Rose : Theme.Stone, owned ? 0.45f : 0.35f), chipH / 2);
            dl.AddRect(a, b, Ui.U(col, 0.7f), chipH / 2, ImDrawFlags.None, 1.2f);
            Ui.Text(dl, fonts.Small, ch.Text, a + new Vector2(12, chipH / 2), Theme.Ink, 0f, 0.5f);
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
