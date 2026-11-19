using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;
using VelvetDairy.Network;

namespace VelvetDairy.Windows;

/// <summary>
/// A dairy's details (Dairies list > Details): where it is, its owner, what it's looking for and its description,
/// what's in its tank, and who's there right now: each player's name, roles and levels, member or visiting, and for
/// cows whether they can be bred. Handy for finding a dairy with a bull. Players who don't share (their scan setting)
/// are only counted.
/// </summary>
public sealed class WhoHereWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private string dairyId = "", dairyName = "";
    private DairyListing? listing;
    private HereReply? reply;
    private string error = "";
    private bool loading;
    private double askedAt = -100;

    public WhoHereWindow(Plugin plugin, Fonts fonts)
        : base("Dairy details##VelvetDairyWhoHere", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        PositionCondition = ImGuiCond.FirstUseEver;
        Position = new Vector2(420, 260);
    }

    private static double Now => ImGui.GetTime();

    public void Open(DairyListing d)
    {
        IsOpen = true;
        listing = d;
        if (d.Id == dairyId && Now - askedAt < 10) return;
        if (d.Id != dairyId) reply = null;
        dairyId = d.Id;
        dairyName = d.Name;
        Ask();
    }

    private void Ask()
    {
        if (loading || dairyId.Length == 0) return;
        loading = true;
        error = "";
        askedAt = Now;
        plugin.Game.Net.WhoIsHere(dairyId, r =>
        {
            loading = false;
            reply = r;
            // The Dairies list's "N there now" was from when the list loaded: bring it up to date too
            if (listing != null && listing.Id == dairyId) listing.CowsHere = r.People.Count + r.Hidden;
        }, e =>
        {
            loading = false;
            error = e;
        });
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        const float width = 380;
        var title = listing != null ? $"Lv {listing.Level}  {dairyName}" : dairyName;
        using (fonts.For(fonts.Heading, title)!.Push()) ImGui.TextColored(Theme.RoseDeep, title);
        ImGui.PushTextWrapPos(width);
        if (listing is { } d)
        {
            ImGui.TextColored(Theme.InkSoft, $"{d.Address} · {d.WorldName}" + (d.DcName.Length > 0 ? $" ({d.DcName})" : ""));
            ImGui.TextColored(Theme.InkSoft, $"{d.Members} member{(d.Members == 1 ? "" : "s")}" + (d.Owner.Length > 0 ? $" · Owner: {d.Owner}" : ""));
            if (d.LookingFor.Count > 0)
                ImGui.TextColored(Theme.SageDeep, $"Looking for: {string.Join(", ", d.LookingFor.Select(r => char.ToUpperInvariant(r[0]) + r[1..] + "s"))}");
            if (d.Description.Length > 0) ImGui.TextColored(Theme.Ink, d.Description);
        }

        if (reply is { } t)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.RoseDeep, "Tank and stalls");
            ImGui.TextColored(Theme.Ink, $"Milk in the tank: {Units.Oz(t.TankMilk)}" + (t.TankSeed > 0 ? $" · Seed: {Units.Oz(t.TankSeed, "0.##")}" : ""));
            ImGui.TextColored(Theme.Ink, $"{t.Stalls} stall{(t.Stalls == 1 ? "" : "s")}");
            ImGui.Spacing();
            ImGui.TextColored(Theme.RoseDeep, "Who's here now");
        }

        ImGui.PopTextWrapPos();
        if (error.Length > 0) ImGui.TextColored(Theme.Warn, error);
        if (reply == null)
        {
            ImGui.TextColored(Theme.InkSoft, loading ? "Looking..." : "Nothing yet.");
            Buttons();
            return;
        }

        var r = reply;
        if (r.People.Count == 0) ImGui.TextColored(Theme.InkSoft, r.Hidden > 0 ? "Nobody here shares who they are." : "Nobody's there right now.");
        var bulls = r.People.Count(p => p.BullLevel > 0);
        if (r.People.Count > 0)
            ImGui.TextColored(bulls > 0 ? Theme.SageDeep : Theme.InkSoft, bulls > 0 ? $"{bulls} bull{(bulls == 1 ? "" : "s")} here" : "No bulls here right now");

        foreach (var p in r.People)
        {
            ImGui.Separator();
            ImGui.TextColored(Theme.Ink, p.Name);
            ImGui.SameLine();
            ImGui.TextColored(Theme.InkSoft, p.Member ? "member" : p.From.Length > 0 ? $"visiting from {p.From}" : "visiting");
            var roles = new[]
            {
                p.CowLevel > 0 ? $"Cow Lv {p.CowLevel}" : "",
                p.BullLevel > 0 ? $"Bull Lv {p.BullLevel}" : "",
                p.FarmerLevel > 0 ? $"Farmer Lv {p.FarmerLevel}" : "",
            }.Where(x => x.Length > 0).ToArray();
            ImGui.TextColored(p.BullLevel > 0 ? Theme.SageDeep : Theme.Ink, roles.Length > 0 ? string.Join(" · ", roles) : "No roles shown");
            if (p.CowLevel > 0)
            {
                var now = r.Now;
                var line = p.Preg.Length > 0 ? $"Pregnant ({DairyGame.StageName(p.Preg)})"
                    : p.BredUntil > now ? "Bred (bonus running)"
                    : p.BreedWait > now ? $"Can be bred in {Math.Max(1, (p.BreedWait - now + 59) / 60)} min"
                    : "Can be bred";
                ImGui.TextColored(Theme.InkSoft, line);
            }
        }

        if (r.Hidden > 0)
        {
            ImGui.Separator();
            ImGui.PushTextWrapPos(width);
            ImGui.TextColored(Theme.InkSoft, r.Hidden == 1 ? "1 more here who doesn't share who they are." : $"{r.Hidden} more here who don't share who they are.");
            ImGui.PopTextWrapPos();
        }

        Buttons();
    }

    private void Buttons()
    {
        ImGui.Spacing();
        var wait = 10 - (Now - askedAt);
        if (Ui.Button("##whorefresh", loading ? "Looking..." : "Refresh", new Vector2(110, 28), "rose", !loading && wait <= 0, wait > 0 ? $"Again in {Math.Ceiling(wait):0} s." : "Look again."))
            Ask();
        ImGui.SameLine();
        if (Ui.Button("##whoclose", "Close", new Vector2(90, 28), "cream", true))
            IsOpen = false;
    }
}
