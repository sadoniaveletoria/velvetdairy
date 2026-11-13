using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;
using VelvetDairy.Network;

namespace VelvetDairy.Windows;

/// <summary>
/// Apply to join a dairy from the Dairies tab: the roles you want (only the ones it's looking for) and a short note.
/// The owner (or its farmers, if the owner allows it) accepts or denies; accepting makes you a member.
/// </summary>
public sealed class ApplyWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private DairyListing? dairy;
    private readonly HashSet<string> roles = new();
    private string note = "";
    private bool sending;

    private static readonly (string Id, string Name)[] Roles = { ("cow", "Cow"), ("farmer", "Farmer"), ("bull", "Bull") };

    public ApplyWindow(Plugin plugin, Fonts fonts) : base("Apply to a dairy##VelvetDairyApply", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        this.fonts = fonts;
    }

    private DairyGame G => plugin.Game;

    public void Open(DairyListing d)
    {
        dairy = d;
        roles.Clear();
        // Start with what you already play, if they want it
        foreach (var (id, _) in Roles)
            if (d.LookingFor.Contains(id) && G.MyRoles.Contains(id)) roles.Add(id);
        if (roles.Count == 0 && d.LookingFor.Count > 0) roles.Add(d.LookingFor[0]);
        note = "";
        sending = false;
        WindowName = $"Apply to {d.Name}##VelvetDairyApply";
        IsOpen = true;
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        if (dairy is not { } d)
        {
            IsOpen = false;
            return;
        }

        const float width = 420;
        ImGui.PushTextWrapPos(width);
        using (fonts.For(fonts.Heading, d.Name)!.Push()) ImGui.TextColored(Theme.RoseDeep, d.Name);
        ImGui.TextColored(Theme.InkSoft, $"{(d.IsPublic ? d.Address : "Private")} · {d.WorldName} · {d.Members} member{(d.Members == 1 ? "" : "s")}");
        if (d.Description.Length > 0) ImGui.TextColored(Theme.Ink, d.Description);
        ImGui.Separator();

        ImGui.TextColored(Theme.RoseDeep, "Join as");
        foreach (var (id, name) in Roles)
        {
            var wanted = d.LookingFor.Contains(id);
            var on = roles.Contains(id) && wanted;
            ImGui.BeginDisabled(!wanted || sending);
            if (ImGui.Checkbox($"{name}{(wanted ? "" : " (not wanted)")}##apr{id}", ref on))
            {
                if (on) roles.Add(id);
                else roles.Remove(id);
            }

            ImGui.EndDisabled();
            ImGui.SameLine();
        }

        ImGui.NewLine();
        var levels = string.Join(" · ", new[] { $"Cow {G.Level}", G.FarmerLevel > 1 ? $"Farmer {G.FarmerLevel}" : "", G.BullLevel > 1 ? $"Bull {G.BullLevel}" : "" }.Where(x => x.Length > 0));
        ImGui.TextColored(Theme.InkSoft, $"They'll see your name, world and levels: {levels}.");

        ImGui.TextColored(Theme.RoseDeep, "A note to them (optional)");
        ImGui.InputTextMultiline("##applynote", ref note, 200, new Vector2(width, 64));
        ImGui.TextColored(Theme.InkSoft, $"{note.Length} / 200");
        ImGui.Spacing();

        var why = roles.Count == 0 ? "Pick at least one role." : null;
        if (Ui.Button("##applysend", sending ? "Sending..." : "Send application", new Vector2(180, 32), "rose", why == null && !sending, why))
        {
            sending = true;
            plugin.Net.Apply(d.Id, Roles.Select(r => r.Id).Where(roles.Contains).ToList(), note.Trim(), _ =>
            {
                G.Notify($"Application sent to {d.Name}. You'll get a message when they answer.", 1);
                IsOpen = false;
            }, e =>
            {
                sending = false;
                G.Notify(e, 2);
            });
        }

        ImGui.SameLine();
        if (Ui.Button("##applycancel", "Cancel", new Vector2(100, 32), "cream", true, null)) IsOpen = false;
        ImGui.TextColored(Theme.InkSoft, "You can apply to several dairies; when one accepts you, the others are withdrawn.");
        ImGui.PopTextWrapPos();
    }
}
