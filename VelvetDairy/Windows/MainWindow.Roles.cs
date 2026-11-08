using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

// Roles tab: one sub-tab per role (Cow, Farmer, Bull), each with its own level and what that role cares about.
public sealed partial class MainWindow
{
    private void DrawRoles(Vector2 min, Vector2 max)
    {
        var top = SubTabs("roles", new[] { "Cow", "Farmer", "Bull" }, min, max.X - min.X);
        var mid = (min.X + max.X) / 2;
        switch (Sub("roles"))
        {
            case 0:
                Panel("##cowlevel", top, new Vector2(mid - 7, max.Y), "Cow level", w =>
                {
                    RoleNote("cow", w);
                    DrawCowLevel(w);
                });
                Panel("##cowquality", new Vector2(mid + 7, top.Y), max, "Milk quality", DrawMilkQuality, true);
                break;
            case 1:
                Panel("##farmerlevel", top, new Vector2(mid - 7, max.Y), "Farmer level", DrawFarmerRole);
                Panel("##farmertank", new Vector2(mid + 7, top.Y), max, "Farmer tank", DrawFarmerTankSummary, true);
                break;
            default:
                Panel("##bulllevel", top, new Vector2(mid - 7, max.Y), "Bull level", DrawBullRole);
                Panel("##seedquality", new Vector2(mid + 7, top.Y), max, "Seed quality", DrawSeedQuality, true);
                break;
        }
    }

    /// <summary>When you don't have this role: say so, and offer to add it (cow and bull you can add yourself).</summary>
    private void RoleNote(string role, float width)
    {
        if (!G.Networked || G.Has(role)) return;
        var p = G.AddRoleProblem(null, role);
        Ui.Wrapped($"You're not a {role} in your dairy right now. Your {role} progress is kept either way.", Theme.RoseDeep, width);
        if (p == null)
        {
            if (Ui.Button($"##roleadd{role}", $"Become a {role} too", new Vector2(200, 30), "sage", true, "You keep your other roles.")) G.AddRole(null, role);
        }
        else ImGui.TextColored(Theme.InkSoft, p);

        Gap(8);
    }

    /// <summary>A big level header: seal, title, xp to the next level.</summary>
    private void LevelHeader(int xp, string title, Vector4 color, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var (lvl, into, need) = Leveling.FromXp(xp);
        var p = ImGui.GetCursorScreenPos();
        Ui.Seal(dl, p + new Vector2(44, 44), 42, $"{lvl}", color);
        Ui.Text(dl, fonts.Heading, title, p + new Vector2(100, 14), Theme.Ink);
        Ui.Text(dl, null, need == 0 ? "Max level" : $"{into} / {need} xp to level {lvl + 1}", p + new Vector2(100, 44), Theme.InkSoft);
        Ui.Bar(dl, p + new Vector2(100, 68), p + new Vector2(width, 76), need == 0 ? 1 : (float)into / need, color);
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(width, 96));
    }

    // ----- Cow -----

    private void DrawMilkQuality(float width)
    {
        QualityBar(width);
        Gap(6);
        var wait = G.QualityTreatWait;
        ImGui.TextColored(wait > TimeSpan.Zero ? Theme.HoneyDeep : Theme.SageDeep,
            wait > TimeSpan.Zero ? $"Next quality treat in {DairyGame.Wait(wait)}" : "A quality treat is ready (Pantry)");
        Gap(6);
        SubHeading("Grades and pay", width);
        var cur = G.CurrentGrade;
        for (var g = Grades.Count - 1; g >= 0; g--)
        {
            ImGui.TextColored(g == cur ? Theme.RoseDeep : Theme.Ink, $"Grade {Grades.Name(g)}");
            ImGui.SameLine(90);
            ImGui.TextColored(Theme.InkSoft, $"{G.PayRate(g):0.#} coins per oz" + (g == cur ? "  ·  yours now" : ""));
        }

        Gap(6);
        Ui.Wrapped("Quality builds as you're milked, fill goals and eat quality treats. Your care (hunger, thirst, comfort, mood, feed) decides the best grade you can make right now.",
            Theme.InkSoft, width);
    }

    // ----- Farmer -----

    private void DrawFarmerRole(float width)
    {
        RoleNote("farmer", width);
        LevelHeader(F.FarmerXp, $"Farmer level {G.FarmerLevel}", Theme.SageDeep, width);

        SubHeading("Coming up", width);
        var tank = Catalog.Upgrades.First(u => u.Id == "farmtank");
        var shown = 0;
        for (var i = 0; i < tank.Unlocks.Length && shown < 4; i++)
        {
            if (tank.Unlocks[i] <= G.FarmerLevel) continue;
            ImGui.TextColored(Theme.Ink, $"Lv {tank.Unlocks[i]}");
            ImGui.SameLine(56);
            ImGui.TextColored(Theme.InkSoft, $"{Catalog.FarmerTankNames[i + 1]} ({Catalog.FarmerTankSizes[i + 1]} oz) in the Shop");
            shown++;
        }

        if (shown == 0) ImGui.TextColored(Theme.InkSoft, "Every farmer tank is unlocked.");

        SubHeading("What farmers do", width);
        Ui.Wrapped("Hand milk your dairy's cows: stand close, target them and use a milking emote. You get 25% of what the cow is paid, on top.", Theme.Ink, width);
        Ui.Wrapped("Fill your farmer tank from the dairy tank (Creamery > Jugs) and deliver it: customer orders, other dairies' buy and visitor orders, and the weekly contract at the Grand Creamery.", Theme.Ink, width);
        Ui.Wrapped("Invite people and give them roles, the farmer role included.", Theme.Ink, width);

        SubHeading("How to earn farmer xp", width);
        Ui.Wrapped("Hand milking pay, filling jugs, customer orders, deliveries and contract shipments.", Theme.InkSoft, width);
    }

    // ----- Bull -----

    private void DrawBullRole(float width)
    {
        RoleNote("bull", width);
        LevelHeader(F.BullXp, $"Bull level {G.BullLevel}", Theme.HoneyDeep, width);
        Ui.Wrapped("Bulls are a role only for now. Things for bulls to do, and ways to earn bull xp, are coming in new updates. Your bull level is kept in the meantime.",
            Theme.InkSoft, width);
    }

    private void DrawSeedQuality(float width)
    {
        using (fonts.For(fonts.Heading, "Coming soon")!.Push()) ImGui.TextColored(Theme.HoneyDeep, "Coming soon");
        Gap(6);
        Ui.Wrapped("Bulls will have seed quality, the way cows have milk quality: built up with care and the right treats, with grades of its own.", Theme.InkSoft, width);
    }
}
