using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// The owner's HUD: your dairy at a glance. Who's online and in the dairy, how full each cow is,
/// invites waiting for an answer and your open buy orders. Only shown to the dairy's owner.
/// </summary>
public sealed class OwnerHudWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;

    private FarmSave F => plugin.Config.Farm;
    private DairyGame G => plugin.Game;

    private const float W = 300f;

    public OwnerHudWindow(Plugin plugin, Art art, Fonts fonts)
        : base("Velvet Dairy Owner##VelvetDairyOwnerHud",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        Position = new Vector2(60, 500);
        PositionCondition = ImGuiCond.FirstUseEver;
        IsOpen = true;
        RespectCloseHotkey = false;
    }

    public override bool DrawConditions() =>
        F.ShowHud && F.ShowOwnerHud && F.Confirmed18 && G.Networked && F.NetMember?.IsOwner == true && G.Net.Mine != null &&
        Plugin.ObjectTable.LocalPlayer != null;

    public override void PreDraw()
    {
        if (F.LockHud) Flags |= ImGuiWindowFlags.NoMove;
        else Flags &= ~ImGuiWindowFlags.NoMove;

        var mine = G.Net.Mine!;
        var others = mine.Members.Count(m => m.Id != mine.You?.Id);
        var rows = Math.Min(6, Math.Max(1, others));
        Size = new Vector2(W, 96 + rows * 22 + (mine.Invites.Count > 0 || G.Net.MyOrders.Count > 0 ? 22 : 0));
        SizeCondition = ImGuiCond.Always;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

    public override void Draw()
    {
        var dl = ImGui.GetWindowDrawList();
        var o = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        Ui.Card(dl, o, o + size, true);
        var mine = G.Net.Mine!;
        var x = o.X + 22;
        var right = o.X + size.X - 22;

        Ui.Text(dl, fonts.Label, $"OWNER · DAIRY LV {G.DairyLevel}", new Vector2(x, o.Y + 16), Theme.BlueDeep);
        Ui.Text(dl, fonts.Heading, mine.Dairy?.Name ?? F.NetMember!.Dairy, new Vector2(x, o.Y + 34), Theme.Ink);

        var members = mine.Members.Where(m => m.Id != mine.You?.Id).OrderByDescending(m => m.Online).ThenBy(m => m.Role).ThenBy(m => m.Name).ToList();
        var online = members.Count(m => m.Online);
        var inside = members.Count(m => m.Online && m.Status?.InDairy == true);
        Ui.Text(dl, fonts.Small, $"{online} of {members.Count} online · {inside} here", new Vector2(right, o.Y + 20), Theme.SageDeep, 1f);

        var y = o.Y + 66;
        dl.PushClipRect(new Vector2(x, o.Y), new Vector2(right, o.Y + size.Y), true);
        if (members.Count == 0) Ui.Text(dl, fonts.Small, "No members yet. Right-click a player to invite them.", new Vector2(x, y), Theme.InkSoft);
        foreach (var m in members.Take(6))
        {
            var dot = !m.Online ? Theme.InkSoft : m.Status?.InDairy == true ? Theme.SageDeep : Theme.HoneyDeep;
            dl.AddCircleFilled(new Vector2(x + 4, y + 9), 3.5f, Ui.U(dot));
            Ui.Text(dl, fonts.Small, $"{m.Name.Split(' ')[0]} · {m.Role}", new Vector2(x + 14, y), m.Online ? Theme.Ink : Theme.InkSoft);
            if (m.Role == "cow" && m.Online && m.Status is { } s)
            {
                if (s.Spoiled) Ui.Text(dl, fonts.Small, "spoiled", new Vector2(right, y), Theme.Warn, 1f);
                else
                {
                    var bx0 = right - 110;
                    Ui.Bar(dl, new Vector2(bx0, y + 7), new Vector2(right - 34, y + 12), s.Cap > 0 ? s.Milk / s.Cap : 0, s.Cap > 0 && s.Milk >= s.Cap - 0.1f ? Theme.RoseDeep : Theme.BlueDeep);
                    Ui.Text(dl, fonts.Small, Grades.Name(Math.Clamp(s.Grade, 0, Grades.Count - 1)), new Vector2(right, y), Theme.GradeColor(Math.Clamp(s.Grade, 0, Grades.Count - 1)), 1f);
                }
            }

            y += 22;
        }

        if (members.Count > 6) Ui.Text(dl, fonts.Small, $"+{members.Count - 6} more", new Vector2(right, y - 22), Theme.InkSoft, 1f);

        var extra = (mine.Invites.Count > 0 ? $"{mine.Invites.Count} invite(s) waiting" : "") +
                    (mine.Invites.Count > 0 && G.Net.MyOrders.Count > 0 ? " · " : "") +
                    (G.Net.MyOrders.Count > 0 ? $"{G.Net.MyOrders.Count} buy order(s) open" : "");
        if (extra.Length > 0) Ui.Text(dl, fonts.Small, extra, new Vector2(x, o.Y + size.Y - 34), Theme.InkSoft);
        dl.PopClipRect();

        if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() &&
            (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)))
            plugin.MainWindow.OpenTab(3);
    }
}
