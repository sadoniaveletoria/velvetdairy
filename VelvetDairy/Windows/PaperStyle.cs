using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace VelvetDairy.Windows;

/// <summary>Plain ImGui windows (guide, tutorial, members) dressed in the boutique creamery colors.</summary>
public static class PaperStyle
{
    private static readonly (ImGuiCol Col, Vector4 Color)[] Colors =
    {
        (ImGuiCol.WindowBg, Theme.Paper), (ImGuiCol.ChildBg, Theme.Alpha(Theme.Linen, 0.35f)),
        (ImGuiCol.TitleBg, Theme.Rose), (ImGuiCol.TitleBgActive, Theme.RoseDeep), (ImGuiCol.TitleBgCollapsed, Theme.Rose),
        (ImGuiCol.Text, Theme.Ink), (ImGuiCol.TextDisabled, Theme.InkSoft),
        (ImGuiCol.FrameBg, Theme.Linen), (ImGuiCol.FrameBgHovered, Theme.Cream), (ImGuiCol.FrameBgActive, Theme.Cream),
        (ImGuiCol.Button, Theme.Lighten(Theme.Rose, 0.3f)), (ImGuiCol.ButtonHovered, Theme.Rose), (ImGuiCol.ButtonActive, Theme.RoseDeep),
        (ImGuiCol.Header, Theme.Lighten(Theme.Blue, 0.4f)), (ImGuiCol.HeaderHovered, Theme.Blue), (ImGuiCol.HeaderActive, Theme.BlueDeep),
        (ImGuiCol.Separator, Theme.Alpha(Theme.RoseDeep, 0.3f)), (ImGuiCol.Border, Theme.Alpha(Theme.RoseDeep, 0.35f)),
        (ImGuiCol.PopupBg, Theme.Paper), (ImGuiCol.CheckMark, Theme.RoseDeep),
        (ImGuiCol.SliderGrab, Theme.RoseDeep), (ImGuiCol.SliderGrabActive, Theme.RoseDeep),
        (ImGuiCol.ScrollbarBg, Theme.Cream), (ImGuiCol.ScrollbarGrab, Theme.Rose), (ImGuiCol.ScrollbarGrabHovered, Theme.RoseDeep),
    };

    public static void Push()
    {
        foreach (var (col, color) in Colors) ImGui.PushStyleColor(col, color);
    }

    public static void Pop() => ImGui.PopStyleColor(Colors.Length);
}
