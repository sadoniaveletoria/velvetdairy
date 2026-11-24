using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using VelvetDairy.Game;
using VelvetDairy.Network;

namespace VelvetDairy.Windows;

// The Eorzea Milkers Network screens: the Dairies directory, your dairy's roles and members, and buy orders.
public sealed partial class MainWindow
{
    private string listDescription = "";
    private bool listLoaded;
    private int orderOz = 16;
    private int dropOz;
    private int orderCoins = -1;
    private string dirFilter = "";
    private bool dirPublicOnly;
    private bool dirApplying;      // Dairies tab: only dairies taking applications

    private static long NowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string Ago(long unix)
    {
        var d = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        if (d.TotalMinutes < 60) return $"{Math.Max(1, (int)d.TotalMinutes)} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        return $"{(int)d.TotalDays} d ago";
    }

    private static string Until(long unix)
    {
        var d = DateTimeOffset.FromUnixTimeSeconds(unix) - DateTimeOffset.UtcNow;
        return d.TotalHours < 24 ? $"in {Math.Max(1, (int)Math.Ceiling(d.TotalHours))} h" : $"in {(int)Math.Ceiling(d.TotalDays)} days";
    }
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
        min.Y = DrawTiles(min, max.X, NetworkTiles());
        var split = min.X + (max.X - min.X) * 0.62f;
        Panel("##directory", min, new Vector2(split - 7, max.Y), "Dairies on the network", DrawDirectory);
        Panel("##netstatus", new Vector2(split + 7, min.Y), max, "Network", DrawNetStatus, true);
    }

    private Tile[] NetworkTiles()
    {
        var net = G.Net;
        var online = net.Last?.Online ?? net.Directory?.Online ?? 0;
        var mine = net.Leaderboard?.Dairies.FirstOrDefault(r => r.Mine);
        return new Tile[]
        {
            new("Online now", $"{online:N0}", "players with Eorzea Milkers running", -1, Theme.SageDeep),
            new("Dairies", $"{net.Directory?.Dairies.Count ?? 0}", "open to visit on the list", -1, Theme.BlueDeep),
            new("Your dairy", F.NetMember?.Dairy ?? "None yet", F.NetMember == null ? "register one on the Dairy tab" : G.VisitingDairy is { } v ? $"visiting {v.Name} now" : G.AtHome ? "you're home" : "you're away", -1, Theme.RoseDeep),
            new("This week", mine != null ? $"#{mine.Rank}" : "-", mine != null ? $"{mine.WeekXp:N0} dairy xp so far" : "no dairy xp yet this week", -1, Theme.HoneyDeep),
        };
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
            Ui.Wrapped("The network isn't available right now, so Eorzea Milkers is running on its own. Everything local still works.", Theme.InkSoft, width);
            TextLine(Theme.InkSoft, net.Status);
            return;
        }

        var online = net.Last?.Online ?? net.Directory?.Online ?? 0;
        using (fonts.For(fonts.Heading, $"{online}")!.Push()) TextLine(Theme.SageDeep, $"{online:N0} online");
        Ui.Wrapped("Players with Eorzea Milkers running in the last few minutes.", Theme.InkSoft, width);
        TextLine(net.Connected ? Theme.SageDeep : Theme.Warn, net.Status);

        SubHeading("You", width);
        var m = F.NetMember;
        if (m == null) Ui.Wrapped("You aren't in a dairy. Register your own estate on the Dairy tab, or accept an invite.", Theme.Ink, width);
        else
        {
            TextLine(Theme.Ink, m.Dairy);
            TextLine(Theme.Ink, m.IsOwner ? $"Owner · {G.Role}" : G.Role);
        }

        var visiting = G.VisitingDairy;
        if (visiting != null)
        {
            SubHeading("You're visiting", width);
            TextLine(Theme.Ink, visiting.Name);
            TextLine(Theme.InkSoft, "Deliver milk here from Market > Creamery.");
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
                TextLine(col, $"{r.Rank}.");
                ImGui.SameLine(28);
                TextLine(col, $"{r.Name}{(r.Mine ? " (yours)" : "")}");
                ImGui.SameLine();
                TextLine(Theme.InkSoft, $"Lv {r.Level} · {r.WeekXp:N0} xp");
            }

            var reset = NextMonday() - DateTime.UtcNow;
            TextLine(Theme.InkSoft, $"Resets in {(int)reset.TotalDays}d {reset.Hours}h (Monday 00:00 UTC).");
        }

        SubHeading("How it works", width);
        Ui.Wrapped("Owners register their own estate as a dairy and invite farmers, cows and bulls. Farmers can invite cows and bulls. " +
                   "Public dairies show their address here so anyone can visit; private ones only show their name. " +
                   "Farmers fill other dairies' buy orders from inside their own dairy (or in person at a public one), and can drop milk off at any other dairy they visit. " +
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
            Ui.Wrapped("The directory needs the Eorzea Milkers Network.", Theme.InkSoft, width);
            return;
        }

        // The search box takes what's left after the filters; if that's too little, the filters go on the next line
        var style = ImGui.GetStyle();
        float CheckW(string label) => ImGui.GetFrameHeight() + style.ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;
        var filtersW = CheckW("Public only") + CheckW("Taking applications") + ImGui.CalcTextSize("Refresh").X + style.FramePadding.X * 2 + style.ItemSpacing.X * 3;
        var oneLine = width - filtersW >= 200;
        ImGui.SetNextItemWidth(oneLine ? Math.Min(300, width - filtersW - style.ItemSpacing.X) : width);
        ImGui.InputTextWithHint("##dirfilter", "Search by name, world or district", ref dirFilter, 40);
        if (oneLine) ImGui.SameLine();
        ImGui.Checkbox("Public only", ref dirPublicOnly);
        ImGui.SameLine();
        ImGui.Checkbox("Taking applications", ref dirApplying);
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh")) net.RefreshDirectory();
        Gap(4);

        var list = net.Directory?.Dairies;
        var apps = net.Directory?.Applications ?? new List<MyApplication>();
        if (list == null)
        {
            TextLine(Theme.InkSoft, "Loading...");
            return;
        }

        var f = dirFilter.Trim();
        var shown = list.Where(d => (!dirPublicOnly || d.IsPublic) && (!dirApplying || d.LookingFor.Count > 0) &&
                                    (f.Length == 0 || d.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || d.WorldName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                                     d.District.Contains(f, StringComparison.OrdinalIgnoreCase) || d.Owner.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(d => d.Level).ThenByDescending(d => d.CowsHere).ThenByDescending(d => d.IsPublic).ThenBy(d => d.Name).ToList();
        // How many there are (and how many the search or Public only leaves)
        var pub = list.Count(d => d.IsPublic);
        var count = $"{list.Count} dair{(list.Count == 1 ? "y" : "ies")} · {pub} public · {list.Count(d => d.LookingFor.Count > 0)} taking applications";
        TextLine(Theme.BlueDeep, shown.Count == list.Count ? count : $"Showing {shown.Count} of {count}");
        Gap(4);
        if (shown.Count == 0) Ui.Wrapped(list.Count == 0 ? "No dairies yet. Be the first: register your estate on the Dairy tab." : "Nothing matches.", Theme.InkSoft, width);

        foreach (var d in shown)
        {
            var lines = new List<Line>
            {
                new(d.IsPublic || d.Mine ? $"{d.Address} · {d.WorldName}{(DcOf(d) is { Length: > 0 } dc ? $" ({dc})" : "")}" : $"Private · {d.WorldName}", Theme.InkSoft),
                new($"{d.Members} member{(d.Members == 1 ? "" : "s")}{(d.CowsHere > 0 ? $" · {d.CowsHere} there now" : "")}{(d.OpenOrders > 0 ? $" · {d.OpenOrders} buy order{(d.OpenOrders == 1 ? "" : "s")}" : "")}",
                    d.CowsHere > 0 ? Theme.SageDeep : Theme.InkSoft),
            };
            if (d.Owner.Length > 0) lines.Add(new Line($"Owner: {d.Owner}", Theme.InkSoft));
            if (d.LookingFor.Count > 0) lines.Add(new Line($"Looking for: {string.Join(", ", d.LookingFor.Select(r => char.ToUpperInvariant(r[0]) + r[1..] + "s"))}", Theme.SageDeep));
            if (d.Description.Length > 0 && !d.IsPublic && !d.Mine) lines.Add(new Line(d.Description, Theme.Ink));   // public ones: in Details

            // Applying: only when you're not in a dairy yet, and it's looking for someone
            var app = apps.FirstOrDefault(a => a.DairyId == d.Id);
            // In it already (as one of your dairies): no applying
            var inDairy = G.MyDairies.Any(x => x.Id == d.Id) || F.NetMember?.DairyId == d.Id;
            Btn? applyBtn = null;
            if (!d.Mine && d.LookingFor.Count > 0 && !inDairy)
            {
                if (app?.Status == "pending") applyBtn = new Btn($"##appcancel{d.Id}", "Withdraw", "cream", true, $"Applied {Ago(app.Sent)}: waiting for {d.Name}. Withdraw your application.");
                else if (app?.Status == "denied" && app.RetryAt > NowUnix) applyBtn = new Btn($"##apply{d.Id}", "Apply", "rose", false, $"{d.Name} didn't take you last time. You can apply again {Until(app.RetryAt)}.");
                else applyBtn = new Btn($"##apply{d.Id}", "Apply", "rose", true, "Ask to join with the roles you want.");
            }
            if (app?.Status == "pending") lines.Add(new Line($"You applied {Ago(app.Sent)} · waiting for an answer", Theme.HoneyDeep));

            var canVisit = d.Area != null || (d.HouseId != null && d.World != null && d.Ward != null);
            var buttons = new List<Btn>();
            if (applyBtn is { } ab) buttons.Add(ab);
            if (d.IsPublic || inDairy)
                buttons.Add(new Btn($"##details{d.Id}", "Details", "sage", true, $"{d.Name}: who's there now (roles and levels), its tank and stalls."));
            if (canVisit)
            {
                buttons.Add(new Btn($"##visit{d.Id}", "Visit", "blue", !Lifestream.Busy, Lifestream.Installed ? "Travel there with Lifestream." : "Copies the address (install Lifestream to travel automatically)."));
            }

            var title = $"Lv {d.Level}  {d.Name}" + (d.Mine ? "  ·  yours" : d.IsPublic ? "" : "  ·  private");
            var clicked = ListRow(Ico(d.IsPublic ? "milk" : "ribbon", d.IsPublic || d.Mine ? 1f : 0.6f), title, d.Mine ? Theme.RoseDeep : Theme.Ink, lines, buttons.ToArray(), width);
            if (clicked < 0) continue;
            var label = buttons[clicked].Label;
            if (label == "Apply") plugin.Apply.Open(d);
            else if (label == "Withdraw" && app != null) net.WithdrawApplication(app.Id, e => G.Notify(e, 2));
            else if (label == "Visit") Visit(d);
            else if (label == "Details") plugin.WhoHere.Open(d);
        }
    }

    private void Copy(DairyListing d)
    {
        ImGui.SetClipboardText($"{d.WorldName} {d.Address}");
        G.Notify($"Copied: {d.WorldName}, {d.Address}", 0);
    }

    private void Visit(DairyListing d)
    {
        // In someone's inn room: only they can go there. On their Island Sanctuary: they have to bring you.
        if (d.Area is { } solo && Estate.SoloPlace(solo.Territory))
        {
            G.Notify($"{d.Name} is in its owner's inn room: only they can go there.", 0);
            return;
        }

        if (d.Area is { } isle && Estate.Island(isle.Territory))
        {
            G.Notify($"{d.Name} is on its owner's Island Sanctuary: ask them to take you to their island.", 0);
            return;
        }

        if (d.Area is { House.Length: 0 } yard && d.Ward is > 0 && Estate.DistrictKind(yard.Territory) > 0 && Lifestream.Installed)
        {
            // Outdoors in a residential ward: Lifestream takes you to the ward, and the spot is flagged on your map
            var why = Lifestream.GoTo(d.Name, yard.World, Estate.DistrictKind(yard.Territory), d.Ward.Value, d.Plot is > 0 ? d.Plot.Value : 1, 0, false);
            FlagArea(d, yard);
            G.Notify(why ?? $"Lifestream is taking you to {Estate.DistrictName(yard.Territory)}, Ward {d.Ward}; {d.Name} is flagged on your map.", why != null ? 2 : 0);
            return;
        }

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

    /// <summary>Flag a dairy's outdoor box on the map (quietly; nothing if the map can't be opened).</summary>
    private static void FlagArea(DairyListing d, DairyArea ar)
    {
        try
        {
            if (ar.MapId == 0) return;
            var link = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(ar.Territory, ar.MapId, (int)(ar.X * 1000), (int)(ar.Z * 1000));
            Plugin.GameGui.OpenMapWithMapLink(link);
        }
        catch
        {
            // the travel still happens
        }
    }

    private void DrawNetDairy(Vector2 min, Vector2 max)
    {
        var m = F.NetMember;
        if (m == null)
        {
            var (a, b) = Column(min, max, 760);
            Panel("##netplace", a, b, "Your dairy", DrawNetPlace);
            return;
        }

        var labels = m.IsOwner ? new[] { "Overview", "Contract", "Stalls", "Feed & pump", "Listing" } : new[] { "Overview", "Contract", "Stalls", "Feed & pump" };
        var top = SubTabs("dairynet", labels, min, max.X - min.X);
        var mid = (min.X + max.X) / 2;
        switch (Sub("dairynet"))
        {
            case 0:
                DrawDairyOverview(top, max);
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
                    else Ui.Wrapped("Cows: your milking emotes are on Today > Cow. Farmers: the dairy's cows and the emotes that milk them are in Herd Care (HUD).", Theme.InkSoft, w);
                }, true);
                break;
            case 3:
                Panel("##feedbin", top, new Vector2(mid - 7, max.Y), "Feed bin", DrawFeedBin);
                Panel("##pump", new Vector2(mid + 7, top.Y), max, "Velvet pump", DrawPump, true);
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
            var warn = m == null ? "" : $"You stay in {m.Dairy} too; switch between your dairies on this tab.";
            var clicked = ListRow(Ico("ribbon"), $"Invite: {inv.Dairy}", Theme.RoseDeep,
                new[] { new Line($"Join as a {inv.Role} · {inv.World}", RoleColor(inv.Role)), new Line(warn, Theme.InkSoft) },
                new[] { new Btn($"##decl{inv.Id}", "Decline", "cream"), new Btn($"##acc{inv.Id}", "Accept", "sage", true, null) }, width);
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

        TextLine(Theme.Ink, m.Dairy);
        if (mine != null)
            if (mine.Area is { } ma) TextLine(Theme.InkSoft, $"{ma.Zone}{(ma.House.Length > 0 ? " (in a house)" : "")} · {mine.WorldName} · {(mine.IsPublic ? "public" : "private")}");
            else
            TextLine(Theme.InkSoft, $"{mine.District}, Ward {mine.Ward}, {(mine.Room > 0 && Estate.FromId(ulong.TryParse(mine.HouseId, out var h) ? h : 0).Apartment ? $"apartment {mine.Room}" : $"Plot {mine.Plot}")} · {mine.WorldName}" +
                                             $" · {(mine.IsPublic ? "public" : "private")}");
        TextLine(Theme.Ink, m.IsOwner ? $"You own it · you play as {G.Role}" : $"You're a {G.Role} here");
        TextLine(G.Where.InDairy ? Theme.SageDeep : Theme.RoseDeep, G.Where.Why);
        Gap(6);

        DrawMyRoles(width);
        if (m.IsOwner) TextLine(Theme.InkSoft, "Change the name or listing on the Listing tab.");
        else
        {
            Gap(4);
            if (Ui.Button("##leavedairy", "Leave dairy", new Vector2(150, 30), "cream", ImGui.GetIO().KeyShift, $"Hold Shift and click to leave {m.Dairy}. Your other dairies are kept."))
                G.LeaveDairy();
        }

        DrawMyDairies(width);

        SubHeading("Members", width);
        DrawMemberSummary(width);
    }

    private bool startingOwn;

    /// <summary>
    /// Every dairy you belong to, with a switch to play in another (walking into one of them switches too), and starting
    /// your own while you're in someone else's. Your levels are yours wherever you play.
    /// </summary>
    private void DrawMyDairies(float width)
    {
        var all = G.MyDairies;
        var m = F.NetMember;
        if (all.Count > 1)
        {
            SubHeading("Your dairies", width);
            Ui.Wrapped("You can belong to several dairies. Everything you do counts for the one you're playing in; walking into another of yours switches to it. Your levels go with you everywhere.", Theme.InkSoft, width);
            foreach (var d in all)
            {
                var now = d.Id == m?.DairyId;
                var clicked = ListRow(Ico(d.IsOwner ? "coin" : "ribbon"), d.Name, now ? Theme.SageDeep : Theme.Ink,
                    new[] { new Line((d.IsOwner ? "You own it · " : "") + (d.RoleText.Length > 0 ? d.RoleText : "member") + (now ? " · playing here now" : ""), now ? Theme.SageDeep : Theme.InkSoft) },
                    now ? Array.Empty<Btn>() : new[] { new Btn($"##switch{d.Id}", "Play here", "sage", true, $"Play in {d.Name} now: its stalls, contract and tank.") }, width);
                if (clicked == 0) G.SwitchDairy(d);
            }
        }

        if (G.OwnedDairy == null)
        {
            Gap(6);
            if (!startingOwn)
            {
                if (Ui.Button("##startown", "Start my own dairy", new Vector2(190, 30), "rose", true, $"Register a dairy of your own. You stay in {m?.Dairy ?? "this dairy"} too."))
                    startingOwn = true;
            }
            else
            {
                SubHeading("Start my own dairy", width);
                Ui.Wrapped($"You stay in {m?.Dairy ?? "this dairy"} too, and can switch between them here.", Theme.InkSoft, width);
                DrawRegisterForm(width, false);
                if (ImGui.SmallButton("Never mind##startowncancel"))
                {
                    startingOwn = false;
                    G.CancelDraft();
                }
            }
        }
    }

    private void DrawContract(float width)
    {
        var c = G.Net.Progress?.Contract;
        if (c == null)
        {
            TextLine(Theme.InkSoft, "Loading...");
            return;
        }

        Ui.Wrapped("The Grand Creamery wants milk from your dairy every week. Farmers and the owner fill jugs from the dairy tank and bring them to any Grand Creamery. Fill it for the full reward; if it isn't full by Monday, your dairy still gets the share it delivered.", Theme.InkSoft, width);
        Gap(8);
        using (fonts.For(fonts.Heading, $"{c.OzDone:0}")!.Push())
            TextLine(c.Completed ? Theme.SageDeep : Theme.Ink, c.Completed ? "Done for this week!" : $"{Units.Num(c.OzDone, "0")} / {Units.Num(c.OzNeed)} {Units.Unit}");
        var cur = ImGui.GetCursorScreenPos();
        var barW = Math.Min(width, 420);
        Ui.Bar(ImGui.GetWindowDrawList(), cur + new Vector2(0, 4), cur + new Vector2(barW, 14), c.Progress, c.Completed ? Theme.SageDeep : Theme.BlueDeep);
        ImGui.Dummy(new Vector2(barW, 20));
        TextLine(Theme.InkSoft, "Any milk counts");
        TextLine(Theme.HoneyDeep, $"Full reward: {c.RewardXp:N0} dairy xp + {c.RewardFund:N0} coins for the fund");
        if (!c.Completed) TextLine(Theme.SageDeep, $"Earned so far: {c.EarnedXp:N0} dairy xp + {c.EarnedFund:N0} coins (paid Monday if it isn't full)");
        var left = NextMonday() - DateTime.UtcNow;
        TextLine(Theme.InkSoft, c.Completed ? $"A new contract comes in {(int)left.TotalDays}d {left.Hours}h." : $"Ends in {(int)left.TotalDays}d {left.Hours}h (Monday 00:00 UTC).");
        if (c.Shippers.Count > 0)
        {
            Gap(6);
            Ui.Wrapped("Shipped by: " + string.Join(", ", c.Shippers.Select(x => $"{x.Name.Split(' ')[0]} {Units.Oz(x.Oz, "N0")}")), Theme.InkSoft, width);
        }

        // The weekend milk contest
        if (c.Contest is { } k)
        {
            Gap(8);
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (k.On)
            {
                var end = TimeSpan.FromSeconds(Math.Max(0, k.End - nowUnix));
                TextLine(Theme.RoseDeep, $"Weekend milk contest: on now (ends in {(int)end.TotalHours}h {end.Minutes}m)");
                Ui.Wrapped("Milk pumped into the tank this weekend also fills the contract, so it counts twice: now, and again when it's shipped.", Theme.SageDeep, width);
                if (k.Top.Count > 0)
                    Ui.Wrapped("Top pumpers: " + string.Join(", ", k.Top.Select((x, i) => $"{i + 1}. {x.Name.Split(' ')[0]} {Units.Oz(x.Oz, "N0")}")), Theme.Ink, width);
            }
            else
            {
                var start = TimeSpan.FromSeconds(Math.Max(0, k.Start - nowUnix));
                TextLine(Theme.InkSoft, $"Weekend milk contest: starts in {(int)start.TotalDays}d {start.Hours}h (Saturday 00:00 UTC)");
            }

            if (k.LastChampion is { } champ)
                TextLine(Theme.HoneyDeep, $"Last weekend's champion: {champ.Name} ({Units.Oz(champ.Oz, "N0")})");
        }

        Gap(6);
        Ui.Wrapped($"Contracts are {Units.Oz(1500, "N0")} at dairy level 1 and grow {Units.Oz(250)} a level, up to {Units.Oz(6000, "N0")}. Any milk counts.", Theme.InkSoft, width);
    }

    private readonly Dictionary<CreameryInfo, Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload?> creameryLinks = new();

    /// <summary>A map link to a Grand Creamery (made once per location).</summary>
    private Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload? CreameryLink(CreameryInfo c)
    {
        if (creameryLinks.TryGetValue(c, out var link)) return link;
        if (creameryLinks.Count > 12) creameryLinks.Clear();
        try
        {
            link = c.MapId > 0 ? new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(c.Territory, c.MapId, (int)(c.X * 1000), (int)(c.Z * 1000)) : null;
        }
        catch
        {
            link = null;
        }

        return creameryLinks[c] = link;
    }

    private void DrawContractShip(float width)
    {
        var c = G.Net.Progress?.Contract;
        if (c == null) return;

        // Where to deliver: any Grand Creamery
        var crs = G.Creameries;
        if (crs.Count > 0)
        {
            var here = G.CreameryHere;
            Ui.Wrapped(here != null ? $"You're at {here.Name}." : crs.Count == 1 ? "Bring your jugs here to ship them." : "Bring your jugs to any of these to ship them.",
                here != null ? Theme.SageDeep : Theme.RoseDeep, width);
            if (here != null && G.CreameryNow is { } now)
            {
                // Everyone at this creamery on any world (you may not see them: other worlds, other instances)
                Ui.Wrapped(now.TodayOz > 0 ? $"Delivered here today: {Units.Oz(now.TodayOz, "N0")} by {now.TodayDairies} dair{(now.TodayDairies == 1 ? "y" : "ies")}." : "Nothing delivered here yet today.", Theme.BlueDeep, width);
                if (now.Here.Count > 0)
                {
                    var names = now.Here.Take(6).Select(p => p.World.Length > 0 ? $"{p.Name} ({p.World})" : p.Name);
                    Ui.Wrapped("Also here now, on any world: " + string.Join(", ", names) + (now.Here.Count > 6 ? $" and {now.Here.Count - 6} more" : "") + ".", Theme.InkSoft, width);
                }
                else TextLine(Theme.InkSoft, "No one else is here right now.");
            }
            foreach (var cr in crs)
            {
                // The name with its Map button, and where it is underneath (so narrow panels don't cut it off)
                var link = CreameryLink(cr);
                TextLine(Theme.Ink, cr.Name);
                if (link != null)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Map##creamerymap{cr.Slot}")) Plugin.GameGui.OpenMapWithMapLink(link);
                }

                Ui.Wrapped(link != null ? $"{cr.Zone} ({link.XCoord:0.0}, {link.YCoord:0.0})" : cr.Zone, Theme.InkSoft, width);
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
            Ui.Wrapped("This week's contract is done. Spare milk can go to other dairies (Market > Creamery > Deliveries).", Theme.SageDeep, width);
            return;
        }

        TextLine(Theme.Ink, $"Your jugs: {Units.Oz(G.Carried, "0.#")}");
        if (G.Carried <= 0) Ui.Wrapped("Fill jugs at your dairy first (Herd > Kitchen).", Theme.InkSoft, width);
        var leftOz = (int)Math.Ceiling(c.Left);
        if (G.Carried >= 0.05f)
        {
            var all = (int)Math.Min(Math.Floor(G.Carried + 0.01f), leftOz);
            var pAll = G.ShipProblem(all);
            var clicked = ListRow(Ico("bottle"), "Your milk", Theme.Ink,
                new[] { new Line($"You carry {Units.Oz(G.Carried, "0.#")}", Theme.InkSoft) },
                new[] { new Btn("##shipall", $"Ship {Units.Oz(all, "0")}", "blue", pAll == null && all > 0, pAll) }, width);
            if (clicked == 0) G.Ship(all);
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
        TextLine(Theme.InkSoft, p.NextXp is { } n ? $"{p.Xp:N0} / {n:N0} xp · {p.WeekXp:N0} this week" : $"Max level · {p.WeekXp:N0} xp this week");
        TextLine(Theme.InkSoft, $"Next stall {p.NextStallPrice:N0} fund coins · best stall: {Catalog.StallTierNames[Math.Clamp(p.MaxTier, 1, 4)]}");
        Ui.Wrapped("Members earn XP for the dairy by milking and caring inside it (up to 300 each a day) and when cows fill its buy orders.", Theme.InkSoft, width);

        // The dairy's milk tank
        if (p.Tank != null)
        {
            Gap(4);
            TextLine(Theme.BlueDeep, $"Dairy milk tank: {Units.Oz(p.Tank.Total, "N1")}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Units.Text("Every milking in this dairy (stalls and hand) fills this tank. Farmers fill their jugs from it."));
            if (p.Tank.Total > 0)
            {
                if (p.Tank.Pumpers.Count > 0)
                    Ui.Wrapped("Top pumpers: " + string.Join(", ", p.Tank.Pumpers.Take(5).Select(x => $"{x.Name.Split(' ')[0]} {Units.Oz(x.Oz, "N0")}")), Theme.InkSoft, width);
            }
            else TextLine(Theme.InkSoft, "Empty. It fills as cows are milked here.");

            // Bulls' seed goes into its own tank (just stored for now)
            TextLine(Theme.BlueDeep, $"Dairy seed tank: {Units.Oz(p.Tank.Seed, "N2")}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Units.Text("Every time a bull is milked in this dairy (stalls and hand), their seed goes into this tank. Uses for it are coming in a later update."));
            if (p.Tank.SeedPumpers.Count > 0)
                Ui.Wrapped("Top bulls: " + string.Join(", ", p.Tank.SeedPumpers.Take(5).Select(x => $"{x.Name.Split(' ')[0]} {Units.Oz(x.Oz, "N1")}")), Theme.InkSoft, width);
        }

        Gap(4);
        TextLine(Theme.HoneyDeep, $"Dairy fund: {p.Fund:N0} coins");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Units.Text("Anyone in the dairy can add coins. The owner spends them on new stalls and stall upgrades."));
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

    /// <summary>The dairy's feed bin: farmers feed from it for free; the owner unlocks better feed with the fund.</summary>
    private void DrawFeedBin(float width)
    {
        var p = G.Net.Progress;
        if (p == null)
        {
            TextLine(Theme.InkSoft, "Loading...");
            return;
        }

        Ui.Wrapped("Cows and bulls get their Fed bar from farmers, who feed them from this bin as often as they like (each one every 15 minutes). "
                   + "Better feed gives a bigger Fed bonus. The owner unlocks it once, from the dairy fund.", Theme.InkSoft, width);
        TextLine(Theme.HoneyDeep, $"Dairy fund: {p.Fund:N0} coins · dairy level {p.Level}");
        Gap(4);
        foreach (var o in p.FeedBinShop)
        {
            var f = Catalog.FeedById(o.Item);
            var have = p.FeedBin.Contains(o.Item);
            var why = have ? null : !G.IsOwner ? "The owner unlocks feed." : p.Level < o.Level ? $"Needs dairy level {o.Level}." : p.Fund < o.Price ? $"Needs {o.Price:N0} coins in the fund." : null;
            var lines = new[]
            {
                new Line($"Fed +{(f?.Fed ?? 0) * 100:0}%", Theme.SageDeep),
                new Line(have ? "In the bin" : $"Dairy level {o.Level} · {o.Price:N0} coins from the fund", have ? Theme.SageDeep : Theme.InkSoft),
            };
            var btns = have ? Array.Empty<Btn>() : new[] { new Btn($"##bin{o.Item}", "Unlock", "honey", why == null, why ?? $"Spend {o.Price:N0} from the fund.") };
            if (ListRow(Ico(f?.Icon ?? "grain", have ? 1f : 0.6f), o.Name, have ? Theme.Ink : Theme.InkSoft, lines, btns, width) == 0)
                G.BuyFeedBin(o.Item, o.Name);
        }
    }

    /// <summary>The Velvet pump: the owner spends from the fund to speed up everyone's milk and seed for a while.</summary>
    private void DrawPump(float width)
    {
        var p = G.Net.Progress;
        if (p == null) return;
        Ui.Wrapped($"Runs for {p.PumpMinutes / 60} hours: everyone milked in the dairy makes milk and seed {Catalog.PumpBonus * 100:0}% faster. "
                   + $"It costs {p.PumpCost:N0} coins from the fund each time, from dairy level {p.PumpLevel}.", Theme.InkSoft, width);
        Gap(4);
        var left = G.PumpLeft;
        TextLine(left > TimeSpan.Zero ? Theme.SageDeep : Theme.InkSoft, left > TimeSpan.Zero ? $"Running: {DairyGame.Wait(left)} left" : "Not running");
        TextLine(Theme.HoneyDeep, $"Dairy fund: {p.Fund:N0} coins");
        var why = !G.IsOwner ? "The owner runs the pump." : p.Level < p.PumpLevel ? $"Needs dairy level {p.PumpLevel}." : left.TotalMinutes > p.PumpMinutes ? "It already has over 2 hours left."
            : p.Fund < p.PumpCost ? $"Needs {p.PumpCost:N0} coins in the fund." : null;
        if (Ui.Button("##pumpgo", left > TimeSpan.Zero ? "Add 2 hours" : "Run the pump", new Vector2(150, 30), "rose", why == null, why ?? $"Spend {p.PumpCost:N0} from the fund."))
            G.StartPump();

        if (Channel.Dev && G.IsOwner)
        {
            Gap(10);
            SubHeading("Test bots (dev only)", width);
            Ui.Wrapped("Adds Daisy (a cow), Hank (a farmer) and Brutus (a bull) to your dairy. They make milk and seed, get milked at stalls, "
                       + "and Hank grooms and feeds everyone and makes deliveries. Hand milk, groom and feed Daisy and Brutus in Herd Care. Set their quotas in the Members list.", Theme.InkSoft, width);
            var hasBots = G.Net.Mine?.Members.Any(m => m.Status?.Bot == true) == true;
            if (Ui.Button("##botsadd", hasBots ? "Reset the bots" : "Add test bots", new Vector2(150, 30), "honey", true, "Adds fresh bots (replacing any you have).")) G.SetBots(true);
            ImGui.SameLine();
            if (Ui.Button("##botsremove", "Remove bots", new Vector2(130, 30), "cream", hasBots, "Takes them out of your dairy.")) G.SetBots(false);
        }

        if (Channel.Dev && G.Networked)
        {
            Gap(10);
            SubHeading("Public test dairy (dev only)", width);
            Ui.Wrapped("Makes Test Pasture (dev): a public dairy owned by Test Bot, with Bessie (a cow) and Clyde (a farmer), in a box centered "
                       + "where you're standing. You're not a member, so walking into it makes you a guest. Stand somewhere outside your own dairy. "
                       + "Then sit on a seat in it and add a test stall. Making it again moves it here.", Theme.InkSoft, width);
            if (Ui.Button("##testdairy", G.HasTestDairy ? "Move it here" : "Make it here", new Vector2(150, 30), "honey", true, "A 16 x 12 box around you (10 x 10 in a house)."))
                G.MakeTestDairyHere();
            ImGui.SameLine();
            if (Ui.Button("##teststall", "Add a test stall here", new Vector2(170, 30), "sage", G.HasTestDairy, "Sit on a seat inside Test Pasture first. Free, Padded tier."))
                G.AddTestStallHere();
            ImGui.SameLine();
            if (Ui.Button("##testdairyoff", "Remove it", new Vector2(110, 30), "cream", G.HasTestDairy, "Deletes Test Pasture, its stalls and its bots."))
                G.RemoveTestDairy();
        }
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
            TextLine(Theme.Ink, "I play as (any mix):");
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
            TextLine(Theme.InkSoft, a != null ? $"{a.Zone} · {a.Width:0} × {a.Length:0} y" : "Your dairy is an older house dairy. Mark out an area to move it anywhere.");
            DrawAreaEditor(width, false);

            Gap(10);
            SubHeading("Private place", width);
            Ui.Wrapped("A second spot just for your dairy's members, like your home or apartment. Members can be milked, rest and do chores there, " +
                       "and it counts as being in the dairy. Visitors, the directory and deliveries only ever see the public area above.", Theme.InkSoft, width);
            var pa = F.NetMember?.PrivateArea;
            TextLine(pa != null ? Theme.Ink : Theme.InkSoft, pa == null ? "None yet."
                : pa.WholeHouse ? $"A whole house in {pa.Zone}" : $"{pa.Zone}{(pa.House.Length > 0 ? " (inside a house)" : "")} · {pa.Width:0} × {pa.Length:0} y");
            if (G.Draft == null)
            {
                var why = G.WholeHouseProblem();
                if (Ui.Button("##wholehouse", "Use this whole house", new Vector2(190, 30), "sage", why == null,
                        why ?? "Anywhere inside the house or apartment you're in counts. Nothing to draw."))
                    G.UseWholeHouseAsPrivate();
                ImGui.SameLine();
            }

            DrawAreaEditor(width, false, true);
            if (pa != null && G.Draft == null)
            {
                ImGui.SameLine();
                if (Ui.Button("##removeprivate", "Remove", new Vector2(110, 30), "cream", true, "Takes the private place away. Stalls there stop working.")) G.RemovePrivatePlace();
            }
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
    private void DrawAreaEditor(float width, bool registering, bool privatePlace = false)
    {
        var d = G.Draft;
        var id = privatePlace ? "priv" : "pub";
        if (d == null || G.DraftPrivate != privatePlace)
        {
            var label = registering ? "Start marking here" : privatePlace ? "Draw the borders" : "Move or resize";
            if (Ui.Button($"##startarea{id}", label, new Vector2(190, 30), "blue", d == null,
                    d != null ? "Finish or cancel the other box first." : privatePlace
                        ? "Go to your home or apartment first. Starts a box centered on you; you can change everything before saving."
                        : "Starts a box centered on you, facing the way you face. You can change everything before saving."))
                G.StartDraft(privatePlace);
            return;
        }

        TextLine(Theme.Ink, $"{(d.Zone.Length > 0 ? d.Zone : "Here")}{(d.House.Length > 0 ? " (inside this house)" : "")}");
        TextLine(Theme.InkSoft, "Pink edges are the sides; the blue edge is the front.");
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
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Units.Text("How far above or below the floor still counts as inside (stairs, lofts)."));
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
        else if (Ui.Button($"##savearea{id}", privatePlace ? "Save private place" : "Save area", new Vector2(180, 30), "rose", true,
                     privatePlace ? "Saves this box as your dairy's private place. It can't overlap another dairy." : "Moves your dairy to this box. Stalls outside the new box stop working."))
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
                "cow" => "Make milk and get milked here; paid for reaching your daily quota.",
                "farmer" => "Hand milk cows, fill jugs, deliver, invite.",
                _ => "Make seed, get milked and breed cows (Corruption).",
            };
            var buttons = new List<Btn>();
            if (!has)
            {
                var p = G.AddRoleProblem(null, r);
                buttons.Add(new Btn($"##addme{r}", "Add", "sage", p == null, p ?? $"Become a {r} too. You keep your other roles."));
            }
            else if (G.IsOwner || r is "cow" or "bull")
                buttons.Add(new Btn($"##rmme{r}", "Remove", "cream", ImGui.GetIO().KeyShift,
                    "Hold Shift and click. Your progress as a " + r + " is kept." + (r is "cow" or "bull" ? " It's dropped in every dairy you're in." : "")));

            var clicked = ListRow(null, $"{Roles.Title(r)}{(has ? "  ·  yours" : "")}", has ? RoleColor(r) : Theme.InkSoft,
                new[] { new Line(desc, Theme.InkSoft) }, buttons, width);
            if (clicked == 0)
            {
                if (has) G.RemoveRole(null, r);
                else G.AddRole(null, r);
            }
        }

        Ui.Wrapped("Cow and bull are yours: add or drop them any time, and they hold in every dairy you're in. Farmer is given by a farmer or the owner of each dairy, and only the owner takes it away. Your progress in each role is always kept.", Theme.InkSoft, width);
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
            var tip = ok ? null : "Target a player first (or right-click them > Eorzea Milkers).";
            var label = ok ? $"Invite {t!.Name.TextValue}:" : "Invite your target:";
            TextLine(Theme.Ink, label);
            var clicked = Buttons(roles.Select(r => new Btn($"##inv{r}", $"as {r}", r == "farmer" ? "sage" : r == "bull" ? "honey" : "rose", ok, tip)).ToList(), width);
            if (clicked >= 0 && t != null) G.InvitePlayer(t.Name.TextValue, t.HomeWorld.RowId, roles[clicked]);
            Gap(4);
        }

        // A short summary here; the full list lives in the Members pop-out (better with 10+ people)
        var all = mine.Members;
        var online = all.Where(x => x.Online).ToList();
        var inside = online.Where(x => x.Here).ToList();
        TextLine(Theme.Ink, $"{all.Count} member{(all.Count == 1 ? "" : "s")} · {online.Count} online · {inside.Count} in the dairy");
        TextLine(Theme.InkSoft,
            $"{all.Count(x => x.Has("cow"))} cows · {all.Count(x => x.Has("farmer"))} farmers · {all.Count(x => x.Has("bull"))} bulls" +
            (mine.Invites.Count > 0 ? $" · {mine.Invites.Count} invite(s) waiting" : ""));

        var full = all.Count(x => x.Has("cow") && x.Online && x.Status is { Cap: > 0 } st && (st.Spoiled || st.Milk >= st.Cap - 0.1f));
        if (full > 0) TextLine(Theme.RoseDeep, $"{full} cow{(full == 1 ? "" : "s")} full and waiting to be milked");
        var fullBulls = all.Count(x => x.Has("bull") && x.Online && x.Status is { SeedCap: > 0 } st && (st.SeedSpoiled || st.SeedOz >= st.SeedCap - 0.01f));
        if (fullBulls > 0) TextLine(Theme.RoseDeep, $"{fullBulls} bull{(fullBulls == 1 ? "" : "s")} full and waiting to be milked");

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

        // Buy orders from every other dairy: delivered from inside your own dairy, no travel needed
        if (G.CanCarry)
        {
            SubHeading("Buy orders from other dairies", width);
            JugsNote(width);
            net.RefreshBoard();
            Ui.Wrapped("Deliver from inside your own dairy (or in person at theirs) with milk in your jugs; it goes straight into their tank and they pay what they offered.", Theme.InkSoft, width);
            if (net.Board.Count == 0) TextLine(Theme.InkSoft, "No open buy orders right now.");
            foreach (var o in net.Board.ToList())
            {
                var p = G.DeliverProblem(o);
                var have = G.Carried;
                var here = G.VisitingDairy?.Id == o.DairyId;
                var clicked = ListRow(Ico("coin"), $"{o.DairyName} · {Units.Oz(o.Oz)}", Theme.Ink,
                    new[]
                    {
                        new Line($"Pays {o.Coins} coins", Theme.HoneyDeep),
                    },
                    new[] { new Btn($"##bfill{o.Id}", "Deliver", "sage", p == null, p) }, width, Math.Min(1f, have / o.Oz), p == null ? Theme.SageDeep : Theme.BlueDeep);
                if (clicked == 0) G.Deliver(o);
            }

            Gap(8);
        }

        // Standing in another dairy: drop milk off in their tank, no order needed (the Grand Creamery pays you)
        var visiting = G.VisitingDairy;
        if (G.CanCarry)
        {
        SubHeading(visiting != null ? $"Drop milk off at {visiting.Name}" : "Drop milk off at another dairy", width);
        Ui.Wrapped($"Bring milk from your jugs to any other dairy and leave it in their tank. The Grand Creamery pays you {Catalog.DeliverPay * Grades.All[0].Price:0.#} coins an oz, "
                   + $"your own dairy fund gets 2 coins an oz, and you get farmer xp. You can deliver {Units.Oz(G.DeliverCap, "0")} a day to other dairies "
                   + $"(it grows with your farmer level): {Units.Oz(G.DeliverLeft, "0")} left today. You can make {Catalog.DeliveriesPerDay} deliveries a day "
                   + $"({Math.Max(0, Catalog.DeliveriesPerDay - F.DayDeliveries)} left), and drop off at each dairy once a day. Filling a buy order counts as a delivery too.", Theme.InkSoft, width);
        if (visiting != null)
        {
            var most = (int)Math.Floor(Math.Min(G.Carried, G.DeliverLeft));
            dropOz = Math.Clamp(dropOz <= 0 ? most : dropOz, 0, Math.Max(0, most));
            ImGui.SetNextItemWidth(130);
            if (ImGui.InputInt("oz##dropoz", ref dropOz, 1, 8)) dropOz = Math.Clamp(dropOz, 0, Math.Max(0, most));
            ImGui.SameLine();
            var why = G.DropProblem(dropOz);
            if (Ui.Button("##dropoff", $"Deliver · {DairyGame.DropPay(dropOz)} coins", new Vector2(190, 28), "sage", why == null, why)) G.DropOff(dropOz);
        }
        else TextLine(Theme.InkSoft, "Walk into another dairy (a public one, or another one you belong to) to drop milk off.");
        Gap(8);
        }

        // Owners: buy milk from other players' cows
        if (F.NetMember?.IsOwner != true) return;
        SubHeading("Your buy orders", width);
        Ui.Wrapped("Farmers from other dairies fill these from their own dairy, so it works even if yours is private.", Theme.InkSoft, width);

        foreach (var o in net.MyOrders.ToList())
        {
            var left = DateTimeOffset.FromUnixTimeSeconds(o.Expires) - DateTimeOffset.UtcNow;
            if (Row($"##cancelord{o.Id}", "coin", $"{Units.Oz(o.Oz)} of milk",
                    $"{o.Coins} coins held · ends in {(left.TotalDays >= 1 ? $"{(int)left.TotalDays}d {left.Hours}h" : Mins(left))}", "Cancel", "cream", true, "Cancel and get the coins back.", width))
                G.CancelBuyOrder(o);
        }

        // Type the amount (or step it by 8 / 32): up to the biggest farmer tank
        ImGui.SetNextItemWidth(130);
        if (ImGui.InputInt("oz##ordoz", ref orderOz, 8, 32))
        {
            orderOz = Math.Clamp(orderOz, 1, DairyGame.MaxOrderOz);
            orderCoins = -1;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Units.Text($"Type how much you want, 1 to {Units.Oz(DairyGame.MaxOrderOz)}. A farmer delivers an order in one go, so a big one needs a farmer with a big tank."));
        if (orderCoins < 0) orderCoins = DairyGame.SuggestedOrderCoins(orderOz, 1);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("coins##ordcoins", ref orderCoins, 5, 25);
        orderCoins = Math.Clamp(orderCoins, 1, orderOz * 40);
        var problem = G.PostOrderProblem(orderOz, 0, orderCoins);
        if (Ui.Button("##postorder", "Post buy order", new Vector2(170, 32), "honey", problem == null, problem ?? "The coins are held until a cow delivers or you cancel."))
            G.PostBuyOrder(orderOz, 0, orderCoins);
    }

    /// <summary>
    /// A listing's data center. The server works it out from the world (a fixed table of every world), so it's right
    /// even for listings saved wrong by older versions. (The game's own world sheet, read through Lumina, gave the
    /// data center next to the right one: Lich came out Chaos, Lamia Aether.)
    /// </summary>
    private static string DcOf(DairyListing d) => d.DcName;

    // ----- Seed market -----
    // Dairies sell their spare seed to each other, for breeding stalls. Owners buy (from the dairy fund, into the seed
    // tank) and sell (from the seed tank, coins to the dairy fund); everyone else can see it.

    private int seedSellOz = 5, seedSellPrice = 60, seedBuyOz = 3;
    private bool seedBusy;

    private void DrawSeedMarket(float width)
    {
        G.Net.RefreshSeedMarket();
        var m = G.Net.SeedMarket;
        Ui.Wrapped($"Dairies sell their spare seed here. A breeding stall uses {Units.Oz(DairyGame.BreedStallSeedOz)} of seed for each cow it breeds, " +
                   "so a dairy needs a bull of its own, or seed from another dairy. Owners buy from the dairy fund, and sell from the seed tank.", Theme.InkSoft, width);
        Gap(6);
        if (m == null)
        {
            TextLine(Theme.InkSoft, "Loading...");
            return;
        }

        var owner = m.IsOwner;
        TextLine(Theme.Ink, $"Your seed tank: {Units.Oz(m.Tank, "0.#")}  ·  dairy fund: {m.Fund:N0} coins");
        Gap(8);

        // ---- For sale ----
        SubHeading("For sale", width);
        if (m.Sales.Count == 0) Ui.Wrapped("No other dairy is selling seed right now.", Theme.InkSoft, width);
        else if (owner)
        {
            TextLine(Theme.InkSoft, "Buy");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(110);
            if (ImGui.InputInt("oz at a time##seedbuyoz", ref seedBuyOz)) seedBuyOz = Math.Clamp(seedBuyOz, 1, 500);
            Gap(4);
        }

        foreach (var sale in m.Sales)
        {
            var oz = Math.Min(seedBuyOz, (int)Math.Floor(sale.Oz + 0.05f));
            var cost = oz * sale.Price;
            var why = !owner ? "Only the owner buys seed, from the dairy fund." : oz < 1 ? "Less than an ounce left." : cost > m.Fund ? $"That's {cost:N0} coins; the dairy fund has {m.Fund:N0}." : null;
            var clicked = ListRow(Ico("bottle"), sale.Dairy, Theme.Ink,
                new[]
                {
                    new Line($"{Units.Oz(sale.Oz, "0.#")} for sale · {Units.PerOz(sale.Price)}", Theme.HoneyDeep),
                    new Line($"About {Math.Floor(sale.Oz / DairyGame.BreedStallSeedOz):0} breedings", Theme.InkSoft),
                },
                new[] { new Btn($"##seedbuy{sale.Id}", oz >= 1 ? $"Buy {Units.Oz(oz, "0")} ({cost:N0})" : "Buy", "sage", why == null && !seedBusy, why ?? "From the dairy fund, into your seed tank.") }, width);
            if (clicked == 0)
            {
                seedBusy = true;
                G.Net.BuySeed(sale.Id, oz, r =>
                {
                    seedBusy = false;
                    G.Notify($"Bought {r.Oz} oz of seed from {sale.Dairy} for {r.Cost:N0} coins. Your seed tank has {Units.Oz(r.Tank, "0.#")}.", 1);
                }, e =>
                {
                    seedBusy = false;
                    G.Notify(e, 2);
                });
            }
        }

        // ---- Your listings ----
        if (!owner) return;
        Gap(8);
        SubHeading("Sell your spare seed", width);
        Ui.Wrapped("The seed leaves your tank while it's listed, and the coins go to your dairy fund as it sells. Take a listing down to get the rest back.", Theme.InkSoft, width);
        Gap(4);
        ImGui.SetNextItemWidth(110);
        if (ImGui.InputInt("oz##seedselloz", ref seedSellOz)) seedSellOz = Math.Clamp(seedSellOz, 1, 500);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        if (ImGui.InputInt("coins per oz##seedsellprice", ref seedSellPrice)) seedSellPrice = Math.Clamp(seedSellPrice, 10, 2000);
        var sellWhy = seedSellOz > m.Tank + 0.05f ? $"Your seed tank has {Units.Oz(m.Tank, "0.#")}." : m.Mine.Count >= 3 ? "You can have 3 listings at a time." : null;
        if (Ui.Button("##seedsell", $"Sell {Units.Oz(seedSellOz, "0")} for {seedSellOz * seedSellPrice:N0}", new Vector2(240, 30), "rose", sellWhy == null && !seedBusy, sellWhy))
        {
            seedBusy = true;
            G.Net.SellSeed(seedSellOz, seedSellPrice, () =>
            {
                seedBusy = false;
                G.Notify($"{seedSellOz} oz of seed is up for sale at {seedSellPrice} coins an ounce.", 1);
            }, e =>
            {
                seedBusy = false;
                G.Notify(e, 2);
            });
        }

        Gap(6);
        foreach (var mine in m.Mine)
        {
            var clicked = ListRow(Ico("coin"), $"{Units.Oz(mine.Oz, "0.#")} left at {Units.PerOz(mine.Price)}", Theme.Ink,
                new[] { new Line(mine.Sold > 0 ? $"Sold {Units.Oz(mine.Sold, "0.#")} so far: {mine.Sold * mine.Price:N0} coins to your fund" : "Nothing sold yet", Theme.InkSoft) },
                new[] { new Btn($"##seedclose{mine.Id}", "Take down", "cream", !seedBusy, "The rest goes back into your seed tank.") }, width);
            if (clicked == 0)
            {
                seedBusy = true;
                G.Net.CloseSeedSale(mine.Id, () => seedBusy = false, e =>
                {
                    seedBusy = false;
                    G.Notify(e, 2);
                });
            }
        }
    }
}
