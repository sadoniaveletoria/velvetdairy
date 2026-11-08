using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;
using VelvetDairy.Network;

namespace VelvetDairy.Windows;

/// <summary>
/// Pop-out list of everyone in your dairy, built for big dairies: search, filter by role or online,
/// sort by any column, and a menu on each member for role changes and removal (owners and farmers only).
/// </summary>
public sealed class MembersWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private string search = "";
    private int roleFilter;          // 0 all, 1 cows, 2 farmers, 3 bulls
    private bool onlineOnly;
    private string? menuFor;

    private static readonly string[] Filters = { "Everyone", "Cows", "Farmers", "Bulls" };
    private static readonly string?[] FilterRoles = { null, "cow", "farmer", "bull" };

    private DairyGame G => plugin.Game;

    public MembersWindow(Plugin plugin, Fonts fonts) : base("Dairy members##VelvetDairyMembers", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        Size = new Vector2(620, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(480, 300), MaximumSize = new Vector2(1400, 1200) };
    }

    public override bool DrawConditions() => G.Networked && plugin.Config.Farm.NetMember != null;

    private int pushedColors;

    public override void PreDraw()
    {
        void C(ImGuiCol c, Vector4 v)
        {
            ImGui.PushStyleColor(c, v);
            pushedColors++;
        }

        pushedColors = 0;
        C(ImGuiCol.WindowBg, Theme.Paper);
        C(ImGuiCol.TitleBg, Theme.Rose);
        C(ImGuiCol.TitleBgActive, Theme.RoseDeep);
        C(ImGuiCol.Text, Theme.Ink);
        C(ImGuiCol.FrameBg, Theme.Linen);
        C(ImGuiCol.FrameBgHovered, Theme.Cream);
        C(ImGuiCol.Button, Theme.Lighten(Theme.Rose, 0.3f));
        C(ImGuiCol.ButtonHovered, Theme.Rose);
        C(ImGuiCol.ButtonActive, Theme.RoseDeep);
        C(ImGuiCol.Header, Theme.Lighten(Theme.Blue, 0.4f));
        C(ImGuiCol.HeaderHovered, Theme.Blue);
        C(ImGuiCol.TableHeaderBg, Theme.Cream);
        C(ImGuiCol.TableRowBgAlt, Theme.Alpha(Theme.Linen, 0.5f));
        C(ImGuiCol.TableBorderLight, Theme.Alpha(Theme.RoseDeep, 0.15f));
        C(ImGuiCol.TableBorderStrong, Theme.Alpha(Theme.RoseDeep, 0.3f));
        C(ImGuiCol.PopupBg, Theme.Paper);
        C(ImGuiCol.Border, Theme.Alpha(Theme.RoseDeep, 0.35f));
        C(ImGuiCol.CheckMark, Theme.RoseDeep);
        C(ImGuiCol.ScrollbarBg, Theme.Cream);
        C(ImGuiCol.ScrollbarGrab, Theme.Rose);
    }

    public override void PostDraw() => ImGui.PopStyleColor(pushedColors);

    private static string Cap1(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static Vector4 RoleColor(string role) => role switch
    {
        "farmer" => Theme.SageDeep,
        "bull" => Theme.HoneyDeep,
        "cow" => Theme.RoseDeep,
        _ => Theme.InkSoft,
    };

    public override void Draw()
    {
        // Clicking in Velvet Dairy's own windows counts as being awake (mouse movement alone doesn't)
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) G.Touch();

        var mine = G.Net.Mine;
        if (mine == null)
        {
            ImGui.TextColored(Theme.InkSoft, "Loading...");
            return;
        }

        var members = mine.Members;
        var online = members.Count(m => m.Online);
        var here = members.Count(m => m.Online && m.Status?.InDairy == true);
        using (fonts.For(fonts.Heading, mine.Dairy?.Name ?? "")!.Push()) ImGui.TextColored(Theme.Ink, mine.Dairy?.Name ?? G.DairyName);
        ImGui.TextColored(Theme.InkSoft,
            $"{members.Count} members · {online} online · {here} in the dairy · " +
            $"{members.Count(m => m.Has("cow"))} cows, {members.Count(m => m.Has("farmer"))} farmers, {members.Count(m => m.Has("bull"))} bulls");

        // Invite your target
        var roles = G.InvitableRoles;
        if (roles.Length > 0)
        {
            var t = Plugin.TargetManager.Target as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
            var me = Plugin.ObjectTable.LocalPlayer;
            var ok = t != null && me != null && t.GameObjectId != me.GameObjectId && G.MemberNamed(t.Name.TextValue, t.HomeWorld.ValueNullable?.Name.ExtractText() ?? "") == null;
            ImGui.TextColored(Theme.Ink, ok ? $"Invite {t!.Name.TextValue}:" : "Invite: target a player who isn't a member yet.");
            foreach (var r in roles)
            {
                ImGui.SameLine();
                ImGui.BeginDisabled(!ok);
                if (ImGui.SmallButton($"as {r}##inv{r}") && t != null) G.InvitePlayer(t.Name.TextValue, t.HomeWorld.RowId, r);
                ImGui.EndDisabled();
            }
        }

        // Filters
        ImGui.Spacing();
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##membersearch", "Search names or worlds", ref search, 40);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.Combo("##rolefilter", ref roleFilter, Filters, Filters.Length);
        ImGui.SameLine();
        ImGui.Checkbox("Online only", ref onlineOnly);

        var f = search.Trim();
        var role = FilterRoles[roleFilter];
        var shown = members.Where(m => (role == null || m.Has(role)) && (!onlineOnly || m.Online) &&
                                       (f.Length == 0 || m.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || m.World.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Sent invites take a little room at the bottom
        var invitesH = mine.Invites.Count > 0 ? Math.Min(4, mine.Invites.Count) * ImGui.GetTextLineHeightWithSpacing() + 34 : 0;
        var tableH = Math.Max(120, ImGui.GetContentRegionAvail().Y - invitesH);
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable |
                                      ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("##members", 5, flags, new Vector2(0, tableH)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.DefaultSort, 2.2f);
            ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.None, 1.4f);
            ImGui.TableSetupColumn("Milk", ImGuiTableColumnFlags.None, 1.6f);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, 34);
            ImGui.TableHeadersRow();

            var specs = ImGui.TableGetSortSpecs();
            var col = 0;
            var asc = true;
            if (specs.SpecsCount > 0)
            {
                col = specs.Specs.ColumnIndex;
                asc = specs.Specs.SortDirection != ImGuiSortDirection.Descending;
            }

            Func<MemberInfo, IComparable> key = col switch
            {
                1 => m => m.RoleText,
                2 => m => (m.Online ? 0 : 2) - (m.Status?.InDairy == true ? 1 : 0),
                3 => m => m.Has("cow") && m.Status is { Cap: > 0 } s ? s.Milk / s.Cap : -1f,
                _ => m => m.Name,
            };
            var rows = (asc ? shown.OrderBy(key) : shown.OrderByDescending(key)).ThenBy(m => m.Name).ToList();
            var meId = mine.You?.Id;

            foreach (var m in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(Theme.Ink, m.Name + (m.Id == meId ? " (you)" : ""));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(m.World);

                ImGui.TableNextColumn();
                ImGui.TextColored(RoleColor(m.AllRoles.Count == 1 ? m.AllRoles[0] : ""), (m.IsOwner ? "Owner · " : "") + m.RoleText);

                ImGui.TableNextColumn();
                if (!m.Online) ImGui.TextColored(Theme.InkSoft, "Offline");
                else if (m.Status?.InDairy == true) ImGui.TextColored(Theme.SageDeep, "In the dairy");
                else ImGui.TextColored(Theme.HoneyDeep, "Online");

                ImGui.TableNextColumn();
                if (m.Has("cow") && m.Online && m.Status is { } st)
                {
                    if (st.Spoiled) ImGui.TextColored(Theme.Warn, "Spoiled");
                    else
                    {
                        var g = Math.Clamp(st.Grade, 0, Grades.Count - 1);
                        var full = st.Cap > 0 && st.Milk >= st.Cap - 0.1f;
                        ImGui.TextColored(full ? Theme.RoseDeep : Theme.Ink, $"{st.Milk:0.#}/{st.Cap:0} oz");
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.GradeColor(g), Grades.Name(g));
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Grade {Grades.Name(g)} · level {st.Level}");
                    }
                }
                else ImGui.TextColored(Theme.InkSoft, "-");

                ImGui.TableNextColumn();
                var canAct = G.IsOwner || G.IsFarmer || G.CanRemove(m);
                if (canAct && ImGui.SmallButton($"...##act{m.Id}"))
                {
                    menuFor = m.Id;
                    ImGui.OpenPopup("##memberMenu");
                }
            }

            DrawMenu(members);
            ImGui.EndTable();
        }

        if (shown.Count == 0) ImGui.TextColored(Theme.InkSoft, members.Count <= 1 ? "Just you so far. Invite people with right-click > Velvet Dairy." : "Nobody matches.");

        if (mine.Invites.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.InkSoft, $"Invites waiting ({mine.Invites.Count})");
            foreach (var inv in mine.Invites.ToList())
            {
                ImGui.TextColored(Theme.Ink, $"{inv.Name} · as {inv.Role}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Cancel##cinv{inv.Id}")) G.Net.CancelInvite(inv.Id, e => G.Notify(e, 2));
            }
        }
    }

    /// <summary>The per-member menu: role changes (owner) and removal (owner, or farmer for cows and bulls).</summary>
    private void DrawMenu(System.Collections.Generic.List<MemberInfo> members)
    {
        if (!ImGui.BeginPopup("##memberMenu")) return;
        var m = members.FirstOrDefault(x => x.Id == menuFor);
        if (m == null)
        {
            ImGui.EndPopup();
            return;
        }

        ImGui.TextColored(Theme.InkSoft, m.Name);
        ImGui.Separator();
        // Give roles (owner: any; farmer: any to others), take them away (owner only)
        foreach (var r in VelvetDairy.Game.Roles.All)
        {
            if (!m.Has(r))
            {
                var p = G.AddRoleProblem(m, r);
                if (p == null && ImGui.MenuItem($"Give the {r} role")) G.AddRole(m, r);
            }
            else if (G.IsOwner && ImGui.MenuItem($"Take the {r} role away")) G.RemoveRole(m, r);
        }

        if (G.CanRemove(m))
        {
            ImGui.Separator();
            var shift = ImGui.GetIO().KeyShift;
            if (ImGui.MenuItem(shift ? "Remove from dairy" : "Remove (hold Shift)", "", false, shift)) G.RemoveMember(m);
        }

        ImGui.EndPopup();
    }
}
