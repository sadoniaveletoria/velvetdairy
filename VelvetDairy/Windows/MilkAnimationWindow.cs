using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// Farmers pick what the Milk button does: the emote, and a Penumbra animation mod (with its options) that's switched on
/// only while they milk. Its own window so a long list of mod options doesn't bury the rest of the Farmer page.
/// </summary>
public sealed class MilkAnimationWindow : Window
{
    private readonly Plugin plugin;
    private FarmSave F => plugin.Config.Farm;
    private DairyGame G => plugin.Game;

    public MilkAnimationWindow(Plugin plugin) : base("Milking animation##VelvetDairyMilkAnim", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(440, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 260), MaximumSize = new Vector2(900, 1400) };
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        var width = ImGui.GetContentRegionAvail().X;
        // The mod (and its animation) first: when it changes an emote, that decides what the Milk button does
        DrawPicker(width);
        ImGui.Separator();
        ImGui.Spacing();
        DrawEmote(width);
    }

    private string emoteSearch = "";

    private void SetEmote(string command)
    {
        F.MilkButtonEmote = command.Equals(DairyGame.MilkEmote, StringComparison.OrdinalIgnoreCase) ? "" : command;
        plugin.SaveConfig();
    }

    private void DrawEmote(float width)
    {
        if (G.MilkModPick is { } pick)
        {
            ImGui.TextColored(Theme.RoseDeep, "What the Milk button does");
            ImGui.TextColored(Theme.Ink, $"{pick.Name}  ({pick.Command})");
            Ui.Wrapped(pick.Pose >= 0
                    ? $"Your animation mod's choice is a sit pose (pose {pick.Pose}). The Milk button sits you down with the mod on; if it isn't the right pose, /cpose once you're seated."
                    : "That's the emote your animation mod's choice changes, so the Milk button does it with the mod on.",
                Theme.InkSoft, width);
            Ui.Wrapped("To pick your own emote instead, set the animation mod to None.", Theme.InkSoft, width);
            return;
        }

        if (F.MilkModDir.Length > 0)
            Ui.Wrapped("Couldn't tell which emote your animation mod changes (pick its option above), so pick the emote it animates here.", Theme.HoneyDeep, width);
        ImGui.TextColored(Theme.RoseDeep, "Milk button emote or pose");
        Ui.Wrapped("What you do while you milk: any emote that keeps going (like /beesknees) or a sit pose (/sit, /groundsit, /doze). Use the one your animation mod changes. Once you're seated you can /cpose to the pose you want.", Theme.InkSoft, width);

        var cur = F.MilkButtonEmote.Length == 0 ? DairyGame.MilkEmote : F.MilkButtonEmote.StartsWith('/') ? F.MilkButtonEmote : "/" + F.MilkButtonEmote;
        var curId = Emotes.Find(cur);
        var label = curId == 0 ? cur : $"{Emotes.Name(curId)}  ({cur})";

        // The usual picks, one click
        foreach (var (cmd, name) in new[] { ("/beesknees", "Bee's Knees"), ("/sit", "Sit"), ("/groundsit", "Sit on ground"), ("/doze", "Doze") })
        {
            var on = cur.Equals(cmd, StringComparison.OrdinalIgnoreCase);
            if (on) ImGui.PushStyleColor(ImGuiCol.Button, Theme.Sage);
            if (ImGui.SmallButton($"{name}##mbq{cmd}")) SetEmote(cmd);
            if (on) ImGui.PopStyleColor();
            ImGui.SameLine();
        }

        ImGui.NewLine();
        ImGui.SetNextItemWidth(Math.Min(320, width));
        if (ImGui.BeginCombo("##milkbtnemote", label, ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.InputTextWithHint("##milkemotesearch", "Search emotes", ref emoteSearch, 40);
            foreach (var (id, cmd, name) in Emotes.Choices())
            {
                if (emoteSearch.Length > 0 && !name.Contains(emoteSearch, StringComparison.OrdinalIgnoreCase) && !cmd.Contains(emoteSearch, StringComparison.OrdinalIgnoreCase)) continue;
                var unlocked = Emotes.Unlocked(id);
                if (!unlocked) ImGui.PushStyleColor(ImGuiCol.Text, Theme.InkSoft);
                if (ImGui.Selectable($"{name}  {cmd}{(unlocked ? "" : "  (not unlocked)")}##mbe{id}", id == curId)) SetEmote(cmd);
                if (!unlocked) ImGui.PopStyleColor();
            }

            ImGui.EndCombo();
        }

        if (curId != 0 && !Emotes.Unlocked(curId)) ImGui.TextColored(Theme.Warn, "Your character hasn't unlocked this one, so the game won't do it.");
        if (curId == 0) ImGui.TextColored(Theme.Warn, $"{cur} isn't an emote the game knows, so the Milk button uses /beesknees.");
        ImGui.TextColored(Theme.InkSoft, "If a cow only accepts their own list and yours isn't on it, the button uses theirs.");
        ImGui.Spacing();
    }

    private System.Collections.Generic.List<(string Dir, string Name)>? modList;
    private System.Collections.Generic.List<(string Group, string[] Options, bool Single)>? modGroups;
    private string modGroupsFor = "";

    /// <summary>"  → Sit on ground" after an option that changes an emote.</summary>
    private string EmoteTag(string group, string option) =>
        ModAnimations.ForOption(F.MilkModDir, group, option) is { } p ? $"   → {p.Name}" : "";

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
        ImGui.TextColored(Theme.RoseDeep, "Mod options while milking");
        Ui.Wrapped("Anything left as set in Penumbra keeps your Penumbra choice.", Theme.InkSoft, ImGui.GetContentRegionAvail().X);
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
                        if (ImGui.Selectable($"{o}{EmoteTag(group, o)}##mmo{group}{o}", picked is { Count: > 0 } && picked[0] == o))
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
                        if (ImGui.Checkbox($"{o}{EmoteTag(group, o)}##mmc{group}{o}", ref b))
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

    /// <summary>The Milk button's emote, then the Penumbra mod and its options.</summary>
    private void DrawPicker(float width)
    {
        if (!PenumbraLink.Available)
        {
            Ui.Wrapped("Install Penumbra to use an animation mod (like one that turns /beesknees into a massage) only while you milk.", Theme.InkSoft, width);
            return;
        }

        ImGui.TextColored(Theme.RoseDeep, "Animation mod (Penumbra)");
        Ui.Wrapped("Pick an animation mod, and below it the animation you want. The Milk button switches the mod on for your character, does the emote that animation changes, and switches it back off when the cow is empty. Your Penumbra settings aren't changed. Others see it only if they sync your mods (like with Mare).",
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

        var redraw = F.MilkModRedraw;
        if (ImGui.Checkbox("Redraw me first (if the animation doesn't change)##milkmodredraw", ref redraw))
        {
            F.MilkModRedraw = redraw;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Some animation mods only show after your character reloads. This blinks you for a moment before the emote starts.");
        ImGui.Spacing();
        DrawMilkModOptions();
    }
}
