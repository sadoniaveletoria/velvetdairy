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
            ImGui.TextColored(Theme.Ink, m.IsOwner ? $"Owner · {G.Role}" : G.Role);
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

            var canVisit = d.Area != null || (d.HouseId != null && d.World != null && d.Ward != null);
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
        if (d.Area is { } ar && ar.House.Length == 0)
        {
            // Outdoors: flag it on the map and copy where it is
            try
            {
                if (ar.MapId > 0)
                {
                    var link = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(ar.Territory, ar.MapId, (int)(ar.X * 1000), (int)(ar.Z * 1000));
                    Plugin.GameGui.OpenMapWithMapLink(link);
                    ImGui.SetClipboardText($"{d.Name}: {ar.Zone} ( {link.XCoord:0.0} , {link.YCoord:0.0} ) {d.WorldName}");
                    G.Notify($"{d.Name} is flagged on your map ({ar.Zone}, {d.WorldName}).", 0);
                    return;
                }
            }
            catch
            {
                // fall through to copying
            }

            ImGui.SetClipboardText($"{d.Name}: {ar.Zone}, {d.WorldName}");
            G.Notify($"Copied: {ar.Zone}, {d.WorldName}", 0);
            return;
        }

        if (d.Area is { House.Length: > 0 } inHouse && d.HouseId == null) d.HouseId = inHouse.House;

        if (!Lifestream.Installed || d.HouseId == null || !ulong.TryParse(d.HouseId, out var hid))
        {
            Copy(d);
            return;
        }

        var addr = Estate.FromId(hid);
        var err = d.Area != null
            ? Lifestream.GoTo(d.Name, addr.World, addr.DistrictKind, addr.Ward, addr.Plot, addr.Room, addr.Apartment)
            : Lifestream.GoTo(d.Name, d.World!.Value, d.DistrictKind, d.Ward!.Value, d.Plot ?? addr.Plot, d.Room ?? 0, addr.Apartment);
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
        var m = F.NetMember;
        if (m == null)
        {
            var (a, b) = Column(min, max, 760);
            Panel("##netplace", a, b, "Your dairy", DrawNetPlace);
            return;
        }

        var labels = m.IsOwner ? new[] { "Overview", "Contract", "Stalls", "Listing" } : new[] { "Overview", "Contract", "Stalls" };
        var top = SubTabs("dairynet", labels, min, max.X - min.X);
        var mid = (min.X + max.X) / 2;
        switch (Sub("dairynet"))
        {
            case 0:
                Panel("##netplace", top, new Vector2(mid - 7, max.Y), "Your dairy", DrawNetPlace);
                Panel("##netlevel", new Vector2(mid + 7, top.Y), max, "Level, fund and tank", DrawDairyLevel, true);
                break;
            case 1:
                Panel("##contract", top, new Vector2(mid - 7, max.Y), "This week's creamery contract", DrawContract);
                Panel("##contractship", new Vector2(mid + 7, top.Y), max, "Deliver to the Grand Creamery", DrawContractShip, true);
                break;
            case 2:
                Panel("##stalls", top, new Vector2(mid - 7, max.Y), "Stalls", DrawStalls);
                Panel("##stallside", new Vector2(mid + 7, top.Y), max, G.IsOwner ? "Stall upgrades" : "Hand milking", w =>
                {
                    if (G.IsOwner) DrawStallUpgrades(w);
                    else Ui.Wrapped("Cows: your milking emotes are on Roles > Cow. Farmers: the dairy's cows and the emotes that milk them are on Roles > Farmer.", Theme.InkSoft, w);
                }, true);
                break;
            default:
                var (lmin, lmax) = Column(top, max, 760);
                Panel("##listing", lmin, lmax, "Listing", w =>
                {
                    DrawRegisterForm(w, true);
                    Gap(10);
                    SubHeading("Close the dairy", w);
                    Ui.Wrapped("Removes your dairy from the network. Members are told, and its stalls, fund, tank and orders go away.", Theme.InkSoft, w);
                    if (Ui.Button("##closedairy", "Close dairy", new Vector2(150, 30), "cream", ImGui.GetIO().KeyShift, "Hold Shift and click."))
                        G.CloseDairy();
                });
                break;
        }
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
            Ui.Wrapped("You aren't in a dairy yet. Start your own anywhere in the world (below), or wait for an owner or farmer to invite you.", Theme.Ink, width);
            Gap(6);
            DrawRegisterForm(width, false);
            return;
        }

        ImGui.TextColored(Theme.Ink, m.Dairy);
        if (mine != null)
            if (mine.Area is { } ma) ImGui.TextColored(Theme.InkSoft, $"{ma.Zone}{(ma.House.Length > 0 ? " (in a house)" : "")} · {mine.WorldName} · {(mine.IsPublic ? "public" : "private")}");
            else
            ImGui.TextColored(Theme.InkSoft, $"{mine.District}, Ward {mine.Ward}, {(mine.Room > 0 && Estate.FromId(ulong.TryParse(mine.HouseId, out var h) ? h : 0).Apartment ? $"apartment {mine.Room}" : $"Plot {mine.Plot}")} · {mine.WorldName}" +
                                             $" · {(mine.IsPublic ? "public" : "private")}");
        ImGui.TextColored(Theme.Ink, m.IsOwner ? $"You own it · you play as {G.Role}" : $"You're a {G.Role} here");
        ImGui.TextColored(G.Where.InDairy ? Theme.SageDeep : Theme.RoseDeep, G.Where.Why);
        Gap(6);

        DrawMyRoles(width);
        if (m.IsOwner) ImGui.TextColored(Theme.InkSoft, "Change the name or listing on the Listing tab.");
        else
        {
            Gap(4);
            if (Ui.Button("##leavedairy", "Leave dairy", new Vector2(150, 30), "cream", ImGui.GetIO().KeyShift, "Hold Shift and click to leave."))
                G.LeaveDairy();
        }

        SubHeading("Members", width);
        DrawMemberSummary(width);
    }

    private void DrawContract(float width)
    {
        var c = G.Net.Progress?.Contract;
        if (c == null)
        {
            ImGui.TextColored(Theme.InkSoft, "Loading...");
            return;
        }

        Ui.Wrapped("The Grand Creamery wants milk from your dairy every week. Farmers and the owner fill jugs from the dairy tank and bring them there. When it's full, the whole dairy is rewarded.", Theme.InkSoft, width);
        Gap(8);
        using (fonts.For(fonts.Heading, $"{c.OzDone:0}")!.Push())
            ImGui.TextColored(c.Completed ? Theme.SageDeep : Theme.Ink, c.Completed ? "Done for this week!" : $"{c.OzDone:0} / {c.OzNeed} oz");
        var cur = ImGui.GetCursorScreenPos();
        var barW = Math.Min(width, 420);
        Ui.Bar(ImGui.GetWindowDrawList(), cur + new Vector2(0, 4), cur + new Vector2(barW, 14), c.Progress, c.Completed ? Theme.SageDeep : Theme.BlueDeep);
        ImGui.Dummy(new Vector2(barW, 20));
        ImGui.TextColored(Theme.InkSoft, c.MinGrade == 0 ? "Any grade" : $"Grade {Grades.Name(c.MinGrade)} or better");
        ImGui.TextColored(Theme.HoneyDeep, $"Reward: {c.RewardXp:N0} dairy xp + {c.RewardFund:N0} coins for the fund");
        var left = NextMonday() - DateTime.UtcNow;
        ImGui.TextColored(Theme.InkSoft, c.Completed ? $"A new contract comes in {(int)left.TotalDays}d {left.Hours}h." : $"Ends in {(int)left.TotalDays}d {left.Hours}h (Monday 00:00 UTC).");
        if (c.Shippers.Count > 0)
        {
            Gap(6);
            Ui.Wrapped("Shipped by: " + string.Join(", ", c.Shippers.Select(x => $"{x.Name.Split(' ')[0]} {x.Oz:N0} oz")), Theme.InkSoft, width);
        }

        Gap(6);
        Ui.Wrapped("Contracts grow 40 oz with each dairy level. Higher levels ask for better grades: B from level 5, A from 10, S from 15.", Theme.InkSoft, width);
    }

    private Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload? creameryLink;
    private CreameryInfo? creameryLinkFor;

    /// <summary>A map link to the Grand Creamery (made once per location).</summary>
    private Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload? CreameryLink(CreameryInfo c)
    {
        if (creameryLinkFor == c) return creameryLink;
        creameryLinkFor = c;
        try
        {
            creameryLink = c.MapId > 0 ? new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(c.Territory, c.MapId, (int)(c.X * 1000), (int)(c.Z * 1000)) : null;
        }
        catch
        {
            creameryLink = null;
        }

        return creameryLink;
    }

    private void DrawContractShip(float width)
    {
        var c = G.Net.Progress?.Contract;
        if (c == null) return;

        // Where to deliver
        var cr = G.Creamery;
        if (cr != null)
        {
            var link = CreameryLink(cr);
            ImGui.TextColored(Theme.Ink, cr.Name);
            ImGui.TextColored(Theme.InkSoft, link != null ? $"{cr.Zone} ( {link.XCoord:0.0} , {link.YCoord:0.0} )" : cr.Zone);
            ImGui.TextColored(G.AtCreamery ? Theme.SageDeep : Theme.RoseDeep, G.AtCreamery ? "You're here." : "Bring your jugs here to ship them.");
            if (link != null)
            {
                if (ImGui.SmallButton("Show on map##creamerymap")) Plugin.GameGui.OpenMapWithMapLink(link);
            }

            Gap(6);
        }
        else Ui.Wrapped("Ship from your dairy for now (the Grand Creamery hasn't opened yet).", Theme.InkSoft, width);

        if (!G.CanCarry)
        {
            Ui.Wrapped("Farmers and the owner deliver contract milk. As a cow, every milking in the dairy fills the tank they carry it from.", Theme.InkSoft, width);
            return;
        }

        if (c.Completed)
        {
            Ui.Wrapped("This week's contract is done. Spare milk can go to Customer orders or Deliveries (Creamery tab).", Theme.SageDeep, width);
            return;
        }

        ImGui.TextColored(Theme.Ink, $"Your jugs: {G.Carried:0.#} oz");
        if (G.Carried <= 0) Ui.Wrapped("Fill jugs at your dairy first (Creamery > Jugs).", Theme.InkSoft, width);
        var leftOz = (int)Math.Ceiling(c.Left);
        for (var g = Grades.Count - 1; g >= 0; g--)
        {
            var have = F.Tank![g];
            if (have < 0.05f) continue;
            var grade = g;
            var all = (int)Math.Min(Math.Floor(have + 0.01f), leftOz);
            var pAll = G.ShipProblem(g, all);
            var ok = g >= c.MinGrade;
            var clicked = ListRow(cc => Ui.Seal(ImGui.GetWindowDrawList(), cc, 22, Grades.Name(grade), Theme.GradeColor(grade)),
                $"Grade {Grades.Name(g)}", ok ? Theme.Ink : Theme.InkSoft,
                new[] { new Line(ok ? $"You carry {have:0.#} oz" : "Below this contract's grade", Theme.InkSoft) },
                ok ? new[] { new Btn($"##shipall{g}", $"Ship {all}", "blue", pAll == null && all > 0, pAll) } : Array.Empty<Btn>(), width);
            if (clicked == 0) G.Ship(g, all);
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

        // The dairy's milk tank
        if (p.Tank != null)
        {
            Gap(4);
            ImGui.TextColored(Theme.BlueDeep, $"Dairy milk tank: {p.Tank.Total:N1} oz");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Milk from milkings in this dairy (stalls and hand) is stored here too (you keep yours).\n" +
                                 string.Join("\n", p.Tank.ByGrade.Select((oz, g) => $"Grade {Grades.Name(g)}: {oz:N1} oz")));
            if (p.Tank.Total > 0)
            {
                var parts = p.Tank.ByGrade.Select((oz, g) => (oz, g)).Where(x => x.oz > 0).Select(x => $"{Grades.Name(x.g)} {x.oz:N1}");
                ImGui.TextColored(Theme.InkSoft, string.Join(" · ", parts));
                if (p.Tank.Pumpers.Count > 0)
                    Ui.Wrapped("Top pumpers: " + string.Join(", ", p.Tank.Pumpers.Take(5).Select(x => $"{x.Name.Split(' ')[0]} {x.Oz:N0} oz")), Theme.InkSoft, width);
            }
            else ImGui.TextColored(Theme.InkSoft, "Empty. It fills as cows are milked here.");
        }

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

        if (!owner)
        {
            ImGui.TextColored(Theme.Ink, "I play as (any mix):");
            foreach (var r in Roles.All)
            {
                ImGui.SameLine();
                var on = F.OwnerRoles!.Contains(r);
                if (ImGui.Checkbox($"{Roles.Title(r)}##own{r}", ref on))
                {
                    if (on) F.OwnerRoles.Add(r);
                    else F.OwnerRoles.Remove(r);
                    F.OwnerRoles = Roles.Clean(F.OwnerRoles);
                    changed = true;
                }
            }
        }

        if (changed) plugin.SaveConfig();

        Gap(4);
        if (owner)
        {
            if (Ui.Button("##savelisting", "Save changes", new Vector2(160, 30), "rose", dairyName.Trim().Length > 0, "Update your dairy's name, listing and your role."))
                G.PublishDairy(dairyName.Trim());
            Gap(10);
            SubHeading("Dairy area", width);
            var a = F.NetMember?.Area;
            ImGui.TextColored(Theme.InkSoft, a != null ? $"{a.Zone} · {a.Width:0} × {a.Length:0} y" : "Your dairy is an older house dairy. Mark out an area to move it anywhere.");
            DrawAreaEditor(width, false);
        }
        else
        {
            SubHeading("Where is your dairy?", width);
            Ui.Wrapped("Stand in the middle of where you want your dairy: a field, a corner of a city, or a room in any house. " +
                       "Then size and turn the box until it covers your dairy. It's drawn on the ground while you set it up.", Theme.InkSoft, width);
            Gap(4);
            DrawAreaEditor(width, true);
        }
    }

    /// <summary>Mark out (or move and resize) the dairy's box; the box is drawn on the ground while this is open.</summary>
    private void DrawAreaEditor(float width, bool registering)
    {
        var d = G.Draft;
        if (d == null)
        {
            if (Ui.Button("##startarea", registering ? "Start marking here" : "Move or resize", new Vector2(190, 30), "blue", true,
                    "Starts a box centered on you, facing the way you face. You can change everything before saving."))
                G.StartDraft();
            return;
        }

        ImGui.TextColored(Theme.Ink, $"{(d.Zone.Length > 0 ? d.Zone : "Here")}{(d.House.Length > 0 ? " (inside this house)" : "")}");
        ImGui.TextColored(Theme.InkSoft, "Pink edges are the sides; the blue edge is the front.");
        var w = d.Width;
        var l = d.Length;
        var h = d.Height;
        var turn = d.Rot * 180f / MathF.PI;
        ImGui.SetNextItemWidth(Math.Min(260, width - 120));
        if (ImGui.SliderFloat("Width##areaw", ref w, DairyArea.MinSize, DairyArea.MaxSize, "%.0f y")) d.Width = w;
        ImGui.SetNextItemWidth(Math.Min(260, width - 120));
        if (ImGui.SliderFloat("Length##areal", ref l, DairyArea.MinSize, DairyArea.MaxSize, "%.0f y")) d.Length = l;
        ImGui.SetNextItemWidth(Math.Min(260, width - 120));
        if (ImGui.SliderFloat("Turn##arear", ref turn, -180f, 180f, "%.0f°")) d.Rot = turn * MathF.PI / 180f;
        ImGui.SetNextItemWidth(Math.Min(260, width - 120));
        if (ImGui.SliderFloat("Height##areah", ref h, 2f, 20f, "%.0f y")) d.Height = h;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("How far above or below the floor still counts as inside (stairs, lofts).");
        if (ImGui.SmallButton("Center on me##areacenter")) G.CenterDraftOnMe();
        ImGui.SameLine();
        if (ImGui.SmallButton("Face my way##areaface")) G.FaceDraftMyWay();
        ImGui.SameLine();
        if (ImGui.SmallButton("Cancel##areacancel")) G.CancelDraft();
        Gap(4);
        if (registering)
        {
            var problem = G.DairyHereProblem();
            if (Ui.Button("##registernet", "Register my dairy", new Vector2(190, 32), "rose", problem == null && dairyName.Trim().Length > 0,
                    problem ?? (dairyName.Trim().Length == 0 ? "Give your dairy a name first." : "Saves this area as your dairy. It can't overlap another dairy.")))
                G.RegisterDairyHere(dairyName);
        }
        else if (Ui.Button("##savearea", "Save area", new Vector2(160, 30), "rose", true, "Moves your dairy to this box. Stalls outside the new box stop working."))
            G.SaveDraft();
    }

    /// <summary>Your roles: what each does, add cow or bull yourself, owners can also add farmer or take roles off themselves.</summary>
    private void DrawMyRoles(float width)
    {
        SubHeading("Your roles", width);
        var mine = G.MyRoles;
        foreach (var r in Roles.All)
        {
            var has = mine.Contains(r);
            var desc = r switch
            {
                "cow" => "Make milk and get milked here; paid per ounce.",
                "farmer" => "Hand milk cows, fill jugs, deliver, invite.",
                _ => "A role only for now; more is coming.",
            };
            var buttons = new List<Btn>();
            if (!has)
            {
                var p = G.AddRoleProblem(null, r);
                buttons.Add(new Btn($"##addme{r}", "Add", "sage", p == null, p ?? $"Become a {r} too. You keep your other roles."));
            }
            else if (G.IsOwner)
                buttons.Add(new Btn($"##rmme{r}", "Remove", "cream", ImGui.GetIO().KeyShift, "Hold Shift and click. Your progress as a " + r + " is kept."));

            var clicked = ListRow(null, $"{Roles.Title(r)}{(has ? "  ·  yours" : "")}", has ? RoleColor(r) : Theme.InkSoft,
                new[] { new Line(desc, Theme.InkSoft) }, buttons, width);
            if (clicked == 0)
            {
                if (has) G.RemoveRole(null, r);
                else G.AddRole(null, r);
            }
        }

        if (!G.IsOwner) Ui.Wrapped("Add cow or bull any time. Farmer is given by a farmer or the owner. Only the owner takes roles away. Your progress in each role is always kept.", Theme.InkSoft, width);
        Gap(4);
    }

    private void DrawMemberSummary(float width)
    {
        var mine = G.Net.Mine;
        if (F.NetMember == null || mine == null)
        {
            Ui.Wrapped(F.NetMember == null ? "Join or register a dairy to see its members." : "Loading...", Theme.InkSoft, width);
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
            $"{all.Count(x => x.Has("cow"))} cows · {all.Count(x => x.Has("farmer"))} farmers · {all.Count(x => x.Has("bull"))} bulls" +
            (mine.Invites.Count > 0 ? $" · {mine.Invites.Count} invite(s) waiting" : ""));

        var full = all.Count(x => x.Has("cow") && x.Online && x.Status is { Cap: > 0 } st && (st.Spoiled || st.Milk >= st.Cap - 0.1f));
        if (full > 0) ImGui.TextColored(Theme.RoseDeep, $"{full} cow{(full == 1 ? "" : "s")} full and waiting to be milked");

        if (inside.Count > 0)
        {
            var names = inside.Take(6).Select(x => $"{x.Name.Split(' ')[0]} ({x.RoleText})");
            Ui.Wrapped("Here now: " + string.Join(", ", names) + (inside.Count > 6 ? $" and {inside.Count - 6} more" : ""), Theme.SageDeep, width);
        }

        Gap(6);
        var manage = G.IsOwner || G.IsFarmer ? "Manage members" : "See members";
        if (Ui.Button("##openmembers", manage, new Vector2(200, 34), "blue", true,
                "Opens the member list in its own window: search, filter by role, sort, change roles and remove."))
            plugin.Members.IsOpen = !plugin.Members.IsOpen;
        Gap(6);
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
                var can = G.CanCarry && G.CanFill(o);
                var clicked = ListRow(Ico("bottle"), $"{o.Customer} (visitor order)", Theme.Ink,
                    new[]
                    {
                        new Line($"{o.Oz} oz, {(o.MinGrade == 0 ? "any grade" : $"Grade {Grades.Name(o.MinGrade)} or better")}", Theme.InkSoft),
                        new Line($"Pays {o.Coins} coins · {o.Xp} xp (the creamery pays)", Theme.HoneyDeep),
                    },
                    new[] { new Btn($"##vfill{visiting.Id}{o.Id}", "Deliver", "sage", can, can ? null : G.CanCarry ? "Fill jugs of that grade from your dairy tank first." : "Only farmers and owners deliver milk.") }, width);
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
