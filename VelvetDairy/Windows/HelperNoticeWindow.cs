using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// Once per character: if a plugin breeding needs (Corruption, Cummy) isn't installed, say so, with its repository
/// link and how to add it. Each missing plugin is only mentioned once; after that the HUD's dots show it.
/// </summary>
public sealed class HelperNoticeWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private double loggedInFor;
    private bool checkedThisSession;
    private Companions.Companion[] missing = System.Array.Empty<Companions.Companion>();

    public HelperNoticeWindow(Plugin plugin, Fonts fonts)
        : base("Eorzea Milkers: plugins for breeding##VelvetDairyHelpers", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        PositionCondition = ImGuiCond.Appearing;
    }

    /// <summary>
    /// Called every frame. Waits 15 seconds after logging in (so other plugins have loaded), and until the walkthrough
    /// and What's new are out of the way, then checks once.
    /// </summary>
    public void Check(float dt)
    {
        var f = plugin.Config.Farm;
        if (checkedThisSession || IsOpen || !f.Confirmed18 || !f.TutorialDone || Plugin.ObjectTable.LocalPlayer == null) return;
        loggedInFor += dt;
        if (loggedInFor < 15 || plugin.Tutorial.IsOpen || plugin.Changes.IsOpen) return;
        checkedThisSession = true;
        missing = plugin.Companions.All(plugin.Game.Now)
            .Where(c => !c.On && c.Repo.Length > 0 && !f.HelperNoticeSeen.Contains(c.Name))
            .ToArray();
        if (missing.Length > 0) IsOpen = true;
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public override void OnClose()
    {
        // Each one is only mentioned once
        var f = plugin.Config.Farm;
        foreach (var c in missing)
            if (!f.HelperNoticeSeen.Contains(c.Name)) f.HelperNoticeSeen.Add(c.Name);
        plugin.SaveConfig();
    }

    public override void Draw()
    {
        const string title = "Breeding needs other plugins";
        ImGui.PushTextWrapPos(480);
        using (fonts.For(fonts.Heading, title)!.Push()) ImGui.TextColored(Theme.RoseDeep, title);
        ImGui.Spacing();
        ImGui.TextColored(Theme.Ink, "Eorzea Milkers works without them, but bulls breeding cows, arousal and pregnancy come from these plugins, and you don't have:");
        ImGui.Spacing();

        foreach (var c in missing)
        {
            ImGui.TextColored(Theme.Ink, c.Name);
            ImGui.SameLine();
            ImGui.TextColored(Theme.InkSoft, $"· {c.For}");
            if (Ui.Button($"##copy{c.Name}", "Copy repository link", new Vector2(190, 28), "blue", true, c.Repo))
            {
                ImGui.SetClipboardText(c.Repo);
                plugin.Game.Notify($"Copied the {c.Name} repository link.", 1);
            }

            ImGui.SameLine();
            if (Ui.Button($"##page{c.Name}", "Open on GitHub", new Vector2(150, 28), "cream", true, c.Page))
                Dalamud.Utility.Util.OpenLink(c.Page);
            ImGui.Spacing();
        }

        ImGui.TextColored(Theme.InkSoft, "To add one: /xlsettings > Experimental > Custom Plugin Repositories. Paste the link, press +, then Save. Install it from /xlplugins.");
        ImGui.TextColored(Theme.InkSoft, "Your scene partner needs them too. The bottom of the main window's sidebar shows which plugins you have, with a green dot for each.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (Ui.Button("##helpersok", "Got it", new Vector2(120, 32), "rose", true, "This won't come up again for these plugins."))
            IsOpen = false;
    }
}
