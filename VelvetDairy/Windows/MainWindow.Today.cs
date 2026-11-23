using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// The main window's layout (0.9.99): six sections down the side (Today, Herd, Dairy, Market, Dairies, Progress, then
/// Settings and the Guide), each holding one or more pages shown as pills along the top. Every page from the old nine
/// tabs is still here: Barn became Today, Roles split into Today (Cow, Bull) and Herd (Farmer), and Shop, Pantry and
/// the Creamery live under Market.
/// </summary>
public sealed partial class MainWindow
{
    public const int TabToday = 9, TabHerd = 10, TabCow = 11, TabBull = 12, TabFarmer = 13;

    private sealed record Section(string Name, string Icon, int[] Pages, string[] Labels);

    private static readonly Section[] Sections =
    {
        new("Today", "bucket", new[] { TabToday, TabCow, TabBull }, new[] { "Today", "Cow", "Bull" }),
        new("Herd", "hay", new[] { TabHerd, TabFarmer }, new[] { "Herd", "Farmer" }),
        new("Dairy", "milk", new[] { TabDairy }, new[] { "Dairy" }),
        new("Market", "coin", new[] { TabShop, TabPantry, TabCreamery }, new[] { "Shop", "Pantry", "Creamery" }),
        new("Dairies", "bell", new[] { TabDairies }, new[] { "Dairies" }),
        new("Progress", "starclover", new[] { TabProgress }, new[] { "Progress" }),
    };

    private static readonly Section SettingsSection = new("Settings", "gear", new[] { TabSettings }, new[] { "Settings" });

    /// <summary>Old tab numbers (from the HUD, tutorial and buttons) point at their new pages.</summary>
    private static int PageFor(int t) => t switch
    {
        TabBarn => TabToday,
        TabRoles => TabCow,
        _ => t,
    };

    private Section SectionOf(int page) => Sections.FirstOrDefault(s => s.Pages.Contains(page)) ?? SettingsSection;

    /// <summary>The pills for the section's pages (when it has more than one). Returns where the page starts.</summary>
    private Vector2 SectionPills(Vector2 min, float width)
    {
        var s = SectionOf(tab);
        if (s.Pages.Length < 2) return min;
        var id = $"section{s.Name}";
        subTabs[id] = Array.IndexOf(s.Pages, tab);
        var top = SubTabs(id, s.Labels, min, width);
        tab = s.Pages[Math.Clamp(Sub(id), 0, s.Pages.Length - 1)];
        return top;
    }

    private void DrawPage(Vector2 min, Vector2 max)
    {
        tab = PageFor(tab);
        var top = SectionPills(min, max.X - min.X);
        switch (tab)
        {
            case TabToday: DrawToday(top, max); break;
            case TabCow: DrawRolePage(top, max, 0); break;
            case TabBull: DrawRolePage(top, max, 2); break;
            case TabFarmer: DrawRolePage(top, max, 1); break;
            case TabHerd: DrawHerd(top, max); break;
            case TabPantry: DrawPantry(top, max); break;
            case TabShop: DrawShop(top, max); break;
            case TabCreamery: DrawCreamery(top, max); break;
            case TabDairy: DrawDairy(top, max); break;
            case TabDairies: DrawNetwork(top, max); break;
            case TabProgress: DrawProgress(top, max); break;
            default: DrawSettings(top, max); break;
        }
    }

