using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

// Pantry tab: the feed and treats you own. Eat them (inside your dairy), sell back what you don't need, or hide it.
public sealed partial class MainWindow
{
    private const string PantryFood = "Food & treats", PantryIngredients = "Ingredients", PantryShelf = "Dairy shelf";

    /// <summary>Pantry tabs: what you eat, what you bake with, and your dairy's shelf (each its own short list).</summary>
    private void DrawPantry(Vector2 min, Vector2 max)
    {
        var labels = new List<string> { PantryFood };
        if (G.CanCarry || Catalog.Ingredients.Any(i => G.IngredientCount(i.Id) > 0)) labels.Add(PantryIngredients);
        if (G.Networked && F.NetMember != null) labels.Add(PantryShelf);
        var top = SubTabs("pantry", labels.ToArray(), min, max.X - min.X);
        var which = labels[Math.Clamp(Sub("pantry"), 0, labels.Count - 1)];
        var mid = min.X + (max.X - min.X) * 0.58f;
        switch (which)
        {
            case PantryIngredients:
                Panel("##pantryingr", top, new Vector2(mid - 7, max.Y), "Ingredients", DrawIngredients);
                Panel("##pantryingrinfo", new Vector2(mid + 7, top.Y), max, "Baking", w =>
                    Ui.Wrapped("Ingredients are only used to make other things in the Kitchen (HUD > Kitchen). Butter is churned from milk in your jugs.", Theme.InkSoft, w), true);
                break;
            case PantryShelf:
                Panel("##pantryshelf", top, new Vector2(mid - 7, max.Y), "Dairy shelf", DrawShelf);
                Panel("##pantryshelfinfo", new Vector2(mid + 7, top.Y), max, "How the shelf works", w =>
                    Ui.Wrapped("Your dairy's farmers bake treats for the shelf. Buy them inside the dairy; the baker gets 80% of the price and the dairy fund 20%. " +
                               "The owner sets the prices. What you buy goes to Food & treats.", Theme.InkSoft, w), true);
                break;
            default:
                Panel("##pantry", top, new Vector2(mid - 7, max.Y), "Your pantry", DrawPantryList);
                Panel("##pantryinfo", new Vector2(mid + 7, top.Y), max, "Milk quality", DrawPantryInfo, true);
                break;
        }
    }

    private void DrawIngredients(float width)
    {
        G.Net.RefreshPantry();
        Ui.Wrapped("Ingredients are shared: whatever any farmer makes goes into the dairy pantry, and every farmer can bake with it.", Theme.InkSoft, width);
        var have = Catalog.Ingredients.Where(i => G.IngredientCount(i.Id) > 0).ToList();
        if (have.Count == 0)
        {
            Ui.Wrapped("No ingredients yet. Farmers make them in the Kitchen (HUD > Kitchen).", Theme.InkSoft, width);
            return;
        }

        foreach (var i in have)
            ListRow(Ico(i.Icon), i.Name, Theme.Ink,
                new[] { new Line($"{G.IngredientCount(i.Id)} in the dairy pantry", Theme.Ink), new Line(i.Desc, Theme.InkSoft) }, Array.Empty<Btn>(), width);
    }

    private void DrawPantryList(float width)
    {
        var problem = G.CareProblem();
        if (problem != null)
        {
            Ui.Wrapped(problem + " You can still sell or hide things here.", Theme.RoseDeep, width);
            Gap(6);
        }

        var shown = 0;
        var hiddenCount = 0;
        foreach (var item in Catalog.Feed)
        {
            if (G.ShopLevel < item.Unlock) continue;
            var hidden = F.HiddenFeed!.Contains(item.Id);
            if (hidden) hiddenCount++;
            if (hidden && !showHiddenFeed) continue;
            var have = G.Count(item.Id);
            if (have <= 0 && !F.CareShowEmpty && !hidden) continue;
            shown++;

            var wait = item.Quality > 0 ? G.QualityTreatWait : TimeSpan.Zero;
            var waiting = wait > TimeSpan.Zero;
            var back = DairyGame.SellBackPrice(item);
            var lines = new[]
            {
                new Line($"You have {have}" + (have > 0 && !item.Baked ? $" · sells back for {back} each" : item.Baked ? " · from the dairy shelf" : ""), have > 0 ? Theme.Ink : Theme.InkSoft),
                new Line(Effects(item), Theme.SageDeep),
                new Line(item.Quality > 0 ? waiting ? $"Quality treat · next one in {DairyGame.Wait(wait)}" : "Quality treat · ready now" : "",
                    waiting ? Theme.HoneyDeep : Theme.InkSoft),
            };

            var buttons = new List<Btn>
            {
                new($"##eat{item.Id}", "Eat", "honey", problem == null && have > 0 && !waiting,
                    problem ?? (have <= 0 ? "You're out. Buy more in the Shop." : waiting ? $"Quality treats share a {Grades.QualityTreatHours:0}-hour cooldown." : item.Desc)),
            };
            if (have > 0 && !item.Baked) buttons.Add(new Btn($"##psell1{item.Id}", $"Sell 1 - {back}", "cream", true, $"Sell one back for {back} coins (half price)."));
            if (have > 1 && !item.Baked) buttons.Add(new Btn($"##psellall{item.Id}", $"Sell all - {back * have}", "cream", true, $"Sell all {have} back for {back * have} coins."));
            buttons.Add(new Btn($"##phide{item.Id}", hidden ? "Show" : "Hide", "cream", true,
                hidden ? "Put it back in your lists." : "Hide it from your Shop, Pantry and Care lists. Find it again under Show hidden."));

            var clicked = ListRow(Ico(item.Icon, hidden || have <= 0 ? 0.5f : 1f), item.Name + (hidden ? "  ·  hidden" : ""),
                hidden || have <= 0 ? Theme.InkSoft : Theme.Ink, lines, buttons, width);
            if (clicked < 0) continue;
            var id = buttons[clicked].Id;
            if (id.StartsWith("##eat")) G.UseFeed(item.Id);
            else if (id.StartsWith("##psell1")) G.SellFeed(item, 1);
            else if (id.StartsWith("##psellall")) G.SellFeed(item, have);
            else if (id.StartsWith("##phide")) G.SetFeedHidden(item, !hidden);
        }

        if (shown == 0) Ui.Wrapped("Your pantry is empty. Feed and treats you buy in the Shop show up here.", Theme.InkSoft, width);
        Gap(6);
        var showEmpty = F.CareShowEmpty;
        if (ImGui.Checkbox("Show feed I'm out of##pantryempty", ref showEmpty))
        {
            F.CareShowEmpty = showEmpty;
            plugin.SaveConfig();
        }

        if (hiddenCount > 0)
        {
            ImGui.SameLine();
            ImGui.Checkbox($"Show hidden ({hiddenCount})##pantryhidden", ref showHiddenFeed);
        }

        Gap(4);
        if (ImGui.SmallButton("Go to the Shop##pantryshop")) tab = TabShop;
    }

