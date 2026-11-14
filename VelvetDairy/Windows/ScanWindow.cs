using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;
using VelvetDairy.Network;

namespace VelvetDairy.Windows;

/// <summary>
/// A scan of another player (target them and press Scan on the HUD, or right-click them): their milk and seed, every
/// bonus speeding them up, their bred bonus, pregnancy and when they can be bred again. Anyone can scan anyone who
/// allows it (their setting: anyone, their dairy only, or nobody). What their plugin last shared, up to a minute or so old.
/// </summary>
public sealed class ScanWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private string name = "", world = "";
    private ScanReply? reply;
    private string error = "";
    private bool loading;
    private double askedAt = -100;
    private const double RefreshGap = 10;

    public ScanWindow(Plugin plugin, Fonts fonts)
        : base("Scan##VelvetDairyScan", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        PositionCondition = ImGuiCond.FirstUseEver;
        Position = new Vector2(400, 320);
    }

    private static double Now => ImGui.GetTime();

    /// <summary>Scan this player and show the card.</summary>
    public void Open(string who, string homeWorld)
    {
        var same = who.Equals(name, StringComparison.OrdinalIgnoreCase) && homeWorld.Equals(world, StringComparison.OrdinalIgnoreCase);
        IsOpen = true;
        if (same && Now - askedAt < RefreshGap) return;
        name = who;
        world = homeWorld;
        if (!same) reply = null;
        Ask();
    }

    private void Ask()
    {
        if (loading || name.Length == 0) return;
        if (!plugin.Game.Networked)
        {
            error = "Scanning needs the Eorzea Milkers Network (Settings).";
            return;
        }

        loading = true;
        error = "";
        askedAt = Now;
        plugin.Game.Net.Scan(name, world, r =>
        {
            loading = false;
            reply = r;
        }, e =>
        {
            loading = false;
            error = e;
        });
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    private static void Row(string label, string value, Vector4 col, float width)
    {
        ImGui.TextColored(Theme.InkSoft, label);
        var w = ImGui.CalcTextSize(value).X;
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), width - w + ImGui.GetStyle().WindowPadding.X));
        ImGui.TextColored(col, value);
    }

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Theme.RoseDeep, title);
    }

    private static void FillBar(float frac, Vector4 col, float width)
    {
        var p = ImGui.GetCursorScreenPos();
        Ui.Bar(ImGui.GetWindowDrawList(), p + new Vector2(0, 2), p + new Vector2(width, 8), Math.Clamp(frac, 0f, 1f), col);
        ImGui.Dummy(new Vector2(width, 10));
    }

    private static string Mins(long seconds) => seconds >= 3600 ? $"{seconds / 3600} h {seconds % 3600 / 60} min" : $"{Math.Max(1, (seconds + 59) / 60)} min";

    public override void Draw()
    {
        const float width = 320;
        var title = name.Length > 0 ? name : "Scan";
        using (fonts.For(fonts.Heading, title)!.Push()) ImGui.TextColored(Theme.RoseDeep, title);

        if (error.Length > 0) ImGui.TextColored(Theme.Warn, error);
        if (reply == null)
        {
            ImGui.TextColored(Theme.InkSoft, loading ? "Scanning..." : "Nothing yet.");
            DrawButtons();
            return;
        }

        var r = reply;
        if (!r.Found || r.Hidden || r.Status == null)
        {
            ImGui.PushTextWrapPos(width);
            ImGui.TextColored(Theme.InkSoft, r.Why.Length > 0 ? r.Why : "There's nothing to show.");
            ImGui.PopTextWrapPos();
            DrawButtons();
            return;
        }

        var s = r.Status;
        var age = s.W > 0 && r.Now > 0 ? Math.Max(0, r.Now - s.W) : 0;
        ImGui.TextColored(Theme.InkSoft, $"{r.World} · {r.Dairy} · {(r.Online ? "online" : "offline")}" + (s.W > 0 ? $" · {(age < 60 ? $"{age}s" : Mins(age))} ago" : ""));

        // Roles and levels
        var roles = Roles.From(r.Roles, r.Role);
        var parts = roles.Select(role => role switch
        {
            "cow" => $"Cow Lv {s.Level}",
            "bull" => $"Bull Lv {s.BullLevel}",
            "farmer" => $"Farmer Lv {s.FarmerLevel}",
            _ => role,
        });
        ImGui.TextColored(Theme.Ink, string.Join(" · ", parts) + (s.Crown ? " · crowned" : ""));
        var old = !Version.TryParse(r.Version, out var v) || v < new Version(0, 9, 59);

        // Milk and seed
        if (s.Cap > 0 || s.SeedCap > 0)
        {
            Section("Milk and seed");
            if (s.Cap > 0)
            {
                Row("Milk", s.Spoiled ? "spoiled" : $"{Units.Oz(s.Milk)} / {Units.Oz(s.Cap, "0")} · Grade {Grades.Name(s.Grade)}", s.Spoiled ? Theme.Warn : Theme.Ink, width);
                FillBar(s.Cap > 0 ? s.Milk / s.Cap : 0, Theme.Rose, width);
            }

            if (s.SeedCap > 0)
            {
                Row("Seed", s.SeedSpoiled ? "spoiled" : $"{Units.Oz(s.SeedOz, "0.##")} / {Units.Oz(s.SeedCap, "0.#")} · Grade {Grades.Name(s.SeedGrade)}", s.SeedSpoiled ? Theme.Warn : Theme.Ink, width);
                FillBar(s.SeedCap > 0 ? s.SeedOz / s.SeedCap : 0, Theme.Blue, width);
            }
        }

        // Bonuses
        Section("Bonuses now");
        if (s.Bonuses.Count == 0)
            ImGui.TextColored(Theme.InkSoft, old ? "They need to update Eorzea Milkers to share their bonuses." : "None right now.");
        foreach (var b in s.Bonuses)
            Row(b.N, b.P >= 0 ? $"+{b.P:0}%" : $"{b.P:0}%", b.P >= 0 ? Theme.SageDeep : Theme.RoseDeep, width);
        if (s.Speed > 0) Row(s.Cap > 0 ? "Total milk speed" : "Total seed speed", $"x{s.Speed:0.00}", Theme.Ink, width);

        // Breeding and pregnancy
        Section("Breeding");
        var now = r.Now > 0 ? r.Now : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isCow = roles.Contains("cow");
        var isBull = roles.Contains("bull");
        if (isCow)
        {
            Row("Bred bonus", s.BredUntil > now && s.BredBy.Length > 0 ? $"by {s.BredBy}: +{s.BredPct:0.#}% for {Mins(s.BredUntil - now)}" : old ? "-" : "None right now",
                s.BredUntil > now ? Theme.RoseDeep : Theme.Ink, width);
            Row("Pregnant", s.Preg.Length > 0 ? DairyGame.StageName(s.Preg) + (s.PregFather.Length > 0 ? $" · Sire: {s.PregFather}" : "") : "No",
                s.Preg.Length > 0 ? Theme.RoseDeep : Theme.Ink, width);
            Row("Can be bred", s.Preg.Length > 0 ? "No, pregnant" : s.BreedWait > now ? $"In {Mins(s.BreedWait - now)}" : old ? "-" : "Yes",
                s.Preg.Length == 0 && s.BreedWait <= now ? Theme.SageDeep : Theme.Ink, width);
        }

        if (isBull) Row("Seed output", s.Seed > 0 ? $"{s.Seed}% next breeding" : "-", Theme.Ink, width);
        if (!isCow && !isBull) ImGui.TextColored(Theme.InkSoft, "Not a cow or bull.");

        DrawButtons();
    }

    private void DrawButtons()
    {
        ImGui.Spacing();
        var wait = RefreshGap - (Now - askedAt);
        if (Ui.Button("##scanrefresh", loading ? "Scanning..." : "Refresh", new Vector2(110, 28), "rose", !loading && wait <= 0,
                wait > 0 ? $"Again in {Math.Ceiling(wait):0} s." : "Scan them again."))
            Ask();
        ImGui.SameLine();
        if (Ui.Button("##scanclose", "Close", new Vector2(90, 28), "cream", true))
            IsOpen = false;
    }
}
