using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// Herd Care in its own window (HUD > Herd Care): your dairy's cows, how close and how full they are,
/// and Target / Milk / Groom for each, with the milking animation picker one click away.
/// </summary>
public sealed class MilkingWindow : Window
{
    private readonly Plugin plugin;
    private FarmSave F => plugin.Config.Farm;
    private DairyGame G => plugin.Game;

    public MilkingWindow(Plugin plugin) : base("Herd Care##VelvetDairyHandMilk", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(520, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 260), MaximumSize = new Vector2(1200, 1400) };
    }

    /// <summary>Opening it brings your dairy's info up to date (it's checked less often while nothing shows it).</summary>
    public override void OnOpen() => plugin.Net.MineWithin(1);

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        // Clicking in Eorzea Milkers's own windows counts as being awake
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) G.Touch();
        var emote = G.MilkEmoteFor(null);
        var mod = F.MilkModDir.Length == 0 ? "no animation mod" : PenumbraLink.NameOf(F.MilkModDir);
        ImGui.TextColored(Theme.InkSoft, $"Milk does {emote} · {mod}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Animation...##hmanim")) plugin.MilkAnimation.IsOpen = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Units.Text("Pick the emote the Milk button does, and a Penumbra animation mod used only while you milk."));
        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.BeginChild("##handmilklist", Vector2.Zero, false))
            plugin.MainWindow.DrawHandMilkCows(ImGui.GetContentRegionAvail().X - 6, false);
        ImGui.EndChild();
    }
}