    /// <summary>The dairy shelf: treats the farmers baked, for any member to buy (the owner sets the price).</summary>
    private void DrawShelf(float width)
    {
        if (!G.Networked || F.NetMember == null) return;
        G.Net.RefreshShelf();
        var items = G.Net.Shelf;
        if (items.Count == 0)
        {
            Ui.Wrapped("Loading the shelf...", Theme.InkSoft, width);
            return;
        }

        if (items.All(s => s.Qty <= 0)) Ui.Wrapped("The shelf is empty right now. Your dairy's farmers bake treats for it in the Kitchen.", Theme.InkSoft, width);
        foreach (var s in items)
        {
            var feed = Catalog.FeedById(s.Item);
            var why = G.BuyShelfProblem(s);
            var lines = new List<Line>
            {
                new(s.Qty > 0 ? $"{s.Qty} on the shelf · {s.Price} coins each" : $"Sold out · {s.Price} coins each", s.Qty > 0 ? Theme.Ink : Theme.InkSoft),
                new(feed != null ? Effects(feed) : "", Theme.SageDeep),
            };
            var buttons = new List<Btn> { new($"##shelfbuy{s.Item}", $"Buy - {s.Price}", "honey", why == null, why ?? "Baked by your dairy's farmers. They get 80%, the fund 20%.") };
            if (F.NetMember.IsOwner)
            {
                buttons.Add(new Btn($"##shelfless{s.Item}", "-5", "cream", s.Price - 5 >= s.Min, $"Lower the price (at least {s.Min})"));
                buttons.Add(new Btn($"##shelfmore{s.Item}", "+5", "cream", s.Price + 5 <= s.Max, $"Raise the price (at most {s.Max})"));
            }

            var clicked = ListRow(Ico(feed?.Icon ?? "biscuit"), s.Name, Theme.Ink, lines.ToArray(), buttons, width);
            if (clicked < 0) continue;
            var id = buttons[clicked].Id;
            if (id.StartsWith("##shelfbuy")) G.BuyFromShelf(s);
            else if (id.StartsWith("##shelfless")) G.SetShelfPrice(s, s.Price - 5);
            else if (id.StartsWith("##shelfmore")) G.SetShelfPrice(s, s.Price + 5);
        }

        Gap(6);
    }

    private void DrawPantryInfo(float width)
    {
        QualityBar(width);
        Gap(8);
        var wait = G.QualityTreatWait;
        ImGui.TextColored(wait > TimeSpan.Zero ? Theme.HoneyDeep : Theme.SageDeep,
            wait > TimeSpan.Zero ? $"Next quality treat in {DairyGame.Wait(wait)}" : "A quality treat is ready");
        Ui.Wrapped($"Quality treats (biscuits, alfalfa, starclover) raise your milk quality toward the next grade. You can have one every hour.",
            Theme.InkSoft, width);
        Gap(6);
        if (F.Cow.BoostUntilUtc > DateTime.UtcNow)
            ImGui.TextColored(Theme.SageDeep, $"Feed boost: milk comes in faster for another {DairyGame.Wait(F.Cow.BoostUntilUtc - DateTime.UtcNow)}");
        Ui.Wrapped("Clover, salt licks and premium mix give a short boost that makes milk come in faster. Everyday feed keeps your hunger up.", Theme.InkSoft, width);
        Gap(6);
        Ui.Wrapped("You can only eat inside your dairy, but you can sell back or hide things from anywhere.", Theme.InkSoft, width);
    }
}