    /// <summary>The sections down the left side, then Settings and the Guide, then the plugins Eorzea Milkers works with.</summary>
    private void DrawSidebar(ImDrawListPtr dl, Vector2 o, float h)
    {
        dl.AddRectFilled(o, o + new Vector2(SideW, h), Ui.U(Theme.Paper, 0.9f));
        dl.AddLine(o + new Vector2(SideW, 0), o + new Vector2(SideW, h), Ui.U(Theme.RoseDeep, 0.22f), 1f);

        const float rowH = 42f;
        var y = o.Y + 12;
        var current = SectionOf(PageFor(tab));
        foreach (var s in Sections)
        {
            if (SidebarRow(dl, o, ref y, rowH, s.Name, s.Icon, current == s, SectionTip(s))) tab = s.Pages[0];
        }

        y += 6;
        dl.AddLine(new Vector2(o.X + 14, y), new Vector2(o.X + SideW - 14, y), Ui.U(Theme.RoseDeep, 0.18f), 1f);
        y += 8;
        if (SidebarRow(dl, o, ref y, 34f, "Settings", "gear", current == SettingsSection, "Settings, backups and support.", small: true)) tab = TabSettings;
        if (SidebarRow(dl, o, ref y, 34f, "Guide", "ribbon", false, "How everything works (/milkers guide).", small: true)) plugin.Guide.Open();

        DrawCompanions(dl, o, h, y);
    }

    private static string SectionTip(Section s) => s.Name switch
    {
        "Today" => "Your quota, bottles, care and goals. Cow and Bull settings are here too.",
        "Herd" => "Farm work: care for and milk the herd, chores, jugs. Your farmer level.",
        "Dairy" => "Your dairy: stalls, the fund, the feed bin and pump, the contract.",
        "Market" => "The Shop, your pantry, your pay and deliveries to other dairies.",
        "Dairies" => "Every dairy, invites, and visiting.",
        _ => "Levels, goals, awards and stats.",
    };

