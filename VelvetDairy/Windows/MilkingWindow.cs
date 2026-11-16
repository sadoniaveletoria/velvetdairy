using System;
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

        // The feeding bell: farmers and the owner, inside the dairy, once an hour
        if (G.CanCarry)
        {
            ImGui.Spacing();
            // Grey until the bell is off its hourly cooldown (and you're in the dairy, active)
            var why = G.BellProblem();
            var wait = G.BellWait;
            if (Ui.Button("##ringbell", wait > TimeSpan.Zero ? $"Bell ready in {DairyGame.Wait(wait)}" : "Ring the feeding bell", new Vector2(210, 30), "honey", why == null,
                    why ?? "Everyone in the dairy hears it: each cow and bull picks a bar to fill right up, and gets a 20 minute boost. Once an hour."))
                G.RingFeedingBell();
        }

        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.BeginChild("##handmilklist", Vector2.Zero, false))
            plugin.MainWindow.DrawHandMilkCows(ImGui.GetContentRegionAvail().X - 6, false);
        ImGui.EndChild();
    }
}
