using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VelvetDairy.Windows;

/// <summary>
/// A scene with a partner who, like you, is both cow and bull: who breeds this time? Asked once per scene, near the
/// top of the screen. Whoever picks "I breed them" is the one breeding; the other's finish doesn't breed them.
/// </summary>
public sealed class BreedChoiceWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;

    public BreedChoiceWindow(Plugin plugin, Fonts fonts)
        : base("##VelvetDairyBreedChoice", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
                                           ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        IsOpen = true;
        RespectCloseHotkey = false;
    }

    public override bool DrawConditions()
    {
        var f = plugin.Config.Farm;
        return f.Confirmed18 && Plugin.ObjectTable.LocalPlayer != null && plugin.Game.WantsBreedChoice;
    }

    public override void PreDraw()
    {
        PaperStyle.Push();
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + vp.Size.X / 2, vp.Pos.Y + vp.Size.Y * 0.26f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
    }

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        var g = plugin.Game;
        var name = g.SceneNow.PartnerName;
        const string title = "Who breeds this scene?";
        using (fonts.For(fonts.Heading, title)!.Push()) ImGui.TextColored(Theme.RoseDeep, title);
        ImGui.PushTextWrapPos(420);
        ImGui.TextColored(Theme.Ink, g.BothChoseToBreed
            ? $"You and {name} both chose to breed the other. One of you picks \"They breed me\"."
            : $"You and {name} are both cow and bull. Whoever chooses to breed is the one breeding: only their finish counts this scene.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (Ui.Button("##breedme", $"I breed {name}", new Vector2(170, 30), "rose", true, $"When you finish, you breed {name}. Their finish doesn't breed you this scene."))
            g.ChooseToBreed(true);
        ImGui.SameLine();
        if (Ui.Button("##breedthem", "They breed me", new Vector2(150, 30), "blue", true, $"{name} chooses to breed you. Until they do, finishing doesn't breed either of you."))
            g.ChooseToBreed(false);
        ImGui.SameLine();
        if (Ui.Button("##breedlater", "Not now", new Vector2(100, 30), "cream", true, "Hide this. No one is bred unless one of you chooses."))
            g.DismissBreedChoice();
    }
}
