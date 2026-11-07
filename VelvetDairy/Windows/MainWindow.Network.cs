using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using VelvetDairy.Game;
using VelvetDairy.Network;

namespace VelvetDairy.Windows;

// The Velvet Dairy Network screens: the Dairies directory, your dairy's roles and members, and buy orders.
public sealed partial class MainWindow
{
    private string listDescription = "";
    private bool listLoaded;
    private int orderOz = 16;
    private int orderGrade;
    private int orderCoins = -1;
    private string dirFilter = "";
    private bool dirPublicOnly;
    private int donateCoins = 100;

    private static readonly string[] OwnerRoles = { "cow", "farmer", "bull", "none" };
    private static readonly string[] RoleLabels = { "Cow (gets milked)", "Farmer (milks the cows)", "Bull", "No role (just run it)" };

    private static string Cap1(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static Vector4 RoleColor(string role) => role switch
    {
        "farmer" => Theme.SageDeep,
        "bull" => Theme.HoneyDeep,
        "cow" => Theme.RoseDeep,
        _ => Theme.InkSoft,
    };

    // =====================================================================
    // Dairies tab: who's online and the directory of dairies
    // =====================================================================

    private void DrawNetwork(Vector2 min, Vector2 max)
    {
        G.Net.WantDirectory = true;
        var split = min.X + (max.X - min.X) * 0.62f;
        Panel("##directory", min, new Vector2(split - 7, max.Y), "Dairies on the network", DrawDirectory);
        Panel("##netstatus", new Vector2(split + 7, min.Y), max, "Network", DrawNetStatus, true);
    }

    private void DrawNetStatus(float width)
    {
        var net = G.Net;
        if (!F.NetEnabled)
        {
            Ui.Wrapped("You've turned the network off in Settings. Your dairy works locally on its own.", Theme.InkSoft, width);
            return;
        }

        if (net.Api == null)
        {
            Ui.Wrapped("The network isn't available right now, so Velvet Dairy is running on its own. Everything local still works.", Theme.InkSoft, width);
            ImGui.TextColored(Theme.InkSoft, net.Status);
            return;
        }

        var online = net.Last?.Online ?? net.Directory?.Online ?? 0;
        using (fonts.For(fonts.Heading, $"{online}")!.Push()) ImGui.TextColored(Theme.SageDeep, $"{online:N0} online");
        ImGui.TextColored(Theme.InkSoft, "Players with Velvet Dairy running in the last few minutes.");
        ImGui.TextColored(net.Connected ? Theme.SageDeep : Theme.Warn, net.Status);

        SubHeading("You", width);
        var m = F.NetMember;
        if (m == null) Ui.Wrapped("You aren't in a dairy. Register your own estate on the Dairy tab, or accept an invite.", Theme.Ink, width);
        else
        {
            ImGui.TextColored(Theme.Ink, m.Dairy);
            ImGui.TextColored(RoleColor(m.Role), m.IsOwner ? $"Owner · plays as {(m.Role == "none" ? "no role" : m.Role)}" : Cap1(m.Role));
        }

        var visiting = G.VisitingDairy;
        if (visiting != null)
        {
            SubHeading("You're visiting", width);
            ImGui.TextColored(Theme.Ink, visiting.Name);
            ImGui.TextColored(Theme.InkSoft, "Deliver milk here from the Creamery tab.");
        }

        // This week's most active dairies
        var board = net.Leaderboard;
        if (board != null)
        {
            SubHeading("This week's top dairies", width);
            if (board.Dairies.Count == 0) Ui.Wrapped("No dairy has earned XP this week yet.", Theme.InkSoft, width);
            foreach (var r in board.Dairies.Take(10))
            {
                var col = r.Mine ? Theme.RoseDeep : r.Rank <= 3 ? Theme.HoneyDeep : Theme.Ink;
                ImGui.TextColored(col, $"{r.Rank}.");
                ImGui.SameLine(28);
                ImGui.TextColored(col, $"{r.Name}{(r.Mine ? " (yours)" : "")}");
                ImGui.SameLine();
                ImGui.TextColored(Theme.InkSoft, $"Lv {r.Level} · {r.WeekXp:N0} xp");
            }

            var reset = NextMonday() - DateTime.UtcNow;
            ImGui.TextColored(Theme.InkSoft, $"Resets in {(int)reset.TotalDays}d {reset.Hours}h (Monday 00:00 UTC).");
        }

        SubHeading("How it works", width);
        Ui.Wrapped("Owners register their own estate as a dairy and invite farmers, cows and bulls. Farmers can invite cows and bulls. " +
                   "Public dairies show their address here so anyone can visit; private ones only show their name. " +
                   "Cows deliver milk in person: go to a public dairy and fill its owner's buy orders or the creamery's visitor orders. " +
                   "Dairies level up from what their members do inside them; higher levels get more stalls and better stall tiers and list first.", Theme.InkSoft, width);
        if (!Lifestream.Installed)
        {
            Gap(6);
            Ui.Wrapped("Tip: install the Lifestream plugin and Visit takes you straight there. Without it, Visit copies the address.", Theme.BlueDeep, width);
        }
    }

    private static DateTime NextMonday()
    {
        var d = DateTime.UtcNow.Date;
        var days = ((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7;
        return d.AddDays(days == 0 ? 7 : days);
    }

    private void DrawDirectory(float width)
    {
        var net = G.Net;
        if (net.Api == null || !F.NetEnabled)
        {
            Ui.Wrapped("The directory needs the Velvet Dairy Network.", Theme.InkSoft, width);
            return;
        }

        ImGui.SetNextItemWidth(Math.Min(260, width - 200));
        ImGui.InputTextWithHint("##dirfilter", "Search by name, world or district", ref dirFilter, 40);
        ImGui.SameLine();
        ImGui.Checkbox("Public only", ref dirPublicOnly);
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh")) net.RefreshDirectory();
        Gap(4);

        var list = net.Directory?.Dairies;
        if (list == null)
        {
            ImGui.TextColored(Theme.InkSoft, "Loading...");
            return;
        }

        var f = dirFilter.Trim();
        var shown = list.Where(d => (!dirPublicOnly || d.IsPublic) &&
                                    (f.Length == 0 || d.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || d.WorldName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                                     d.District.Contains(f, StringComparison.OrdinalIgnoreCase) || d.Owner.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(d => d.Level).ThenByDescending(d => d.CowsHere).ThenByDescending(d => d.IsPublic).ThenBy(d => d.Name).ToList();
        if (shown.Count == 0) Ui.Wrapped(list.Count == 0 ? "No dairies yet. Be the first: register your estate on the Dairy tab." : "Nothing matches.", Theme.InkSoft, width);

        foreach (var d in shown)
        {
            var lines = new List<Line>
            {
                new(d.IsPublic || d.Mine ? $"{d.Address} · {d.WorldName}{(d.DcName.Length > 0 ? $" ({d.DcName})" : "")}" : $"Private · {d.WorldName}", Theme.InkSoft),
                new($"{d.Members} member{(d.Members == 1 ? "" : "s")}{(d.CowsHere > 0 ? $" · {d.CowsHere} there now" : "")}{(d.OpenOrders > 0 ? $" · {d.OpenOrders} buy order{(d.OpenOrders == 1 ? "" : "s")}" : "")}",
                    d.CowsHere > 0 ? Theme.SageDeep : Theme.InkSoft),
            };
            if (d.Owner.Length > 0) lines.Add(new Line($"Owner: {d.Owner}", Theme.InkSoft));
            if (d.Description.Length > 0) lines.Add(new Line(d.Description, Theme.Ink));

            var canVisit = d.HouseId != null && d.World != null && d.Ward != null;
            var buttons = canVisit
                ? new[]
                {
                    new Btn($"##copy{d.Id}", "Copy", "cream", true, "Copy the address."),
                    new Btn($"##visit{d.Id}", "Visit", "blue", !Lifestream.Busy, Lifestream.Installed ? "Travel there with Lifestream." : "Copies the address (install Lifestream to travel automatically)."),
                }
                : Array.Empty<Btn>();
            var title = $"Lv {d.Level}  {d.Name}" + (d.Mine ? "  ·  yours" : d.IsPublic ? "" : "  ·  private");
            var clicked = ListRow(Ico(d.IsPublic ? "milk" : "ribbon", d.IsPublic || d.Mine ? 1f : 0.6f), title, d.Mine ? Theme.RoseDeep : Theme.Ink, lines, buttons, width);
            if (clicked == 0) Copy(d);
            else if (clicked == 1) Visit(d);
        }
    }

    private void Copy(DairyListing d)
    {
        ImGui.SetClipboardText($"{d.WorldName} {d.Address}");
        G.Notify($"Copied: {d.WorldName}, {d.Address}", 0);
    }

    private void Visit(DairyListing d)
    {
        if (!Lifestream.Installed || d.HouseId == null || !ulong.TryParse(d.HouseId, out var hid))
        {
            Copy(d);
            return;
        }

        var addr = Estate.FromId(hid);
        var err = Lifestream.GoTo(d.Name, d.World!.Value, d.DistrictKind, d.Ward!.Value, d.Plot ?? addr.Plot, d.Room ?? 0, addr.Apartment);
        if (err != null)
        {
            G.Notify(err, 2);
            Copy(d);
        }
        else G.Notify($"Lifestream is taking you to {d.Name}.", 0);
    }

    // =====================================================================
    // Dairy tab on the network: your dairy, its stalls and its people
    // =====================================================================

    private void DrawNetDairy(Vector2 min, Vector2 max)
    {
        var mid = (min.X + max.X) / 2;
        var splitY = min.Y + (max.Y - min.Y) * 0.55f;
        Panel("##netplace", min, new Vector2(mid - 7, splitY - 7), "Your dairy", DrawNetPlace);
        Panel("##stalls", new Vector2(min.X, splitY + 7), new Vector2(mid - 7, max.Y), "Stalls", DrawStalls);
        Panel("##members", new Vector2(mid + 7, min.Y), max, "Members", DrawMembers, true);
    }

    private void DrawNetPlace(float width)
    {
        var m = F.NetMember;
        var mine = G.Net.Mine?.Dairy;

        // Invites waiting for you
        var invites = G.Net.Last?.Invites ?? new List<InviteInfo>();
        foreach (var inv in invites.ToList())
        {
            var warn = m == null ? "" : m.IsOwner ? "Owners can't join another dairy. Close yours first." : $"You'd leave {m.Dairy}.";
            var clicked = ListRow(Ico("ribbon"), $"Invite: {inv.Dairy}", Theme.RoseDeep,
                new[] { new Line($"Join as a {inv.Role} · {inv.World}", RoleColor(inv.Role)), new Line(warn, Theme.InkSoft) },
                new[] { new Btn($"##decl{inv.Id}", "Decline", "cream"), new Btn($"##acc{inv.Id}", "Accept", "sage", m?.IsOwner != true, m?.IsOwner == true ? warn : null) }, width);
            if (clicked == 0) G.DeclineInvite(inv);
            else if (clicked == 1) G.AcceptInvite(inv);
        }

        if (m == null)
        {
            Ui.Wrapped("You aren't in a dairy yet. Go inside your own estate (your house, your FC's house, your apartment or a shared estate) and register it, " +
                       "or wait for an owner or farmer to invite you.", Theme.Ink, width);
            Gap(6);
            DrawRegisterForm(width, false);
            return;
        }

        ImGui.TextColored(Theme.Ink, m.Dairy);
        if (mine != null)
            ImGui.TextColored(Theme.InkSoft, $"{mine.District}, Ward {mine.Ward}, {(mine.Room > 0 && Estate.FromId(ulong.TryParse(mine.HouseId, out var h) ? h : 0).Apartment ? $"apartment {mine.Room}" : $"Plot {mine.Plot}")} · {mine.WorldName}" +
                                             $" · {(mine.IsPublic ? "public" : "private")}");
        ImGui.TextColored(RoleColor(m.Role), m.IsOwner ? $"You own it · you play as {(m.Role == "none" ? "no role" : m.Role)}" : $"You're a {m.Role} here");
        ImGui.TextColored(G.Where.InDairy ? Theme.SageDeep : Theme.RoseDeep, G.Where.Why);
        Gap(6);
        DrawDairyLevel(width);

        if (m.IsOwner)
        {
            DrawRegisterForm(width, true);
            Gap(4);
            if (Ui.Button("##closedairy", "Close dairy", new Vector2(150, 30), "cream", ImGui.GetIO().KeyShift,
                    "Hold Shift and click. Removes your dairy from the network; members are told and its stalls and orders go away."))
                G.CloseDairy();
        }
        else
        {
            Ui.Wrapped(m.Role switch
            {
                "cow" => "Get milked in this dairy's stalls or by its farmers. Care and resting work while you're inside.",
                "farmer" => "You hand milk this dairy's cows: stand close, target them and use a milking emote. You can invite cows and bulls (right-click a player).",
                "bull" => "Bulls are a role for now. More to come.",
                _ => "",
            }, Theme.InkSoft, width);
            Gap(4);
            if (Ui.Button("##leavedairy", "Leave dairy", new Vector2(150, 30), "cream", ImGui.GetIO().KeyShift, "Hold Shift and click to leave."))
                G.LeaveDairy();
        }
    }

    /// <summary>The dairy's level, XP to the next one, and the shared fund members can donate to.</summary>
    private void DrawDairyLevel(float width)
    {
        var p = G.Net.Progress;
        if (p == null) return;
        SubHeading($"Dairy level {p.Level}", width);
        var cur = ImGui.GetCursorScreenPos();
        var barW = Math.Min(width, 360);
        Ui.Bar(ImGui.GetWindowDrawList(), cur + new Vector2(0, 4), cur + new Vector2(barW, 12), p.LevelProgress, Theme.HoneyDeep);
        ImGui.Dummy(new Vector2(barW, 16));
        ImGui.TextColored(Theme.InkSoft, p.NextXp is { } n ? $"{p.Xp:N0} / {n:N0} xp · {p.WeekXp:N0} this week" : $"Max level · {p.WeekXp:N0} xp this week");
        ImGui.TextColored(Theme.InkSoft, $"{p.StallSlots} stall slots · best stall: {Catalog.StallTierNames[Math.Clamp(p.MaxTier, 1, 4)]}");
        Ui.Wrapped("Members earn XP for the dairy by milking and caring inside it (up to 300 each a day) and when cows fill its buy orders.", Theme.InkSoft, width);

        Gap(4);
        ImGui.TextColored(Theme.HoneyDeep, $"Dairy fund: {p.Fund:N0} coins");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Anyone in the dairy can add coins. The owner spends them on new stalls and stall upgrades.");
        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("##donate", ref donateCoins, 50, 250);
        donateCoins = Math.Clamp(donateCoins, 1, 100000);
        ImGui.SameLine();
        var problem = G.DonateProblem(donateCoins);
        if (Ui.Button("##donatebtn", "Donate", new Vector2(110, 30), "honey", problem == null, problem ?? $"Put {donateCoins} of your coins into the fund."))
            G.Donate(donateCoins);
        if (p.Donors.Count > 0)
            Ui.Wrapped("Top donors: " + string.Join(", ", p.Donors.Take(5).Select(d => $"{d.Name.Split(' ')[0]} {d.Coins:N0}")), Theme.InkSoft, width);
        Gap(6);
    }

    /// <summary>Register (or, for the owner, update) the dairy and how it's listed.</summary>
    private void DrawRegisterForm(float width, bool owner)
    {
        if (!listLoaded)
        {
            listDescription = F.ListDescription;
            if (owner && F.NetMember != null) dairyName = F.NetMember.Dairy;
            listLoaded = true;
        }

        var changed = false;
        ImGui.SetNextItemWidth(Math.Min(260, width - 10));
        ImGui.InputTextWithHint("##netdairyname", "Dairy name", ref dairyName, 40);
        ImGui.SetNextItemWidth(Math.Min(360, width - 10));
        if (ImGui.InputTextWithHint("##netdesc", "Short description (optional)", ref listDescription, 120)) { F.ListDescription = listDescription; changed = true; }

        var b = F.ListPublic;
        if (ImGui.Checkbox("Public: show the address so anyone can visit and deliver", ref b)) { F.ListPublic = b; changed = true; }
        b = F.ListShowName;
        if (ImGui.Checkbox("Show my character name as the owner", ref b)) { F.ListShowName = b; changed = true; }

        var ri = Math.Max(0, Array.IndexOf(OwnerRoles, F.OwnerRole));
        ImGui.SetNextItemWidth(Math.Min(260, width - 10));
        if (ImGui.Combo("I play as##ownerrole", ref ri, RoleLabels, RoleLabels.Length)) { F.OwnerRole = OwnerRoles[ri]; changed = true; }
        if (changed) plugin.SaveConfig();

        Gap(4);
        if (owner)
        {
            if (Ui.Button("##savelisting", "Save changes", new Vector2(160, 30), "rose", dairyName.Trim().Length > 0, "Update your dairy's name, listing and your role."))
                G.PublishDairy(dairyName.Trim());
        }
        else
        {
            var problem = G.DairyHereProblem();
            if (Ui.Button("##registernet", "Register here", new Vector2(160, 30), "rose", problem == null,
                    problem ?? "Make the estate you're in your dairy. Nobody else can register it after you."))
                G.RegisterDairyHere(dairyName);
        }
    }

    private void DrawMembers(float width)
    {
        var mine = G.Net.Mine;
        if (F.NetMember == null || mine == null)
        {
            Ui.Wrapped(F.NetMember == null ? "Join or register a dairy to see its members." : "Loading...", Theme.InkSoft, width);
            DrawEmotes(width);
            return;
        }

        // Invite whoever you're targeting
        var roles = G.InvitableRoles;
        if (roles.Length > 0)
        {
            var t = Plugin.TargetManager.Target as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
            var me = Plugin.ObjectTable.LocalPlayer;
            var ok = t != null && me != null && t.GameObjectId != me.GameObjectId;
            var tip = ok ? null : "Target a player first (or right-click them > Velvet Dairy).";
            var label = ok ? $"Invite {t!.Name.TextValue}:" : "Invite your target:";
            ImGui.TextColored(Theme.Ink, label);
            var clicked = Buttons(roles.Select(r => new Btn($"##inv{r}", $"as {r}", r == "farmer" ? "sage" : r == "bull" ? "honey" : "rose", ok, tip)).ToList(), width);
            if (clicked >= 0 && t != null) G.InvitePlayer(t.Name.TextValue, t.HomeWorld.RowId, roles[clicked]);
            Gap(4);
        }

        // A short summary here; the full list lives in the Members pop-out (better with 10+ people)
        var all = mine.Members;
        var online = all.Where(x => x.Online).ToList();
        var inside = online.Where(x => x.Status?.InDairy == true).ToList();
        ImGui.TextColored(Theme.Ink, $"{all.Count} member{(all.Count == 1 ? "" : "s")} · {online.Count} online · {inside.Count} in the dairy");
        ImGui.TextColored(Theme.InkSoft,
            $"{all.Count(x => x.Role == "cow")} cows · {all.Count(x => x.Role == "farmer")} farmers · {all.Count(x => x.Role == "bull")} bulls" +
            (mine.Invites.Count > 0 ? $" · {mine.Invites.Count} invite(s) waiting" : ""));

        var full = all.Count(x => x.Role == "cow" && x.Online && x.Status is { Cap: > 0 } st && (st.Spoiled || st.Milk >= st.Cap - 0.1f));
        if (full > 0) ImGui.TextColored(Theme.RoseDeep, $"{full} cow{(full == 1 ? "" : "s")} full and waiting to be milked");

        if (inside.Count > 0)
        {
            var names = inside.Take(6).Select(x => $"{x.Name.Split(' ')[0]} ({x.Role})");
            Ui.Wrapped("Here now: " + string.Join(", ", names) + (inside.Count > 6 ? $" and {inside.Count - 6} more" : ""), Theme.SageDeep, width);
        }

        Gap(6);
        var manage = G.IsOwner || G.Role == "farmer" ? "Manage members" : "See members";
        if (Ui.Button("##openmembers", manage, new Vector2(200, 34), "blue", true,
                "Opens the member list in its own window: search, filter by role, sort, change roles and remove."))
            plugin.Members.IsOpen = !plugin.Members.IsOpen;
        Gap(6);

        if (G.Role is "cow" or "farmer") DrawEmotes(width);
    }

    // =====================================================================
    // Creamery on the network: owner buy orders and deliveries to other dairies
    // =====================================================================

    private void DrawNetOrders(float width)
    {
        if (!G.Networked) return;
        var net = G.Net;

        // Visiting someone else's public dairy: their buy orders and the creamery's visitor orders
        var visiting = G.VisitingDairy;
        if (visiting != null)
        {
            SubHeading($"Deliveries at {visiting.Name}", width);
            if (!net.OrdersByDairy.ContainsKey(visiting.Id)) net.RefreshOrders(visiting.Id);
            var theirs = net.OrdersByDairy.TryGetValue(visiting.Id, out var l) ? l : new List<BuyOrder>();
            foreach (var o in theirs.ToList())
            {
                var p = G.DeliverProblem(o);
                var have = G.OzAtOrAbove(o.MinGrade);
                var clicked = ListRow(Ico("coin"), $"Buy order · {o.Oz} oz", Theme.Ink,
                    new[]
                    {
                        new Line(o.MinGrade == 0 ? "Any grade" : $"Grade {Grades.Name(o.MinGrade)} or better", Theme.InkSoft),
                        new Line($"The owner pays {o.Coins} coins", Theme.HoneyDeep),
                    },
                    new[] { new Btn($"##nfill{o.Id}", "Deliver", "sage", p == null, p) }, width, Math.Min(1f, have / o.Oz), p == null ? Theme.SageDeep : Theme.BlueDeep);
                if (clicked == 0) G.Deliver(o);
            }

            foreach (var o in G.VisitorOrders(visiting.Id))
            {
                var can = G.ProducesMilk && G.CanFill(o);
                var clicked = ListRow(Ico("bottle"), $"{o.Customer} (visitor order)", Theme.Ink,
                    new[]
                    {
                        new Line($"{o.Oz} oz, {(o.MinGrade == 0 ? "any grade" : $"Grade {Grades.Name(o.MinGrade)} or better")}", Theme.InkSoft),
                        new Line($"Pays {o.Coins} coins · {o.Xp} xp (the creamery pays)", Theme.HoneyDeep),
                    },
                    new[] { new Btn($"##vfill{visiting.Id}{o.Id}", "Deliver", "sage", can, can ? null : G.ProducesMilk ? "Not enough milk of that grade." : "Only cows deliver milk.") }, width);
                if (clicked == 0) G.FillVisitorOrder(visiting.Id, o);
            }

            if (theirs.Count == 0 && G.VisitorOrders(visiting.Id).Count == 0) ImGui.TextColored(Theme.InkSoft, "Nothing to deliver here today.");
        }

        // Owners: buy milk from other players' cows
        if (F.NetMember?.IsOwner != true) return;
        SubHeading("Your buy orders", width);
        if (net.Mine?.Dairy?.IsPublic != true)
        {
            Ui.Wrapped("Make your dairy public (Dairy tab) and cows can come deliver milk to your buy orders.", Theme.InkSoft, width);
            return;
        }

        foreach (var o in net.MyOrders.ToList())
        {
            var left = DateTimeOffset.FromUnixTimeSeconds(o.Expires) - DateTimeOffset.UtcNow;
            if (Row($"##cancelord{o.Id}", "coin", $"{o.Oz} oz, {(o.MinGrade == 0 ? "any grade" : $"Grade {Grades.Name(o.MinGrade)}+")}",
                    $"{o.Coins} coins held · ends in {(left.TotalDays >= 1 ? $"{(int)left.TotalDays}d {left.Hours}h" : Mins(left))}", "Cancel", "cream", true, "Cancel and get the coins back.", width))
                G.CancelBuyOrder(o);
        }

        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderInt("oz##ordoz", ref orderOz, 1, DairyGame.MaxOrderOz)) orderCoins = -1;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        var names = Enumerable.Range(0, Grades.Count).Select(g => g == 0 ? "Any grade" : $"Grade {Grades.Name(g)}+").ToArray();
        if (ImGui.Combo("##ordgrade", ref orderGrade, names, names.Length)) orderCoins = -1;
        if (orderCoins < 0) orderCoins = DairyGame.SuggestedOrderCoins(orderOz, orderGrade);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("coins##ordcoins", ref orderCoins, 5, 25);
        orderCoins = Math.Clamp(orderCoins, 1, orderOz * 40);
        var problem = G.PostOrderProblem(orderOz, orderGrade, orderCoins);
        if (Ui.Button("##postorder", "Post buy order", new Vector2(170, 32), "honey", problem == null, problem ?? "The coins are held until a cow delivers or you cancel."))
            G.PostBuyOrder(orderOz, orderGrade, orderCoins);
    }
}
