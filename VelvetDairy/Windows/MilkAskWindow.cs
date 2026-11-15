using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// For cows who turned off "Farmers can start milking me without asking": a farmer pressing Milk on you
/// pops this up, and milking only starts if you say yes.
/// </summary>
public sealed class MilkAskWindow : Window
{
    private readonly Plugin plugin;
    private DairyGame G => plugin.Game;

    public MilkAskWindow(Plugin plugin)
        : base("Milking request##VelvetDairyMilkAsk", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        IsOpen = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        PositionCondition = ImGuiCond.Appearing;
    }

    public override bool DrawConditions() => (G.MilkAsk != null || G.BreedAsk != null) && plugin.Config.Farm.Confirmed18;

    public override void PreDraw()
    {
        var vp = ImGui.GetMainViewport();
        Position = vp.Pos + new Vector2(vp.Size.X / 2 - 170, vp.Size.Y * 0.3f);
        PaperStyle.Push();
    }

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        if (G.BreedAsk is { } bull)
        {
            // A bull from another dairy, in a Corruption scene with you
            ImGui.TextColored(Theme.Ink, $"{bull} wants to breed you.");
            ImGui.TextColored(Theme.InkSoft, "They're a bull from another dairy. If you allow it, their finish counts as a breeding\n(and always inside, so Corruption rolls for conception).");
            ImGui.Spacing();
            if (Ui.Button("##breedallow", "Allow", new Vector2(130, 32), "rose", true, "Let them breed you this scene.")) G.AnswerBreedAsk(true);
            ImGui.SameLine();
            if (Ui.Button("##breednot", "No", new Vector2(130, 32), "cream", true, "Their finish won't count as a breeding.")) G.AnswerBreedAsk(false);
            if (G.MilkAsk == null) return;
            ImGui.Separator();
        }

        if (G.MilkAsk is not { } f) return;
        ImGui.TextColored(Theme.Ink, $"{f.Name} wants to milk you.");
        ImGui.TextColored(Theme.InkSoft, $"You have {Units.Oz(plugin.Config.Farm.Cow.Milk, "0.#")}. They're doing their Milk emote on you.");
        ImGui.Spacing();
        if (Ui.Button("##askallow", "Allow", new Vector2(130, 32), "rose", true, "Start hand milking now.")) G.AnswerMilkAsk(true);
        ImGui.SameLine();
        if (Ui.Button("##asknot", "Not now", new Vector2(130, 32), "cream", true, "They can ask again in 2 minutes.")) G.AnswerMilkAsk(false);
        ImGui.Spacing();
        ImGui.TextColored(Theme.InkSoft, G.Where.Guest ? "You're a guest here: farmers of this dairy always ask first." : "Roles > Cow: turn on \"start without asking\" to skip this.");
    }
}
