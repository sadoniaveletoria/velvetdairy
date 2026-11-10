using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VelvetDairy.Windows;

/// <summary>
/// What's new: pops up once after an update with the changes since the version you last saw.
/// "/vdairy changes" opens it any time with the full list.
/// </summary>
public sealed class ChangelogWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private Version shownFrom = new(0, 0);
    private bool checkedThisSession;

    private sealed record Entry(string Version, string Title, string[] Notes);

    /// <summary>Newest first. Write these for players: what changed for them, not how.</summary>
    private static readonly Entry[] Entries =
    {
        new("0.9.10.9", "Hand milk and Kitchen buttons", new[]
        {
            "Farmers get two buttons on the HUD: Hand milk and Kitchen. Each opens its own window.",
            "Hand milk lists your dairy's cows with Target, Milk and Groom, and the animation picker is one click away.",
            "Jugs are filled at the top of the Kitchen now (Fill 8 oz, Fill 32 oz or Fill up), instead of on the Creamery tab.",
        }),
        new("0.9.10.8", "Your grade, not your milk's", new[]
        {
            "Milk no longer has a grade. Your cow grade still sets your pay, and now it also milks you faster and gets more milk out: B +10%, A +20%, S +35%, S+ +50%.",
            "Any milk fills any order, the weekly contract and every recipe. Jugs and the dairy tank just show ounces, and milk you already had was poured together.",
            "Customer orders pay a little better as your farmer level grows.",
            "New: this What's new window. Open it any time with /vdairy changes.",
        }),
        new("0.9.10.7", "Kitchen and Pantry", new[]
        {
            "The Kitchen is its own window: search the recipes, pick how many with - / + / Max, and watch the progress bar.",
            "The Pantry has tabs: Food & treats, Ingredients and the Dairy shelf.",
        }),
        new("0.9.10.6", "Baking and the dairy shelf", new[]
        {
            "Farmers churn butter (16 oz of milk makes 1 butter) and bake Butter cookies (1 butter + 8 oz of milk makes 3).",
            "Cookies go on your dairy's shelf. Members buy them inside the dairy: the baker gets 80% and the fund 20%. The owner sets the price.",
        }),
        new("0.9.10.5", "Deliveries from home", new[]
        {
            "Fill other dairies' buy orders from inside your own dairy. Delivering in person at a public dairy pays 10% more.",
            "Private dairies can post buy orders too.",
            "A small green dot above each stall shows which seats milk you (Settings).",
            "Farmers pick their milking animation in its own pop-out window.",
        }),
        new("0.9.9.4", "Stalls and the HUD", new[]
        {
            "No stall level limit: up to 16 stalls, each one costing a bit more from the fund.",
            "The HUD shows when you're in your dairy, and Care only lists feed and treats you own.",
        }),
        new("0.9.8.0", "A private place", new[]
        {
            "Give your dairy a members-only spot: your whole house, or borders you draw.",
        }),
    };

    public ChangelogWindow(Plugin plugin, Fonts fonts) : base("What's new in Velvet Dairy##VelvetDairyChanges", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        Size = new Vector2(560, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 280), MaximumSize = new Vector2(1200, 1200) };
    }

    private FarmSave F => plugin.Config.Farm;

    private static Version Current => typeof(Plugin).Assembly.GetName().Version ?? new Version(0, 0);

    private static Version Parse(string v) => Version.TryParse(v, out var r) ? r : new Version(0, 0);

    /// <summary>Everything, from the start.</summary>
    public void OpenAll()
    {
        shownFrom = new Version(0, 0);
        IsOpen = true;
    }

    /// <summary>Opens by itself once after an update, with what changed since you last looked.</summary>
    public void CheckAfterUpdate()
    {
        if (checkedThisSession || IsOpen || !F.Confirmed18 || Plugin.ObjectTable.LocalPlayer == null) return;
        checkedThisSession = true;
        var now = Current.ToString();
        if (F.SeenChanges == now) return;

        // Someone brand new doesn't need a list of changes: the walkthrough covers it
        if (!F.TutorialDone)
        {
            F.SeenChanges = now;
            plugin.SaveConfig();
            return;
        }

        // Before this window existed, everyone last saw the 0.9.9.4 release
        shownFrom = Parse(string.IsNullOrEmpty(F.SeenChanges) ? "0.9.9.4" : F.SeenChanges);
        if (Entries.Any(e => Parse(e.Version) > shownFrom)) IsOpen = true;
        else
        {
            F.SeenChanges = now;
            plugin.SaveConfig();
        }
    }

    public override void OnClose()
    {
        var now = Current.ToString();
        if (F.SeenChanges == now) return;
        F.SeenChanges = now;
        plugin.SaveConfig();
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        var list = Entries.Where(e => Parse(e.Version) > shownFrom).ToArray();
        if (list.Length == 0) list = Entries;

        ImGui.BeginChild("##changesbody", new Vector2(0, -44), false);
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        for (var i = 0; i < list.Length; i++)
        {
            var e = list[i];
            if (i > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            using (fonts.For(fonts.Heading, e.Title)!.Push()) ImGui.TextColored(Theme.RoseDeep, e.Title);
            ImGui.TextColored(Theme.InkSoft, $"Version {e.Version}");
            ImGui.Spacing();
            foreach (var n in e.Notes)
            {
                ImGui.TextColored(Theme.HoneyDeep, "•");
                ImGui.SameLine();
                ImGui.TextColored(Theme.Ink, n);
            }
        }

        ImGui.PopTextWrapPos();
        ImGui.EndChild();

        if (shownFrom > new Version(0, 0) && list.Length < Entries.Length)
        {
            if (Ui.Button("##changesall", "Show older changes", new Vector2(180, 32), "cream", true, null)) shownFrom = new Version(0, 0);
            ImGui.SameLine();
        }

        if (Ui.Button("##changesok", "Got it", new Vector2(120, 32), "sage", true, null)) IsOpen = false;
    }
}
