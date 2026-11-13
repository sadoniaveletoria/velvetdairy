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
        Size = new Vector2(860, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(480, 300), MaximumSize = new Vector2(1400, 1200) };
    }

    public override bool DrawConditions() => G.Networked && plugin.Config.Farm.NetMember != null;

    private int pushedColors;

    /// <summary>Opening it brings your dairy's info up to date (it's checked less often while nothing shows it).</summary>
    public override void OnOpen()
    {
        plugin.Net.MineWithin(1);
        if (plugin.Net.Mine?.Applying?.CanAnswer == true) plugin.Net.RefreshApplications(true);
    }

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
        // Clicking in Eorzea Milkers's own windows counts as being awake (mouse movement alone doesn't)
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

        DrawApplications(mine);

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
        if (ImGui.BeginTable("##members", 8, flags, new Vector2(0, tableH)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.DefaultSort, 2.2f);
            ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableSetupColumn("Levels", ImGuiTableColumnFlags.None, 1.6f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.None, 1.4f);
            ImGui.TableSetupColumn("Milk", ImGuiTableColumnFlags.None, 1.6f);
            ImGui.TableSetupColumn("Quality", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableSetupColumn("Given", ImGuiTableColumnFlags.None, 1f);
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
                2 => m => TopLevel(m),
                3 => m => m.Online ? (m.Status?.InDairy == true ? long.MinValue : long.MinValue + 1) : -m.LastSeen,
                4 => m => m.Has("cow") && m.Status is { Cap: > 0 } s ? s.Milk / s.Cap : -1f,
                5 => m => m.Has("cow") ? m.Status?.Quality ?? 0 : -1,
                6 => m => m.Status?.TotalOz ?? 0,
                _ => m => m.Name,
            };
            var rows = (asc ? shown.OrderBy(key) : shown.OrderByDescending(key)).ThenBy(m => m.Name).ToList();
            var meId = mine.You?.Id;

            foreach (var m in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(Theme.Ink, m.Name + (m.Id == meId ? " (you)" : ""));
                // Cow level 75: a star; the Velvet crown (or farmer level 100): a crown
                var badge = m.Status?.Crown == true ? 2 : (m.Status?.Level ?? 0) >= 75 ? 1 : 0;
                if (badge > 0)
                {
                    ImGui.SameLine(0, 4);
                    var at = ImGui.GetCursorScreenPos() + new Vector2(0, 2);
                    var h = ImGui.GetTextLineHeight() - 4;
                    if (badge == 2) Ui.Crown(ImGui.GetWindowDrawList(), at, h);
                    else Ui.Star(ImGui.GetWindowDrawList(), at + new Vector2(h / 2, h / 2), h / 2);
                    ImGui.Dummy(new Vector2(h * 1.3f, h));
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(badge == 2 ? "Velvet crown" : "Cow level 75+");
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(m.World);

                ImGui.TableNextColumn();
                ImGui.TextColored(RoleColor(m.AllRoles.Count == 1 ? m.AllRoles[0] : ""), (m.IsOwner ? "Owner · " : "") + m.RoleText);

                ImGui.TableNextColumn();
                DrawLevels(m);

                ImGui.TableNextColumn();
                if (Outdated(m) && Reports(m))
                {
                    ImGui.TextColored(Theme.Warn, "Old version");
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip($"They're on {m.Version}; the newest is {Plugin.Version}.");
                    ImGui.SameLine();
                }

                if (!m.Online)
                {
                    ImGui.TextColored(Theme.InkSoft, m.LastSeen > 0 ? $"Seen {Ago(m.LastSeen)}" : "Offline");
                    if (m.LastSeen > 0 && ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Last online {DateTimeOffset.FromUnixTimeSeconds(m.LastSeen).ToLocalTime():MMM d, h:mm tt}");
                }
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
                if (m.Has("cow") && !Reports(m)) ImGui.TextColored(Theme.InkSoft, "-");
                else if (m.Has("cow") && m.Status is { } q)
                {
                    var cap = Grades.Cap(q.Quality);
                    ImGui.TextColored(Theme.GradeColor(cap), $"{q.Quality:N0}");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Milk quality {q.Quality:N0}: up to Grade {Grades.Name(cap)}" +
                                         (cap + 1 < Grades.QualityNeed.Length ? $" (Grade {Grades.Name(cap + 1)} at {Grades.QualityNeed[cap + 1]:N0})" : ""));
                }
                else ImGui.TextColored(Theme.InkSoft, "-");

                ImGui.TableNextColumn();
                if (Reports(m) && m.Status is { } t) ImGui.TextColored(t.TotalOz > 0 ? Theme.Ink : Theme.InkSoft, $"{t.TotalOz:N0} oz");
                else ImGui.TextColored(Theme.InkSoft, "-");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("All the milk they've ever given (stalls and hand milking).");

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

        if (shown.Count == 0) ImGui.TextColored(Theme.InkSoft, members.Count <= 1 ? "Just you so far. Invite people with right-click > Eorzea Milkers." : "Nobody matches.");

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

    /// <summary>Their level in each role they hold, e.g. "Cow 12 · Farmer 4". Levels come from their plugin (last time it checked in).</summary>
    /// <summary>The first version whose plugin reports levels, quality and milk given.</summary>
    private static readonly Version DetailsSince = new(0, 9, 6, 0);

    private static Version? VersionOf(MemberInfo m) => Version.TryParse(m.Version, out var v) ? v : null;

    /// <summary>Their plugin reports the details shown here (levels, quality, milk given).</summary>
    private static bool Reports(MemberInfo m) => m.Status != null && VersionOf(m) is { } v && v >= DetailsSince;

    private static bool Outdated(MemberInfo m) =>
        VersionOf(m) is { } v && Version.TryParse(Plugin.Version, out var mine) && v < mine;

    private static void NeedsUpdate(MemberInfo m)
    {
        ImGui.TextColored(Theme.Warn, "Needs update");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(m.Version.Length > 0 ? $"They're on {m.Version}. Their levels, quality and milk given show once they update (newest: {Plugin.Version})." : "Shows once they've played with the newest version.");
    }

    private static void DrawLevels(MemberInfo m)
    {
        var st = m.Status;
        if (!Reports(m) || st == null)
        {
            NeedsUpdate(m);
            return;
        }

        var first = true;
        void Part(string role, string label, int level)
        {
            if (!m.Has(role) || level <= 0) return;
            if (!first) ImGui.SameLine(0, 4);
            ImGui.TextColored(RoleColor(role), $"{(first ? "" : "· ")}{label} {level}");
            first = false;
        }

        Part("cow", "Cow", st.Level);
        Part("farmer", "Farmer", st.FarmerLevel);
        Part("bull", "Bull", st.BullLevel);
        if (first) ImGui.TextColored(Theme.InkSoft, "-");
    }

    // ----- Applications: answer them (owner, or farmers if allowed), and what the dairy is looking for (owner) -----
    private readonly System.Collections.Generic.Dictionary<long, System.Collections.Generic.HashSet<string>> unticked = new();

    private void DrawApplications(MyDairyReply mine)
    {
        var a = mine.Applying;
        var isOwner = mine.You?.IsOwner == true;
        if (a == null || (!a.CanAnswer && !isOwner)) return;
        var net = G.Net;
        if (a.CanAnswer) net.RefreshApplications();
        var apps = a.CanAnswer ? net.Applications : new System.Collections.Generic.List<ApplicationInfo>();

        ImGui.Spacing();
        var head = apps.Count > 0 ? $"Applications ({apps.Count})###apps" : a.LookingFor.Count > 0 ? "Applications###apps" : "Applications (not taking any)###apps";
        if (apps.Count > 0) ImGui.SetNextItemOpen(true, ImGuiCond.Once);
        if (!ImGui.CollapsingHeader(head)) return;

        if (isOwner)
        {
            ImGui.TextColored(Theme.Ink, "Looking for:");
            var looking = a.LookingFor.ToList();
            var changed = false;
            foreach (var (id, name) in new[] { ("cow", "Cows"), ("farmer", "Farmers"), ("bull", "Bulls") })
            {
                ImGui.SameLine();
                var on = looking.Contains(id);
                if (ImGui.Checkbox($"{name}##look{id}", ref on))
                {
                    if (on) looking.Add(id);
                    else looking.Remove(id);
                    changed = true;
                }
            }

            var farmers = a.FarmersAnswer;
            ImGui.SameLine();
            if (ImGui.Checkbox("Farmers can answer applications##farmapps", ref farmers)) changed = true;
            if (changed)
            {
                a.LookingFor = looking;   // show it right away; the next refresh confirms it
                a.FarmersAnswer = farmers;
                net.SetApplying(looking, farmers, e => G.Notify(e, 2));
            }

            ImGui.TextColored(Theme.InkSoft, looking.Count == 0
                ? "Not taking applications. Tick a role and players without a dairy can apply from the Dairies tab."
                : "Players without a dairy can apply from the Dairies tab with these roles.");
        }

        if (!a.CanAnswer) return;
        if (apps.Count == 0)
        {
            ImGui.TextColored(Theme.InkSoft, "No applications waiting.");
            return;
        }

        ImGui.TextColored(Theme.InkSoft, "Untick a role to accept them without it. Accept adds them right away; Deny tells them \"not this time\" (they can apply again in 3 days).");
        foreach (var ap in apps.ToList())
        {
            ImGui.PushID($"app{ap.Id}");
            var off = unticked.TryGetValue(ap.Id, out var u) ? u : unticked[ap.Id] = new();
            ImGui.TextColored(Theme.Ink, ap.Name);
            ImGui.SameLine();
            ImGui.TextColored(Theme.InkSoft, $"{ap.World} · {ap.Levels} · {Ago(ap.Sent)}");
            foreach (var r in ap.Roles)
            {
                var on = !off.Contains(r);
                if (ImGui.Checkbox($"{Cap1(r)}##r{r}", ref on))
                {
                    if (on) off.Remove(r);
                    else off.Add(r);
                }

                ImGui.SameLine();
            }

            var accept = ap.Roles.Where(r => !off.Contains(r)).ToList();
            ImGui.BeginDisabled(accept.Count == 0);
            if (ImGui.SmallButton("Accept")) net.AcceptApplication(ap.Id, accept, e => G.Notify(e, 2));
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.SmallButton("Deny")) net.DenyApplication(ap.Id, e => G.Notify(e, 2));
            if (ap.Note.Length > 0)
            {
                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                ImGui.TextColored(Theme.HoneyDeep, $"\"{ap.Note}\"");
                ImGui.PopTextWrapPos();
            }

            ImGui.Separator();
            ImGui.PopID();
        }
    }

    private static string Ago(long unix)
    {
        var d = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        if (d.TotalMinutes < 60) return $"{Math.Max(1, (int)d.TotalMinutes)}m ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
        if (d.TotalDays < 30) return $"{(int)d.TotalDays}d ago";
        return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("MMM d");
    }

    private static int TopLevel(MemberInfo m)
    {
        var st = m.Status;
        if (st == null) return 0;
        return Math.Max(m.Has("cow") ? st.Level : 0, Math.Max(m.Has("farmer") ? st.FarmerLevel : 0, m.Has("bull") ? st.BullLevel : 0));
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
