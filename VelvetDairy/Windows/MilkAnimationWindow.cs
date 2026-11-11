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
        DrawEmote(width);
        ImGui.Separator();
        ImGui.Spacing();
        DrawPicker(width);
    }

    private void DrawEmote(float width)
    {
        ImGui.TextColored(Theme.RoseDeep, "Milk button emote");
        ImGui.SetNextItemWidth(180);
        var em = F.MilkButtonEmote;
        if (ImGui.InputTextWithHint("##milkbtnemote", DairyGame.MilkEmote, ref em, 30))
        {
            F.MilkButtonEmote = em.Trim();
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("If a cow only accepts their own list of emotes and yours isn't on it, the button uses theirs.");
        Ui.Wrapped("The emote the Milk button does. Use the one your animation mod changes; it has to be a looping one. Empty = /beesknees.", Theme.InkSoft, width);
        var typed = F.MilkButtonEmote.Length == 0 ? "" : F.MilkButtonEmote.StartsWith('/') ? F.MilkButtonEmote : "/" + F.MilkButtonEmote;
        if (typed.Length > 0 && Emotes.Find(typed) == 0) ImGui.TextColored(Theme.Warn, $"{typed} isn't an emote the game knows, so the Milk button uses /beesknees.");
        ImGui.Spacing();
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

    /// <summary>The Milk button's emote, then the Penumbra mod and its options.</summary>
    private void DrawPicker(float width)
    {
        if (!PenumbraLink.Available)
        {
            Ui.Wrapped("Install Penumbra to use an animation mod (like one that turns /beesknees into a massage) only while you milk.", Theme.InkSoft, width);
            return;
        }

        ImGui.TextColored(Theme.RoseDeep, "Animation mod (Penumbra)");
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
