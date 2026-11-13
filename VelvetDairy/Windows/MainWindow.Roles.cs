using System;
using System.Collections.Generic;
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
                    DrawCowEmotes(w);
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
            ImGui.TextColored(Theme.InkSoft, Units.PerOz(G.PayRate(g)) + (g > 0 ? $" · +{Catalog.GradeBoost[g] * 100:0}% speed and milk" : "") + (g == cur ? "  ·  yours now" : ""));
        }

        Gap(6);
        Ui.Wrapped("Quality builds as you're milked, fill goals and eat quality treats. Your grade is your quality, as long as you're milked every few days (it slips after 3 days without).",
            Theme.InkSoft, width);
    }

    /// <summary>The emotes your farmers milk you with, and whether they can start without asking.</summary>
    private void DrawCowEmotes(float width)
    {
        SubHeading("Being hand milked", width);
        Ui.Wrapped("A farmer of your dairy milks you by pressing Milk on you (HUD > Herd Care): they stand close, target you and their plugin does their milking emote or pose (like /beesknees or a sit pose). Someone just doing an emote near you never milks you. Milk flows until you're empty or they stop.", Theme.InkSoft, width);
        var any = F.AnyLoopEmote;
        if (ImGui.Checkbox("Any emote or sit pose counts##anyloop", ref any))
        {
            F.AnyLoopEmote = any;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Units.Text("Off: only the emotes listed below. One-shot gestures like /pet can't be seen by the plugin; use looping ones."));
        var auto = F.AutoHand;
        if (ImGui.Checkbox("Farmers can start milking me without asking##autohand", ref auto))
        {
            F.AutoHand = auto;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Units.Text("On: a farmer pressing Milk on you starts it right away. Off: you get a pop-up asking if they can (Allow or Not now)."));
        DrawEmotes(width);
    }

    // ----- Farmer -----

    private void DrawFarmerRole(float width)
    {
        RoleNote("farmer", width);
        LevelHeader(F.FarmerXp, $"Farmer level {G.FarmerLevel}", Theme.SageDeep, width);
        DrawFarmerButtons(width);
        DrawCleanStalls(width);

        SubHeading("Coming up", width);
        var tank = Catalog.Upgrades.First(u => u.Id == "farmtank");
        var shown = 0;
        for (var i = 0; i < tank.Unlocks.Length && shown < 4; i++)
        {
            if (tank.Unlocks[i] <= G.FarmerLevel) continue;
            ImGui.TextColored(Theme.Ink, $"Lv {tank.Unlocks[i]}");
            ImGui.SameLine(56);
            ImGui.TextColored(Theme.InkSoft, $"{Catalog.FarmerTankNames[i + 1]} ({Units.Oz(Catalog.FarmerTankSizes[i + 1])}) in the Shop");
            shown++;
        }

        if (shown == 0) ImGui.TextColored(Theme.InkSoft, "Every farmer tank is unlocked.");

        SubHeading("What farmers do", width);
        Ui.Wrapped("Hand milk your dairy's cows (Herd Care button): stand close, target them and press Milk; it does the emote and stops when they're empty. You get 25% of what the cow is paid, on top.", Theme.Ink, width);
        Ui.Wrapped("Fill your farmer tank from the dairy tank (top of the Kitchen) and deliver it: customer orders, other dairies' buy and visitor orders, and the weekly contract at the Grand Creamery.", Theme.Ink, width);
        Ui.Wrapped("Invite people and give them roles, the farmer role included.", Theme.Ink, width);

        SubHeading("How to earn farmer xp", width);
        Ui.Wrapped($"Grooming cows ({Catalog.GroomXp} each, every 15 min per cow), cleaning stalls ({Catalog.CleanXp} each), hand milking pay, filling jugs, customer orders, deliveries and contract shipments.", Theme.InkSoft, width);
    }

    /// <summary>Hand milking and the Kitchen each open their own window (also on the HUD).</summary>
    private void DrawFarmerButtons(float width)
    {
        var buttons = new System.Collections.Generic.List<Btn>();
        if (G.IsFarmer) buttons.Add(new Btn("##farmhandmilk", "Herd Care", "rose", true, "Your dairy's cows and bulls: milk, groom and feed them."));
        if (G.CanCarry) buttons.Add(new Btn("##farmkitchen", "Kitchen", "honey", true, "Fill your jugs from the dairy tank and bake."));
        if (buttons.Count == 0) return;
        Gap(4);
        var clicked = Buttons(buttons, width);
        if (clicked >= 0)
        {
            if (buttons[clicked].Id == "##farmhandmilk") plugin.Milking.IsOpen = true;
            else plugin.Kitchen.IsOpen = true;
        }

        if (G.Baking is { } b)
            ImGui.TextColored(Theme.InkSoft, $"{Catalog.Doing(b.Recipe)} {Catalog.MadeName(b.Recipe.Makes).ToLowerInvariant()} · {F.Bake!.Done + 1} of {F.Bake.Count} · {(G.Where.InDairy ? $"{DairyGame.Wait(b.Left)} left" : "paused")}");
        ImGui.TextColored(Theme.InkSoft, $"Jugs: {Units.Num(G.Carried, "0.#")} / {Units.Num(G.MaxCarry)} {Units.Unit}");
        Gap(4);
    }

    /// <summary>For farmers and the owner: clean the dairy's stalls for a bonus to cows milked there.</summary>
    private void DrawCleanStalls(float width)
    {
        SubHeading("Clean the stalls", width);
        if (!G.Networked || F.NetMember == null || (!F.NetMember.IsOwner && !G.IsFarmer))
        {
            Ui.Wrapped("Farmers and the owner clean the dairy's stalls.", Theme.InkSoft, width);
            return;
        }

        Ui.Wrapped($"Walk up to a stall nobody is sitting on and press Clean. Cows milked there get +{Catalog.CleanBonus * 100:0}% speed and xp for an hour. It can be freshened up again once half an hour is left.", Theme.Ink, width);
        if (G.Stalls.Count == 0)
        {
            ImGui.TextColored(Theme.InkSoft, "Your dairy has no stalls yet.");
            return;
        }

        foreach (var s in G.Stalls)
        {
            var lines = new System.Collections.Generic.List<Line>();
            var d = G.DistanceTo(s);
            lines.Add(new Line(DairyGame.Fresh(s) ? $"Fresh for {DairyGame.Wait(DairyGame.FreshLeft(s))}" : "Not cleaned lately (normal speed)",
                DairyGame.Fresh(s) ? Theme.SageDeep : Theme.InkSoft));
            if (G.StallTaken(s)) lines.Add(new Line("Someone is sitting on it", Theme.RoseDeep));
            else if (d >= 0) lines.Add(new Line(d <= G.HandRange ? "Right here" : $"{d:0} y away", Theme.InkSoft));
            var why = G.CleanProblem(s);
            var buttons = new[] { new Btn($"##clean{s.Id}", "Clean", "sage", why == null, why ?? $"+{Catalog.CleanXp} farmer xp") };
            if (ListRow(Ico("bucket"), $"{s.Name} · {Catalog.StallTierNames[Math.Clamp(s.Tier, 1, 4)]}", Theme.Ink, lines, buttons, width) == 0) G.CleanStall(s);
        }
    }

    /// <summary>For farmers: the dairy's cows, whether they're close, and which emotes milk each of them.</summary>
    internal void DrawHandMilkCows(float width, bool heading = true)
    {
        if (heading) SubHeading("Hand milk a cow", width);
        if (!G.IsFarmer)
        {
            Ui.Wrapped("Only farmers hand milk. A farmer or the owner can give you the farmer role.", Theme.InkSoft, width);
            return;
        }

        Ui.Wrapped("Stand within 3 yalms of a cow, press Target, then Milk. Only the Milk button counts: doing an emote yourself doesn't milk anyone. Their plugin starts the milking (unless they turned that off) and milk flows until they're empty or you stop.",
            Theme.Ink, width);
        Gap(4);
        var mine = G.Net.Mine;
        var me = Plugin.ObjectTable.LocalPlayer;
        var myName = me?.Name.TextValue ?? "";
        var cows = mine?.Members.Where(m => m.Has("cow") && !m.Name.Equals(myName, StringComparison.OrdinalIgnoreCase)).ToList() ?? new();
        if (cows.Count == 0)
        {
            ImGui.TextColored(Theme.InkSoft, "No other cows in your dairy yet.");
            return;
        }

        foreach (var c in cows.OrderByDescending(c => c.Online).ThenBy(c => c.Name))
        {
            var near = -1f;
            if (me != null)
                foreach (var o in Plugin.ObjectTable)
                    if (o is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc && pc.Name.TextValue.Equals(c.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        near = Vector3.Distance(me.Position, pc.Position);
                        break;
                    }

            var st = c.Status;
            var lines = new System.Collections.Generic.List<Line>();
            if (!c.Online) lines.Add(new Line("Offline", Theme.InkSoft));
            else
            {
                lines.Add(new Line(near < 0 ? (st?.InDairy == true ? "In the dairy, not near you" : "Not in the dairy") : near <= G.HandRange ? $"Right here ({near:0.#} y)" : $"{near:0} y away",
                    near >= 0 && near <= G.HandRange ? Theme.SageDeep : Theme.InkSoft));
                if (st != null)
                {
                    lines.Add(new Line(st.Spoiled ? "Their milk spoiled; they have to dump it first." : $"{Units.Num(st.Milk, "0.#")} / {Units.Num(st.Cap, "0")} {Units.Unit} · Grade {Grades.Name(Math.Clamp(st.Grade, 0, Grades.Count - 1))}",
                        st.Spoiled ? Theme.Warn : Theme.InkSoft));
                    if (st.HandBy.Length > 0)
                        lines.Add(new Line(st.HandBy.Equals(myName, StringComparison.OrdinalIgnoreCase) ? "You're milking them" : $"Being milked by {st.HandBy}", Theme.RoseDeep));
                    else if (G.AwaitingAllow(c.Name)) lines.Add(new Line("Asked: waiting for them to press Allow...", Theme.HoneyDeep));
                    else if (st.Ask) lines.Add(new Line("Asks first: they get a pop-up to Allow", Theme.InkSoft));
                }
            }

            lines.Add(new Line($"Milk does {G.MilkEmoteFor(st)} for you", Theme.BlueDeep));
            if (!G.CowAcceptsMyEmote(st))
                lines.Add(new Line($"They only count {string.Join(", ", st!.Emotes)}: pick one of those in Animation... to milk them", Theme.Warn));
            // Target first; once they're your target the button becomes Milk (does their first looping milking emote for you)
            var targeted = Plugin.TargetManager.Target is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter tp && tp.Name.TextValue.Equals(c.Name, StringComparison.OrdinalIgnoreCase);
            // Always your own pick (Herd Care > Animation)
            var milkEmote = G.MilkEmoteFor(st);
            var buttons = Array.Empty<Btn>();
            if (near >= 0 && c.Online)
            {
                Btn first;
                if (!targeted) first = new Btn($"##tgt{c.Id}", "Target", "blue", true, "Targets them so your milking counts.");
                else
                {
                    var close = near <= G.HandRange;
                    var waiting = G.AwaitingAllow(c.Name);
                    first = new Btn($"##milk{c.Id}", waiting ? "Waiting" : "Milk", "rose", close && !waiting,
                        waiting ? "Waiting for them to press Allow." :
                        !close ? $"Get within {G.HandRange:0} yalms of them first." :
                        st?.Ask == true ? $"They get a pop-up first. Once they Allow, you do {milkEmote}; it stops on its own when they're empty." :
                        $"Does {milkEmote} for you (as if you typed it). Hold still; it stops on its own when they're empty.");
                }

                var groomWhy = G.GroomProblem(c.Name, c.Id, near);
                var feedWhy = G.FeedProblem(c.Name, c.Id, near);
                buttons = new[]
                {
                    first,
                    new Btn($"##groom{c.Id}", "Groom", "sage", groomWhy == null, groomWhy ?? $"Brush them down: their Happy bar gets 30 more minutes, you get {Catalog.GroomXp} farmer xp. Once every 15 minutes per cow."),
                    new Btn($"##feed{c.Id}", "Feed", "honey", feedWhy == null, feedWhy ?? G.FeedTip),
                };
            }

            var clicked = ListRow(Ico("milk"), c.Name, c.Online ? Theme.Ink : Theme.InkSoft, lines, buttons, width,
                c.Online && st is { Cap: > 0 } s ? s.Milk / s.Cap : -1, Theme.BlueDeep);
            if (clicked == 0 && !targeted)
            {
                foreach (var o in Plugin.ObjectTable)
                    if (o is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc && pc.Name.TextValue.Equals(c.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Plugin.TargetManager.Target = pc;
                        break;
                    }
            }
            else if (clicked == 1) G.Groom(c.Name, c.Id, near);
            else if (clicked == 2) G.FeedFromShelf(c.Name, c.Id, near);
            else if (clicked == 0 && !G.MilkCow(c.Name, milkEmote, st?.Milk ?? 0, st?.Ask == true)) G.Notify($"{milkEmote} isn't an emote the game knows. Ask them to fix their milking emotes (Roles > Cow).", 2);
        }

        Ui.Wrapped("You get 25% of what the cow is paid, on top of their pay, plus farmer xp.", Theme.InkSoft, width);
        DrawFeedBulls(width, myName, me);
    }

    /// <summary>Bulls (who aren't cows too) can be fed from the dairy shelf as well.</summary>
    private void DrawFeedBulls(float width, string myName, Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter? me)
    {
        var bulls = G.Net.Mine?.Members.Where(m => m.Has("bull") && !m.Has("cow") && !m.Name.Equals(myName, StringComparison.OrdinalIgnoreCase)).ToList() ?? new();
        if (bulls.Count == 0) return;
        SubHeading("Bulls", width);
        foreach (var b in bulls.OrderByDescending(b => b.Online).ThenBy(b => b.Name))
        {
            var near = -1f;
            if (me != null)
                foreach (var o in Plugin.ObjectTable)
                    if (o is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc && pc.Name.TextValue.Equals(b.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        near = Vector3.Distance(me.Position, pc.Position);
                        break;
                    }

            var line = !b.Online ? "Offline" : near < 0 ? "Not near you" : near <= G.HandRange ? $"Right here ({near:0.#} y)" : $"{near:0} y away";
            var why = b.Online ? G.FeedProblem(b.Name, b.Id, near) : "They're offline.";
            if (ListRow(Ico("biscuit"), b.Name, b.Online ? Theme.Ink : Theme.InkSoft, new[] { new Line(line, Theme.InkSoft) },
                    new[] { new Btn($"##feedb{b.Id}", "Feed", "honey", why == null, why ?? G.FeedTip) }, width) == 0)
                G.FeedFromShelf(b.Name, b.Id, near);
        }
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
