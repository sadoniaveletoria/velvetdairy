using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VelvetDairy.Windows;

/// <summary>
/// Feeding time: a farmer rang the dairy's feeding bell. A pop-up near the top of the screen with the bell's chime:
/// pick one bar to get an extra bonus on top of it for 15 minutes. It goes away after a few minutes if you don't pick.
/// </summary>
public sealed class FeedingBellWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private string by = "";
    private double shownAt = -1;
    private const double StaySeconds = 180;

    public FeedingBellWindow(Plugin plugin, Fonts fonts)
        : base("##VelvetDairyFeedingBell", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
                                           ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        IsOpen = true;
        RespectCloseHotkey = false;
    }

    private static double Now => ImGui.GetTime();

    public void Show(string ringer)
    {
        by = ringer;
        shownAt = Now;
        plugin.Sounds.Play("feedbell.wav");
    }

    public override bool DrawConditions()
    {
        if (shownAt >= 0 && Now - shownAt > StaySeconds) shownAt = -1;
        var f = plugin.Config.Farm;
        return shownAt >= 0 && f.Confirmed18 && Plugin.ObjectTable.LocalPlayer != null;
    }

    public override void PreDraw()
    {
        PaperStyle.Push();
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + vp.Size.X / 2, vp.Pos.Y + vp.Size.Y * 0.22f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
    }

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        const string title = "Feeding time!";
        using (fonts.For(fonts.Heading, title)!.Push()) ImGui.TextColored(Theme.RoseDeep, title);
        ImGui.TextColored(Theme.Ink, $"{by} rang the feeding bell. Pick a bar: its bonus goes on top of what you have, for {VelvetDairy.Game.Catalog.BellMinutes} minutes.");
        ImGui.Spacing();
        foreach (var (bar, color) in new[] { ("Fed", "honey"), ("Watered", "blue"), ("Comfy", "rose"), ("Happy", "sage") })
        {
            if (Ui.Button($"##bell{bar}", $"{bar} +{plugin.Game.BellBonus(bar) * 100:0}%", new Vector2(120, 30), color, true, $"+{plugin.Game.BellBonus(bar) * 100:0}% on top of your {bar} bonus for {VelvetDairy.Game.Catalog.BellMinutes} minutes."))
            {
                plugin.Game.ClaimFeeding(bar);
                shownAt = -1;
            }

            ImGui.SameLine();
        }

        if (Ui.Button("##bellskip", "Not now", new Vector2(90, 30), "cream", true, "Skip this feeding."))
            shownAt = -1;
    }
}