    private bool SidebarRow(ImDrawListPtr dl, Vector2 o, ref float y, float rowH, string name, string icon, bool sel, string tip, bool small = false)
    {
        var min = new Vector2(o.X + 10, y);
        var size = new Vector2(SideW - 20, rowH);
        ImGui.SetCursorScreenPos(min);
        var clicked = ImGui.InvisibleButton($"##side{name}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered) ImGui.SetTooltip(tip);
        if (sel)
        {
            dl.AddRectFilled(min, min + size, Ui.U(Theme.Rose, 0.38f), 8);
            dl.AddRectFilled(min, min + new Vector2(3, size.Y), Ui.U(Theme.RoseDeep), 2);
        }
        else if (hovered) dl.AddRectFilled(min, min + size, Ui.U(Theme.Rose, 0.16f), 8);

        Ui.Icon(dl, icon, min + new Vector2(20, size.Y / 2), small ? 11 : 13, sel || hovered ? 1f : 0.8f);
        Ui.Text(dl, small ? fonts.Small : fonts.Label, small ? name : name.ToUpperInvariant(), min + new Vector2(40, size.Y / 2),
            sel ? Theme.Ink : hovered ? Theme.RoseDeep : Theme.InkSoft, 0f, 0.5f);
        y += rowH + 3;
        return clicked;
    }

    // ----- Today -----

    /// <summary>
    /// Today: the daily quota up top, then Care, then how you're doing and today's goals; your bottles down the right,
    /// which show the milking while you're being milked. Someone who isn't a cow or bull sees their farm work instead.
    /// </summary>
    private void DrawToday(Vector2 min, Vector2 max)
    {
        if (!G.Producer)
        {
            if (G.Networked && (G.IsFarmer || F.NetMember?.IsOwner == true)) DrawFarmBarn(min, max);
            else
            {
                var (a, b) = Column(min, max, 760);
                Panel("##todaynone", a, b, "Today", w =>
                {
                    Ui.Wrapped("You're not a cow or a bull, so nothing fills up for you. Become one on Today > Cow or Today > Bull, or play a farmer (Herd).", Theme.Ink, w);
                    SubHeading("Today's goals", w);
                    DrawDaily(w);
                });
            }

            return;
        }

        const float gap = 14f, careH = 122f;
        var blocked = G.Blocked();
        var rightW = Math.Clamp((max.X - min.X) * 0.34f, 290f, 380f);
        var leftMax = new Vector2(max.X - rightW - gap, max.Y);
        var heroH = Math.Clamp((max.Y - min.Y) * 0.32f, 196f, 224f);

        DrawQuotaHero(min, new Vector2(leftMax.X, min.Y + heroH));
        var careMin = new Vector2(min.X, min.Y + heroH + gap);
        DrawCareRow(careMin, new Vector2(leftMax.X, careMin.Y + careH));
        var lowMin = new Vector2(min.X, careMin.Y + careH + gap);
        var mid = (lowMin.X + leftMax.X) / 2;
        Panel("##needs", lowMin, new Vector2(mid - gap / 2, max.Y), "How you're doing", DrawNeeds);
        Panel("##todaygoals", new Vector2(mid + gap / 2, lowMin.Y), leftMax, "Today's goals", DrawDaily);

        // Your bottles down the right. While you're being milked the lower part shows the milking (who, how much, Stop);
        // with starting by itself turned off, it holds the Start stall button.
        var rightMin = new Vector2(max.X - rightW, min.Y);
        var milkH = G.Active != null ? 196f : !G.AutoStall ? 150f : 0f;
        DrawBottleCard(rightMin, new Vector2(max.X, max.Y - (milkH > 0 ? milkH + gap : 0)));
        if (milkH > 0) Panel("##milking", new Vector2(rightMin.X, max.Y - milkH), max, G.Active != null ? "Milking now" : "Milking", DrawMilking, true);

        if (blocked != null && blocked != "Not logged in.")
            ImGui.GetWindowDrawList().AddText(min + new Vector2(28, -4), Ui.U(Theme.Warn), blocked);
    }

    /// <summary>The daily quota as a ring: how far along, how long to go, what it pays, and Claim.</summary>
    private void DrawQuotaHero(Vector2 min, Vector2 max)
    {
        var dl = ImGui.GetWindowDrawList();
        Ui.Card(dl, min, max);
        var h = max.Y - min.Y;
        var have = G.QuotaHave;
        var need = Math.Max(0.001f, G.QuotaNeed);
        var frac = Math.Clamp(have / need, 0f, 1f);
        var goal = G.Goals().FirstOrDefault(g => g.Bit == G.QuotaBit);
        var claimed = goal != null && G.Claimed(goal);

        // The ring
        var r = Math.Min(62f, h / 2 - 24);
        var c = new Vector2(min.X + 28 + r, min.Y + h / 2);
        Arc(dl, c, r, 0f, 1f, Ui.U(Theme.Linen), 14f);
        if (frac > 0.001f) Arc(dl, c, r, 0f, frac, Ui.U(G.QuotaReached ? Theme.SageDeep : Theme.RoseDeep), 14f);

        Ui.Text(dl, fonts.Title, $"{frac * 100:0}%", c - new Vector2(0, 8), Theme.Ink, 0.5f, 0.5f);
        var fmt = G.QuotaInSeed ? "0.##" : "0.#";
        // Inside the ring: the unit only when it fits
        var amount = $"{Units.Num(have, fmt)} / {Units.Num(need, fmt)} {Units.Unit}";
        if (Ui.Measure(fonts.Small, amount).X > r * 1.45f) amount = $"{Units.Num(have, fmt)} / {Units.Num(need, fmt)}";
        Ui.Text(dl, fonts.Small, amount, c + new Vector2(0, 20), Theme.InkSoft, 0.5f, 0.5f);

        // The words
        var x = c.X + r + 30;
        var w = max.X - x - 24;
        var y = min.Y + 22;
        Ui.Eyebrow(dl, $"Today's {(G.QuotaInSeed ? "bull" : "cow")} quota", new Vector2(x, y), w, Theme.RoseDeep);
        y += 28;
        string title;
        if (claimed) title = "Quota done for today";
        else if (G.QuotaReached) title = "Quota reached: claim your pay";
        else
        {
            var perMin = G.QuotaInSeed ? G.ProductionPerMinute() * DairyGame.SeedRatio : G.ProductionPerMinute();
            var mins = perMin > 0.0001f ? (need - have) / perMin : 0;
            title = mins > 0 ? $"About {Mins(TimeSpan.FromMinutes(mins))} of {(G.QuotaInSeed ? "seed" : "milk")} to go" : "Make milk and get milked to fill it";
        }

        y += TextWrapped(dl, fonts.Heading, title, new Vector2(x, y), w, Theme.Ink) + 4;
        var more = G.HasSecondQuota
            ? $" Bull quota: {Units.Oz(F.DaySeedOz, "0.##")} / {Units.Oz(G.BullQuotaNeed, "0.##")} of seed, pays {G.BullQuotaCoins:N0} (claim it in Today's goals)."
            : "";
        TextWrapped(dl, fonts.Small, $"Pays {G.QuotaCoins:N0} coins when you claim it. A farmer or the owner sets how much; it resets at midnight Central.{more}", new Vector2(x, y), w, Theme.InkSoft);
        y = max.Y - 22 - 34;   // the buttons sit along the bottom
        var label = claimed ? "Claimed" : $"Claim {G.QuotaCoins:N0} coins";
        if (goal != null && Ui.Button("##heroclaim", label, new Vector2(200, 34), "sage", G.QuotaReached && !claimed,
                claimed ? "Already claimed today." : G.QuotaReached ? $"+{G.QuotaCoins} coins" : "Reach your quota first.", new Vector2(x, y)))
            G.Claim(goal);
        if (Ui.Button("##herohow", "Your pay", new Vector2(120, 34), "cream", true, "How your pay is worked out (Market > Creamery).", new Vector2(x + 212, y)))
        {
            tab = TabCreamery;
            subTabs["creamery"] = 0;
        }
    }

    /// <summary>A ring arc from "from" to "to" (0..1 of a full turn, starting at the top, clockwise), as short lines.</summary>
    private static void Arc(ImDrawListPtr dl, Vector2 c, float r, float from, float to, uint col, float thick)
    {
        var steps = Math.Max(2, (int)(72 * (to - from)));
        var prev = Vector2.Zero;
        for (var i = 0; i <= steps; i++)
        {
            var t = from + (to - from) * i / steps;
            var a = -MathF.PI / 2 + MathF.PI * 2 * t;
            var p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            if (i > 0) dl.AddLine(prev, p, col, thick);
            prev = p;
        }
    }

    // ----- Roles -----

    /// <summary>One role's page (0 cow, 1 farmer, 2 bull), without the old Roles pills.</summary>
    private void DrawRolePage(Vector2 min, Vector2 max, int role)
    {
        subTabs["roles"] = role;
        DrawRoles(min, max, false);
    }

    // ----- Herd -----

    /// <summary>
    /// Herd: the cows and bulls in the dairy with you (milk, groom, feed), and beside them your chores and jugs.
    /// Your farmer level, tank and goals are on the Farmer page.
    /// </summary>
    private void DrawHerd(Vector2 min, Vector2 max)
    {
        if (!G.Networked || !(G.IsFarmer || F.NetMember?.IsOwner == true))
        {
            var (a, b) = Column(min, max, 760);
            Panel("##herdnone", a, b, "Herd", w =>
            {
                Ui.Wrapped("Herd is farm work: feeding, grooming and hand milking your dairy's cows and bulls, cleaning stalls, filling jugs and delivering milk. "
                           + "A farmer or the owner can give you the farmer role.", Theme.Ink, w);
                if (!G.Networked) Ui.Wrapped("You're playing off the network, so there's no herd here.", Theme.InkSoft, w);
            });
            return;
        }

        const float gap = 14f;
        var rightW = Math.Clamp((max.X - min.X) * 0.42f, 340f, 460f);
        var split = max.X - rightW - gap;
        Panel("##herdcows", min, new Vector2(split, max.Y), "The herd", w => DrawHandMilkCows(w, false));
        Panel("##herdchores", new Vector2(split + gap, min.Y), max, "Chores & jugs", w =>
        {
            DrawFarmerButtons(w);
            DrawCleanStalls(w);
        }, true);
    }
}
