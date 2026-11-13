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
                    DrawCowBreeding(w);
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
                Panel("##seedquality", new Vector2(mid + 7, top.Y), max, "Your seed", DrawSeedQuality, true);
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
        if (heading) SubHeading("Hand milk cows and bulls", width);
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
        var cows = mine?.Members.Where(m => (m.Has("cow") || m.Has("bull")) && !m.Name.Equals(myName, StringComparison.OrdinalIgnoreCase)).ToList() ?? new();
        if (cows.Count == 0)
        {
            ImGui.TextColored(Theme.InkSoft, "No other cows or bulls in your dairy yet.");
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
                    if (c.Has("cow"))
                        lines.Add(new Line(st.Spoiled ? "Their milk spoiled; they have to dump it first." : $"{Units.Num(st.Milk, "0.#")} / {Units.Num(st.Cap, "0")} {Units.Unit} · Grade {Grades.Name(Math.Clamp(st.Grade, 0, Grades.Count - 1))}",
                            st.Spoiled ? Theme.Warn : Theme.InkSoft));
                    if (c.Has("bull") && st.SeedCap > 0)
                        lines.Add(new Line(st.SeedSpoiled ? "Their seed spoiled; they have to dump it first." : $"Seed {Units.Num(st.SeedOz, "0.##")} / {Units.Num(st.SeedCap, "0.#")} {Units.Unit} · Grade {Grades.Name(Math.Clamp(st.SeedGrade, 0, Grades.Count - 1))}",
                            st.SeedSpoiled ? Theme.Warn : Theme.InkSoft));
                    else if (c.Has("bull") && !c.Has("cow")) lines.Add(new Line("Their plugin doesn't share seed yet (needs the update).", Theme.InkSoft));
                    if (st.HandBy.Length > 0)
                        lines.Add(new Line(st.HandBy.Equals(myName, StringComparison.OrdinalIgnoreCase) ? "You're milking them" : $"Being milked by {st.HandBy}", Theme.RoseDeep));
                    else if (G.AwaitingAllow(c.Name)) lines.Add(new Line("Asked: waiting for them to press Allow...", Theme.HoneyDeep));
                    else if (st.Ask) lines.Add(new Line("Asks first: they get a pop-up to Allow", Theme.InkSoft));
                    if (st.Preg.Length > 0) lines.Add(new Line($"Pregnant · {DairyGame.StageName(st.Preg)}", Theme.RoseDeep));
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

            var fill = !c.Online || st == null ? -1f
                : c.Has("cow") && st.Cap > 0 ? st.Milk / st.Cap
                : st.SeedCap > 0 ? st.SeedOz / st.SeedCap : -1f;
            var clicked = ListRow(Ico("milk"), c.Name, c.Online ? Theme.Ink : Theme.InkSoft, lines, buttons, width, fill, Theme.BlueDeep);
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
            else if (clicked == 0 && !G.MilkCow(c.Name, milkEmote, Math.Max(st?.Milk ?? 0, (st?.SeedOz ?? 0) / DairyGame.SeedRatio), st?.Ask == true)) G.Notify($"{milkEmote} isn't an emote the game knows. Ask them to fix their milking emotes (Roles > Cow).", 2);
        }

        Ui.Wrapped("You get 25% of what the cow or bull is paid, on top of their pay, plus farmer xp. A bull's seed is milked the same way, into the dairy's seed tank.", Theme.InkSoft, width);
    }

    // ----- Bull -----

    private void DrawBullRole(float width)
    {
        RoleNote("bull", width);
        LevelHeader(F.BullXp, $"Bull level {G.BullLevel}", Theme.HoneyDeep, width);
        if (Leveling.BullRank(G.BullLevel) is { } title) ImGui.TextColored(Theme.HoneyDeep, title);
        DrawFathered(width);

        SubHeading("Breeding", width);
        Ui.Wrapped("Bulls breed their dairy's cows in a scene with the Corruption plugin. When you finish in a scene with a cow of your dairy, you've bred them: they get a little more milk for a while, and you get bull xp.",
            Theme.Ink, width);
        Ui.Wrapped($"There's no limit on how often you breed, but you tire: after {DairyGame.FreshBreeds} breedings a day your breeding output drops 20% each time (down to 20%), which makes the cow's bonus and your xp smaller. It's back to full at midnight UTC.",
            Theme.InkSoft, width);
        Ui.Wrapped("Getting a cow pregnant is up to Corruption (their cycle and settings); Eorzea Milkers shows it when it happens.", Theme.InkSoft, width);
        if (!G.CorruptionOn) Ui.Wrapped("Corruption isn't installed or loaded, so breeding doesn't count yet. It's on GitHub: RaylaPetal/corruption.", Theme.Warn, width);
    }

    private void DrawSeedQuality(float width)
    {
        if (!G.ProducesSeed)
        {
            Ui.Wrapped("Bulls make seed the way cows make milk.", Theme.InkSoft, width);
            return;
        }

        var cow = F.Cow;
        ImGui.TextColored(Theme.Ink, $"Seed: {Units.Oz(cow.Seed, "0.##")} / {Units.Oz(G.SeedCapacity, "0.#")} · Grade {Grades.Name(G.SeedGrade)}");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(SeedGradeTip());
        ImGui.TextColored(Theme.InkSoft, G.SeedSpoiled ? "Spoiled: dump it before you can be milked."
            : G.SeedFull ? "Full: get milked at a stall or by a farmer."
            : $"+{Units.Oz(G.SeedPerMinute(), "0.000")} a minute ({G.SeedShare * 100:0.#}% of milk speed at Grade {Grades.Name(G.SeedGrade)})");
        if (G.SeedSpoiled && Ui.Button("##dumpseedrole", "Dump seed", new Vector2(130, 30), "rose", true, "Pour it all out. Your seed grade is one lower for a while."))
            G.DumpSeed();
        Gap(4);
        SeedQualityBar(width);
        Ui.Wrapped("Seed works like milk: the same needs (Fed, Watered, Comfy, Happy) speed it up, it's milked at stalls or by a farmer's hand into your dairy's seed tank, and it pays per oz. "
                   + $"Your vials hold a tenth of your bottles. Seed comes in at {DairyGame.SeedRateByGrade[0] * 100:0}% of milk speed at Grade C, up to {DairyGame.SeedRateByGrade[^1] * 100:0}% at S+, and each oz pays {DairyGame.SeedPriceMult:0}x milk's price.",
            Theme.InkSoft, width);

        SubHeading("Breeding output", width);
        var seed = G.SeedNow;
        ImGui.TextColored(seed >= 100 ? Theme.SageDeep : Theme.HoneyDeep, $"{seed}% for your next breeding · bred today: {F.DayBreeds}");
        ImGui.TextColored(Theme.InkSoft, F.DayBreeds < DairyGame.FreshBreeds
            ? $"{DairyGame.FreshBreeds - F.DayBreeds} more at full output today"
            : "You're tired: each breeding today gives a bit less. Full again at midnight UTC.");
        Ui.Wrapped($"Each breeding: up to {DairyGame.BreedXp} bull xp, and the cow gets +{DairyGame.BredBonus * 100:0}% milk for {DairyGame.BredMinutes} minutes, both scaled by your breeding output.", Theme.InkSoft, width);
    }

    /// <summary>Seed quality progress toward the next seed grade.</summary>
    private void SeedQualityBar(float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var cap = G.SeedGradeCap;
        var need = G.NextSeedQuality;
        var from = Grades.QualityNeed[cap];
        var t = need is { } n ? (F.SeedQuality - from) / (n - from) : 1f;
        // Label, then the bar under it, then the numbers, so nothing overlaps (like the milk quality bar)
        var labelH = Ui.Measure(fonts.Label, "A").Y;
        Ui.Text(dl, fonts.Label, $"SEED QUALITY - GRADE {Grades.Name(cap)}", p, Theme.InkSoft);
        Ui.Bar(dl, p + new Vector2(0, labelH + 4), new Vector2(p.X + Math.Min(width, 360), p.Y + labelH + 12), t, Theme.GradeColor(Math.Min(cap + 1, Grades.Count - 1)));
        Ui.Text(dl, null, need is { } nn ? $"{F.SeedQuality:0} / {nn:0} to Grade {Grades.Name(cap + 1)}" : $"{F.SeedQuality:0} - every grade unlocked", p + new Vector2(0, labelH + 16), Theme.InkSoft);
        if (ImGui.IsMouseHoveringRect(p, p + new Vector2(width, labelH + 36)))
            ImGui.SetTooltip(Units.Text($"Seed quality grows like milk quality: each 0.1 oz you're milked counts like 1 oz of milk (more by hand), and quality treats raise it too.\nSeed grades use the same steps as milk: {string.Join(", ", Grades.QualityNeed.Skip(1).Select((q, i) => $"{Grades.Name(i + 1)} at {q:0}"))}."));
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(width, labelH + 40));
    }

    /// <summary>Tooltip for a bull's seed grade.</summary>
    private string SeedGradeTip()
    {
        var cap = G.SeedGradeCap;
        return $"Seed Grade {Grades.Name(G.SeedGrade)} ({Grades.All[G.SeedGrade].Blurb}) if you're milked now.\n" +
               $"Higher seed grades make more seed ({G.SeedShare * 100:0.#}% of milk speed now, {DairyGame.SeedRateByGrade[^1] * 100:0}% at S+), milk faster and pay more.\n" +
               (G.SeedSlip > 0 ? $"Slipped {G.SeedSlip} grade{(G.SeedSlip > 1 ? "s" : "")}: it's been a while since you were milked.\n" : "") +
               (F.Cow.SeedTaint > 0 ? "One grade lower after dumping spoiled seed, until it wears off.\n" : "") +
               "Hand milking makes up one lost grade.\n" +
               $"Best grade your seed quality allows: {Grades.Name(cap)}." + (G.NextSeedQuality is { } q ? $" Grade {Grades.Name(cap + 1)} unlocks at {q:0} (you have {F.SeedQuality:0})." : "");
    }

    /// <summary>A cow who's also a bull: her seed vial, small, in the corner of the bottle card.</summary>
    private void DrawSeedVial(ImDrawListPtr dl, Vector2 at, float height)
    {
        var w = height / 2;
        var f = Math.Clamp(F.Cow.Seed / Math.Max(0.001f, G.SeedCapacity), 0f, 1f);
        Ui.Bottle(dl, at, new Vector2(w, height), f, G.Active != null ? Ui.Flow.Draining : G.SeedComing ? Ui.Flow.Filling : Ui.Flow.Still,
            G.SeedSpoiled ? SourMilk : SeedTint, G.BottleTier);
        Ui.Text(dl, fonts.Small, "SEED", new Vector2(at.X + w / 2, at.Y + height + 2), Theme.InkSoft, 0.5f);
        Ui.Text(dl, fonts.Small, Units.Num(F.Cow.Seed, "0.##"), new Vector2(at.X + w / 2, at.Y + height + 2 + Ui.Measure(fonts.Small, "Ag").Y), G.SeedSpoiled || G.SeedFull ? Theme.Warn : Theme.Ink, 0.5f);
        if (ImGui.IsMouseHoveringRect(at, at + new Vector2(w, height + 30)))
            ImGui.SetTooltip(Units.Text($"Seed: {F.Cow.Seed:0.##} / {G.SeedCapacity:0.#} oz\n") + (G.SeedSpoiled ? "Spoiled: dump it (Roles > Bull, or the HUD).\n" : G.SeedFull ? "Full: get milked.\n" : "") + SeedGradeTip());
    }

    /// <summary>Cows: being bred (a bull of your dairy, in a Corruption scene) and pregnancy (from Corruption).</summary>
    private void DrawCowBreeding(float width)
    {
        SubHeading("Breeding and pregnancy", width);
        if (!G.CorruptionOn)
        {
            Ui.Wrapped("With the Corruption plugin, a bull of your dairy can breed you (+5% milk for 30 minutes), and a pregnancy adds milk too. It's on GitHub: RaylaPetal/corruption.", Theme.InkSoft, width);
            return;
        }

        if (G.BredLeft > TimeSpan.Zero)
            ImGui.TextColored(Theme.RoseDeep, $"Bred by {F.BredBy}: +{G.BredBonusNow * 100:0.#}% milk for {(int)G.BredLeft.TotalMinutes + 1} more min");
        else if (G.BreedCooldownLeft > TimeSpan.Zero)
            ImGui.TextColored(Theme.InkSoft, $"Another breeding counts in {(int)G.BreedCooldownLeft.TotalMinutes + 1} min");
        else Ui.Wrapped($"When a bull of your dairy finishes in a Corruption scene with you, you're bred: +{DairyGame.BredBonus * 100:0}% milk for {DairyGame.BredMinutes} minutes (less if they're tired), once every {DairyGame.BredCooldownMinutes} minutes.", Theme.InkSoft, width);

        // Breeding scenes always end in a creampie (Corruption's forced creampies, set by Eorzea Milkers)
        if (G.ForceCreampieAnswer == "not-allowed")
            Ui.Wrapped("Breeding scenes with your dairy's bulls always count as a pussy creampie, but Corruption needs your OK first: turn on \"Let other plugins control Corruption\" in Corruption's Settings > Other plugins.", Theme.Warn, width);
        else Ui.Wrapped("Breeding scenes with your dairy's bulls always count as a pussy creampie, so Corruption rolls for conception every time a bull finishes.", Theme.InkSoft, width);

        var p = G.Preg;
        if (G.Pregnant)
        {
            Gap(4);
            ImGui.TextColored(Theme.RoseDeep, $"Pregnant · {DairyGame.StageName(p.Stage)} · milk +{G.PregnancyBonus * 100:0}%");
            var cur = ImGui.GetCursorScreenPos();
            var barW = Math.Min(width, 360);
            Ui.Bar(ImGui.GetWindowDrawList(), cur + new Vector2(0, 4), cur + new Vector2(barW, 12), G.PregnancyProgress, Theme.RoseDeep);
            ImGui.Dummy(new Vector2(barW, 18));
            var due = p.DueUtc - DateTime.UtcNow;
            if (p.FatherName.Length > 0) ImGui.TextColored(Theme.Ink, $"Father: {p.FatherName}" + (p.FatherWorld.Length > 0 ? $" ({p.FatherWorld})" : ""));
            ImGui.TextColored(Theme.InkSoft, $"Day {p.DaysPregnant:0} of {(p.DueUtc - p.ConceivedUtc).TotalDays:0}" + (due > TimeSpan.Zero ? $" · due in {(int)due.TotalDays}d {due.Hours}h" : " · due now"));
            if (p.Craving.Length > 0) ImGui.TextColored(p.CravingMet ? Theme.SageDeep : Theme.HoneyDeep, $"Craving today: {p.Craving}" + (p.CravingMet ? " (met)" : ""));
            ImGui.TextColored(Theme.InkSoft, "Milk +5% in the first trimester, +10% in the second, +15% in the third. It ends with the birth.");
        }
        else if (p.Stage == "recovering")
            ImGui.TextColored(Theme.InkSoft, p.RecoveryUntilUtc > DateTime.UtcNow ? $"Recovering after the birth for {(int)(p.RecoveryUntilUtc - DateTime.UtcNow).TotalHours + 1} more h" : "Recovering after the birth");
        else ImGui.TextColored(Theme.InkSoft, "Not pregnant. Pregnancy comes from Corruption's own breeding (its cycle and settings); it shows here once it's confirmed (a test, or at day 5).");

        // Past births, newest first, with their fathers
        var births = G.BirthsNow;
        if (births.Count > 0)
        {
            Gap(4);
            ImGui.TextColored(Theme.Ink, $"Births: {births.Count}");
            foreach (var b in births.AsEnumerable().Reverse().Take(5))
                ImGui.TextColored(Theme.InkSoft, $"  {b.BornUtc.ToLocalTime():MMM d}: father {(b.FatherName.Length > 0 ? b.FatherName : "unknown")}");
            if (births.Count > 5) ImGui.TextColored(Theme.InkSoft, $"  and {births.Count - 5} more");
        }

        if (G.IsBull) DrawFathered(width);
    }

    /// <summary>A bull: the cows of your dairy pregnant by you, and how many children you've fathered (from Corruption).</summary>
    private void DrawFathered(float width)
    {
        if (!G.CorruptionOn) return;
        var mine = G.PregnantByMe();
        var fathered = G.Preg.Fathered;
        if (mine.Count == 0 && fathered == 0) return;
        Gap(4);
        foreach (var (name, stage) in mine)
            ImGui.TextColored(Theme.RoseDeep, $"Pregnant by you: {name} · {DairyGame.StageName(stage)}");
        if (fathered > 0) ImGui.TextColored(Theme.InkSoft, $"Children you've fathered: {fathered}");
    }
}
