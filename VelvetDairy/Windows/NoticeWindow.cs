using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VelvetDairy.Windows;

/// <summary>
/// A small pop-up near the top of the screen for a few seconds: notices to your dairy, like a cow who's ready to be
/// bred again. One at a time; more wait their turn. Shown to anyone with the HUD on.
/// </summary>
public sealed class NoticeWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private readonly Queue<(string Title, string Text)> waiting = new();
    private (string Title, string Text)? showing;
    private double shownAt;
    private const double ShowSeconds = 8;

    public NoticeWindow(Plugin plugin, Fonts fonts)
        : base("##VelvetDairyNotice", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
                                      ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        IsOpen = true;
        RespectCloseHotkey = false;
    }

    public void Show(string title, string text)
    {
        waiting.Enqueue((title, text));
    }

    private static double Now => ImGui.GetTime();

    public override bool DrawConditions()
    {
        if (showing != null && Now - shownAt > ShowSeconds) showing = null;
        if (showing == null && waiting.Count > 0)
        {
            showing = waiting.Dequeue();
            shownAt = Now;
            plugin.Sounds.Play("ready.wav");
        }

        var f = plugin.Config.Farm;
        return showing != null && f.ShowHud && f.Confirmed18 && Plugin.ObjectTable.LocalPlayer != null;
    }

    public override void PreDraw()
    {
        PaperStyle.Push();
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + vp.Size.X / 2, vp.Pos.Y + vp.Size.Y * 0.16f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        // Fade out over the last second
        var left = ShowSeconds - (Now - shownAt);
        ImGui.SetNextWindowBgAlpha((float)Math.Clamp(left, 0.0, 1.0) * 0.96f);
    }

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        if (showing is not { } s) return;
        var alpha = (float)Math.Clamp(ShowSeconds - (Now - shownAt), 0.0, 1.0);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
        using (fonts.For(fonts.Heading, s.Title)!.Push()) ImGui.TextColored(Theme.RoseDeep, s.Title);
        ImGui.TextColored(Theme.Ink, s.Text);
        ImGui.PopStyleVar();
    }
}
