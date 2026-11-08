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
            ImGui.TextColored(Theme.InkSoft, $"{G.PayRate(g):0.#} coins per oz" + (g == cur ? "  ·  yours now" : ""));
        }

        Gap(6);
        Ui.Wrapped("Quality builds as you're milked, fill goals and eat quality treats. Your grade is your quality, as long as you're milked every few days (it slips after 3 days without).",
            Theme.InkSoft, width);
    }

    /// <summary>The emotes your farmers milk you with, and whether they can start without asking.</summary>
    private void DrawCowEmotes(float width)
    {
        SubHeading("Being hand milked", width);
        Ui.Wrapped("A farmer of your dairy milks you by pressing Milk on you (Roles > Farmer): they stand close, target you and their plugin does a looping emote. Someone just doing an emote near you never milks you. Milk flows until you're empty or they stop.", Theme.InkSoft, width);
        var any = F.AnyLoopEmote;
        if (ImGui.Checkbox("Any looping emote counts##anyloop", ref any))
        {
            F.AnyLoopEmote = any;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Off: only the emotes listed below. One-shot gestures like /pet can't be seen by the plugin; use looping ones.");
        var auto = F.AutoHand;
        if (ImGui.Checkbox("Farmers can start milking me without asking##autohand", ref auto))
        {
            F.AutoHand = auto;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("On: a farmer holding a milking emote on you starts it. Off: you start it yourself (right-click them > Velvet Dairy > Let them milk me, or Barn > Milking).");
        DrawEmotes(width);
    }

    // ----- Farmer -----

    private void DrawFarmerRole(float width)
    {
        RoleNote("farmer", width);
        LevelHeader(F.FarmerXp, $"Farmer level {G.FarmerLevel}", Theme.SageDeep, width);
        DrawHandMilkCows(width);
        DrawMilkMod(width);
        DrawCleanStalls(width);

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
        Ui.Wrapped("Hand milk your dairy's cows: stand close, target them and press Milk below; it does the emote and stops when they're empty. You get 25% of what the cow is paid, on top.", Theme.Ink, width);
        Ui.Wrapped("Fill your farmer tank from the dairy tank (Creamery > Jugs) and deliver it: customer orders, other dairies' buy and visitor orders, and the weekly contract at the Grand Creamery.", Theme.Ink, width);
        Ui.Wrapped("Invite people and give them roles, the farmer role included.", Theme.Ink, width);

        SubHeading("How to earn farmer xp", width);
        Ui.Wrapped($"Grooming cows ({Catalog.GroomXp} each, every 15 min per cow), cleaning stalls ({Catalog.CleanXp} each), hand milking pay, filling jugs, customer orders, deliveries and contract shipments.", Theme.InkSoft, width);
    }

    private System.Collections.Generic.List<(string Dir, string Name)>? modList;
    private System.Collections.Generic.List<(string Group, string[] Options, bool Single)>? modGroups;
    private string modGroupsFor = "";

    /// <summary>The picked mod's options (like which animation it uses), chosen just for milking.</summary>
    private void DrawMilkModOptions()
    {
        if (F.MilkModDir.Length == 0) return;
        if (modGroups == null || modGroupsFor != F.MilkModDir)
        {
            modGroups = PenumbraLink.Groups(F.MilkModDir) ?? new();
            modGroupsFor = F.MilkModDir;
        }

        if (modGroups.Count == 0) return;
        ImGui.TextColored(Theme.InkSoft, "Options while milking (anything left as set in Penumbra keeps your Penumbra choice):");
        var changed = false;
        foreach (var (group, options, single) in modGroups)
        {
            if (options.Length == 0) continue;
            F.MilkModOptions.TryGetValue(group, out var picked);
            if (single)
            {
                var label = picked is { Count: > 0 } ? picked[0] : "As set in Penumbra";
                ImGui.SetNextItemWidth(260);
                if (ImGui.BeginCombo($"{group}##mmg{group}", label))
                {
                    if (ImGui.Selectable($"As set in Penumbra##mmgdef{group}", picked == null))
                    {
                        F.MilkModOptions.Remove(group);
                        changed = true;
                    }

                    foreach (var o in options)
                        if (ImGui.Selectable($"{o}##mmo{group}{o}", picked is { Count: > 0 } && picked[0] == o))
                        {
                            F.MilkModOptions[group] = new System.Collections.Generic.List<string> { o };
                            changed = true;
                        }

                    ImGui.EndCombo();
                }
            }
            else
            {
                ImGui.TextColored(Theme.Ink, group);
                var custom = picked != null;
                if (ImGui.Checkbox($"Choose for milking##mmgc{group}", ref custom))
                {
                    if (custom) F.MilkModOptions[group] = new System.Collections.Generic.List<string>();
                    else F.MilkModOptions.Remove(group);
                    changed = true;
                }

                if (F.MilkModOptions.TryGetValue(group, out var on))
                {
                    ImGui.Indent();
                    foreach (var o in options)
                    {
                        var b = on.Contains(o);
                        if (ImGui.Checkbox($"{o}##mmc{group}{o}", ref b))
                        {
                            if (b) on.Add(o);
                            else on.Remove(o);
                            changed = true;
                        }
                    }

                    ImGui.Unindent();
                }
            }
        }

        if (changed)
        {
            G.EndMilkMod();
            plugin.SaveConfig();
        }
    }

    /// <summary>For farmers: a Penumbra mod switched on only while the Milk button's emote plays (say, one that turns it into a massage).</summary>
    private void DrawMilkMod(float width)
    {
        if (!G.IsFarmer) return;
        SubHeading("Milking animation (Penumbra)", width);
        if (!PenumbraLink.Available)
        {
            Ui.Wrapped("Install Penumbra to use an animation mod (like one that turns /beesknees into a massage) only while you milk.", Theme.InkSoft, width);
            return;
        }

        Ui.Wrapped("Pick a mod that changes your milking emote. The Milk button switches it on for your character just before the emote and back off when the cow is empty. Your Penumbra settings aren't changed. Others see it only if they sync your mods (like with Mare).",
            Theme.InkSoft, width);
        modList ??= PenumbraLink.Sorted();
        var current = F.MilkModDir.Length == 0 ? "None" : modList.FirstOrDefault(m => m.Dir == F.MilkModDir).Name ?? F.MilkModDir;
        ImGui.SetNextItemWidth(Math.Min(320, width - 120));
        if (ImGui.BeginCombo("##milkmod", current))
        {
            if (ImGui.Selectable("None##milkmodnone", F.MilkModDir.Length == 0))
            {
                G.EndMilkMod();
                F.MilkModDir = "";
                F.MilkModOptions.Clear();
                modGroups = null;
                plugin.SaveConfig();
            }

            foreach (var (dir, name) in modList)
                if (ImGui.Selectable($"{name}##mm{dir}", dir == F.MilkModDir))
                {
                    G.EndMilkMod();
                    if (dir != F.MilkModDir) F.MilkModOptions.Clear();
                    F.MilkModDir = dir;
                    modGroups = null;
                    plugin.SaveConfig();
                }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh##milkmodrefresh"))
        {
            modList = PenumbraLink.Sorted();
            modGroups = null;
        }

        DrawMilkModOptions();

        ImGui.SetNextItemWidth(160);
        var em = F.MilkButtonEmote;
        if (ImGui.InputTextWithHint("Milk button emote##milkbtnemote", DairyGame.MilkEmote, ref em, 30))
        {
            F.MilkButtonEmote = em.Trim();
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("The emote the Milk button does: use the one your animation mod changes. It has to be a looping one. Empty = /beesknees.\nIf a cow only accepts their own list of emotes and yours isn't on it, the button uses theirs.");
        var typed = F.MilkButtonEmote.Length == 0 ? "" : F.MilkButtonEmote.StartsWith('/') ? F.MilkButtonEmote : "/" + F.MilkButtonEmote;
        if (typed.Length > 0 && Emotes.Find(typed) == 0) ImGui.TextColored(Theme.Warn, $"{typed} isn't an emote the game knows, so the Milk button uses /beesknees.");
        var redraw = F.MilkModRedraw;
        if (ImGui.Checkbox("Redraw me first (if the animation doesn't change)##milkmodredraw", ref redraw))
        {
            F.MilkModRedraw = redraw;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Some animation mods only show after your character reloads. This blinks you for a moment before the emote starts.");
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
    private void DrawHandMilkCows(float width)
    {
        SubHeading("Hand milk a cow", width);
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
                    lines.Add(new Line(st.Spoiled ? "Their milk spoiled; they have to dump it first." : $"{st.Milk:0.#} / {st.Cap:0} oz · Grade {Grades.Name(Math.Clamp(st.Grade, 0, Grades.Count - 1))}",
                        st.Spoiled ? Theme.Warn : Theme.InkSoft));
                    if (st.HandBy.Length > 0)
                        lines.Add(new Line(st.HandBy.Equals(myName, StringComparison.OrdinalIgnoreCase) ? "You're milking them" : $"Being milked by {st.HandBy}", Theme.RoseDeep));
                }
            }

            lines.Add(new Line($"Milk does {G.MilkEmoteFor(st)} for you", Theme.BlueDeep));
            // Target first; once they're your target the button becomes Milk (does their first looping milking emote for you)
            var targeted = Plugin.TargetManager.Target is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter tp && tp.Name.TextValue.Equals(c.Name, StringComparison.OrdinalIgnoreCase);
            // Your Milk emote (Roles > Farmer) if this cow accepts it; otherwise /beesknees or their first one
            var milkEmote = G.MilkEmoteFor(st);
            var buttons = Array.Empty<Btn>();
            if (near >= 0 && c.Online)
            {
                Btn first;
                if (!targeted) first = new Btn($"##tgt{c.Id}", "Target", "blue", true, "Targets them so your milking counts.");
                else
                {
                    var close = near <= G.HandRange;
                    first = new Btn($"##milk{c.Id}", "Milk", "rose", close, close ? $"Does {milkEmote} for you (as if you typed it). Hold still; it stops on its own when they're empty." : $"Get within {G.HandRange:0} yalms of them first.");
                }

                var groomWhy = G.GroomProblem(c.Name, c.Id, near);
                buttons = new[] { first, new Btn($"##groom{c.Id}", "Groom", "sage", groomWhy == null, groomWhy ?? $"Brush them down: their Happy bar gets 30 more minutes, you get {Catalog.GroomXp} farmer xp. Once every 15 minutes per cow.") };
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
            else if (clicked == 0 && !G.MilkCow(c.Name, milkEmote, st?.Milk ?? 0)) G.Notify($"{milkEmote} isn't an emote the game knows. Ask them to fix their milking emotes (Roles > Cow).", 2);
        }

        Ui.Wrapped("You get 25% of what the cow is paid, on top of their pay, plus farmer xp.", Theme.InkSoft, width);
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
