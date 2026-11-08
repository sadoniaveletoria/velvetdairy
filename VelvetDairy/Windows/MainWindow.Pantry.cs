using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

// Pantry tab: the feed and treats you own. Eat them (inside your dairy), sell back what you don't need, or hide it.
public sealed partial class MainWindow
{
    private void DrawPantry(Vector2 min, Vector2 max)
    {
        var mid = min.X + (max.X - min.X) * 0.58f;
        Panel("##pantry", min, new Vector2(mid - 7, max.Y), "Your pantry", DrawPantryList);
        Panel("##pantryinfo", new Vector2(mid + 7, min.Y), max, "Milk quality", DrawPantryInfo, true);
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
            if (G.Level < item.Unlock) continue;
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
                new Line($"You have {have}" + (have > 0 ? $" · sells back for {back} each" : ""), have > 0 ? Theme.Ink : Theme.InkSoft),
                new Line(Effects(item), Theme.SageDeep),
                new Line(item.Quality > 0 ? waiting ? $"Quality treat · next one in {DairyGame.Wait(wait)}" : "Quality treat · ready now" : "",
                    waiting ? Theme.HoneyDeep : Theme.InkSoft),
            };

            var buttons = new List<Btn>
            {
                new($"##eat{item.Id}", "Eat", "honey", problem == null && have > 0 && !waiting,
                    problem ?? (have <= 0 ? "You're out. Buy more in the Shop." : waiting ? $"Quality treats share a {Grades.QualityTreatHours:0}-hour cooldown." : item.Desc)),
            };
            if (have > 0) buttons.Add(new Btn($"##psell1{item.Id}", $"Sell 1 - {back}", "cream", true, $"Sell one back for {back} coins (half price)."));
            if (have > 1) buttons.Add(new Btn($"##psellall{item.Id}", $"Sell all - {back * have}", "cream", true, $"Sell all {have} back for {back * have} coins."));
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
