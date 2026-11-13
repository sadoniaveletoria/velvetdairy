using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VelvetDairy.Windows;

/// <summary>
/// What's new: pops up once after an update with the changes since the version you last saw.
/// "/milkers changes" opens it any time with the full list.
/// </summary>
public sealed class ChangelogWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private Version shownFrom = new(0, 0, 0, 0);
    private bool checkedThisSession;

    private sealed record Entry(string Version, string Title, string[] Notes);

    /// <summary>Newest first. Write these for players: what changed for them, not how.</summary>
    private static readonly Entry[] Entries =
    {
        new("0.9.35", "Apply to join a dairy", new[]
        {
            "Not in a dairy yet? Dairies that are looking for members show what they're looking for (Cows, Farmers, Bulls) and an Apply button on the Dairies tab. Pick the roles you want, add a short note and send it.",
            "You can only be in one dairy at a time. You can apply to several, but when one accepts you, your other applications are withdrawn. Already in a dairy? Leave it first (Dairy tab) to apply to another.",
            "Owners choose what they're looking for in Members > Applications, and answer applications there: untick a role to accept someone without it, or Deny (they can apply again in 3 days). Owners can let their farmers answer applications too (off unless you turn it on).",
            "Whoever can answer gets a message when a new application comes in.",
        }),
        new("0.9.34", "Island Sanctuary dairies with friends", new[]
        {
            "Friends who visit your Island Sanctuary count as in your dairy again when they're members, in its area or private place.",
            "Island dairies never clash with other players' island dairies at the same spot.",
            "Visit on an island dairy tells you to ask the owner to take you to their island.",
        }),
        new("0.9.33", "Better pay, support info and fixes", new[]
        {
            "Daily pay goes much further: the first 3,000 coins of the day are paid in full (was 1,500), then half up to 8,000 (was a quarter up to 4,000), then a tenth. A few hours of milking now buys the top Shop items in days, not weeks.",
            "# Help",
            "Having trouble? Settings > Help > Copy support info (or /milkers support) copies your setup and what the plugin did recently, ready to paste in #bug-reports on the Discord. No passwords or keys are included.",
            "# Fixes",
            "Dairies on an Island Sanctuary made before the last update work again.",
            "The Dairies list shows the ward and plot of dairies inside a house or apartment (it used to show only the area).",
            "Visiting a dairy on someone's Island Sanctuary or in their inn room tells you only the owner can go there.",
            "Fewer network hiccups: a dropped connection is retried once, and old connections aren't reused after they've gone quiet.",
            "The Kitchen shows the dairy pantry without the ingredients set aside for your bake (they're listed on their own), and the pantry refreshes every 10 seconds while the Kitchen is open, so you see what other farmers just used.",
        }),
        new("0.9.30", "Dairy count", new[]
        {
            "The Dairies list shows how many dairies there are (and how many are public) at the top, and how many your search leaves.",
        }),
        new("0.9.29", "Milking animations and island dairies", new[]
        {
            "# Milking animations",
            "Pick an animation mod in Herd Care > Animation..., then the animation you want from its options. Each option shows the emote it uses, read from its name: \"(Dom-Bdance/Sub-Hdance)\" means you (the farmer) do Ball Dance, \"(Gsit0, 1)\" means sit on the ground. The Milk button switches the mod on, does that emote and switches it off when the cow is empty.",
            "An animation that works with several emotes (like \"(Blush/Confirm/Wave/Yes)\") lists them all, and you choose which one you do. Your choice is remembered.",
            "No animation mod? Pick the Milk button's emote from a list of every emote (search it, or one click for Bee's Knees, Sit, Sit on ground or Doze).",
            "Sit poses count as milking. Once seated, /cpose to the pose you want; when the cow is empty you're left seated, so get up when you like.",
            "The Milk button always does your emote. If a cow only counts certain emotes and yours isn't one of them, their row in Herd Care tells you which ones they count. Cows who allow any emote (the default) accept sit poses too.",
            "# Island Sanctuary and inn room dairies",
            "A dairy on your Island Sanctuary or in an inn room is yours alone and no longer clashes with other players' dairies at the same spot on their own island. Dairies already there are fixed the next time their owner visits.",
        }),
        new("0.9.24", "A save for each character", new[]
        {
            "Each character now has their own Eorzea Milkers progress: their own levels, coins, roles, pantry and dairy. Before, every character on your PC shared one save.",
            "Your progress moves to the character you last played it on, by itself. Only if Eorzea Milkers can't tell (say you logged in on a different character first) does it ask whether this is the one: yes moves your progress here, no starts this character fresh and your progress waits for the next one you log in with.",
            "Your save follows your character through name changes and world transfers, and your dairy sees your new name.",
            "A new character starts as a new player and joins a dairy on their own. Your settings (sound, HUD, the 18+ check) carry over from the last character you played.",
            "# Fixes",
            "The Dairies list shows each dairy's own data center, not the one its owner was visiting when they saved it. Dairies already listed are corrected the next time their owner saves the dairy.",
            "Stall markers only show while you're in your dairy.",
        }),
        new("0.9.23", "Pay, the kitchen, levels and Discord", new[]
        {
            "# Pay",
            "New pay per ounce: C 6, B 8, A 10, S 12, S+ 14 coins. Higher grades still milk faster and give more milk.",
            "Everything the game pays you in a day now adds up together, across all your roles: the first 1,500 coins are paid in full, then a quarter up to 4,000, then a tenth. XP is never reduced, and coins from other players (shelf sales, buy orders) don't count. Creamery > Your pay shows where you are; it resets each day.",
            "# Kitchen",
            "Ingredients are shared: butter (and anything else a farmer makes to bake with) goes into the dairy pantry, and every farmer of the dairy can bake with it. Butter you already had moves there too.",
            "Meadow hay is now Wheat grain. Farmers can buy it in the Shop and mill 2 into flour in the Kitchen.",
            "New treat: Cream buns (1 flour, 1 butter and 12 oz of milk make 2) for the dairy shelf: Fed +5%, Happy +7% and a 20 minute milk boost.",
            "Butter cookies are now Happy +6% with a 15 minute milk boost, so baked treats sit in the middle of what the Shop sells.",
            "Treats from farmers count as your treat: one every 15 minutes, whoever gives it, the same as eating one yourself.",
            "Recipes unlock with your farmer level: butter at 1, butter cookies at 3, flour at 6, cream buns at 10. Locked ones show their level in the Kitchen.",
            "Need something the Shop sells for a recipe (like wheat grain)? The Kitchen has a Buy button next to it that opens the Shop right on that item.",
            "# Levels",
            "The role tags at the top of the main window now show your level (COW 23, FARMER 12), and fill up as you get closer to the next one. Hover one for the exact xp.",
            "The tags are a little slimmer, and where you are is one word: HERE, RESTING, PRIVATE or AWAY (hover it for more).",
            "# Discord",
            "Eorzea Milkers has a Discord (18+): ask questions, find a dairy or players, and get update news and testing builds first.",
            "Open it with the Discord button at the top right of the main window, the Join the Discord button here, in the guide (the ? button), or in Settings > Help.",
        }),
        new("0.9.16", "Eorzea Milkers: your grade, baking and farmer tools", new[]
        {
            "# A new name",
            "Velvet Dairy is now Eorzea Milkers, with a new icon and bottle labels. Your progress, dairy and settings are all kept. The command is now /milkers.",
            "# Your grade",
            "Milk no longer has a grade. Your cow grade still sets your pay, and now it also milks you faster and gets more milk out: B +10%, A +20%, S +35%, S+ +50%.",
            "Any milk fills any order, the weekly contract and every recipe. Jugs and the dairy tank just show ounces, and milk you already had was poured together.",
            "# Baking and the dairy shelf",
            "Farmers bake in the Kitchen: churn butter (16 oz of milk makes 1) and bake Butter cookies (1 butter + 8 oz of milk makes 3). Baking takes a little time, shows a progress bar and gives farmer xp.",
            "Cookies go on your dairy's shelf. Members buy them inside the dairy: the baker gets 80% and the fund 20%. The owner sets the price.",
            "The Pantry has tabs: Food & treats, Ingredients and the Dairy shelf.",
            "# Farmer tools",
            "Two new buttons under the HUD: Herd Care (your dairy's cows and bulls: Milk, Groom and Feed) and Kitchen. Each opens its own window.",
            "Each recipe in the Kitchen shows what it does: its bonuses for cows, what it's used for, and what it sells for on the shelf.",
            "Fill your jugs at the top of the Kitchen (Fill 8 oz, Fill 32 oz or Fill up). The Jugs tab on the Creamery is gone.",
            "Pick your milking emote and animation mod from the Herd Care window.",
            "Customer orders pay a little better as your farmer level grows.",
            "Feed your dairy's cows and bulls from the dairy shelf (Feed in the Herd Care window). It's free for the farmer, but you can't feed yourself.",
            "# Deliveries",
            "Fill other dairies' buy orders from inside your own dairy. Delivering in person at a public dairy pays 10% more.",
            "Private dairies can post buy orders too.",
            "# Also new",
            "A small green dot above each stall shows which seats milk you (Settings).",
            "The Milking box on the Barn is smaller when you're not being milked, so the rest of the Barn gets more room.",
            "Turned off \"Farmers can start milking me without asking\"? A farmer pressing Milk on you now pops up a request: Allow or Not now. The farmer's emote only starts once you Allow.",
            "Cows are paid for up to 1,500 oz a day. Milk past that still gives xp.",
            "This What's new window. Open it any time with /milkers changes.",
            "# Fixes",
            "The HUD's status line always fits inside the card, and the bottle stops dripping while you're asleep or not making milk.",
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

    public ChangelogWindow(Plugin plugin, Fonts fonts) : base("What's new in Eorzea Milkers##VelvetDairyChanges", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        Size = new Vector2(560, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 280), MaximumSize = new Vector2(1200, 1200) };
    }

    private FarmSave F => plugin.Config.Farm;

    private static Version Current => Parse(typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0");

    /// <summary>"0.9.11" and "0.9.11.0" are the same version (updates use three numbers, the assembly four).</summary>
    private static Version Parse(string v) =>
        Version.TryParse(v, out var r) ? new Version(r.Major, r.Minor, Math.Max(r.Build, 0), Math.Max(r.Revision, 0)) : new Version(0, 0, 0, 0);

    /// <summary>Everything, from the start.</summary>
    public void OpenAll()
    {
        shownFrom = new Version(0, 0, 0, 0);
        IsOpen = true;
    }

    /// <summary>Opens by itself once after an update, with what changed since you last looked.</summary>
    public void CheckAfterUpdate()
    {
        if (checkedThisSession || IsOpen || !F.Confirmed18 || Plugin.ObjectTable.LocalPlayer == null) return;
        checkedThisSession = true;
        var now = Current.ToString(3);
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
        var now = Current.ToString(3);
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
                // "# Heading" lines group the notes under a small heading
                if (n.StartsWith("# "))
                {
                    ImGui.Spacing();
                    ImGui.TextColored(Theme.BlueDeep, n[2..].ToUpperInvariant());
                    continue;
                }

                ImGui.TextColored(Theme.HoneyDeep, "•");
                ImGui.SameLine();
                ImGui.TextColored(Theme.Ink, n);
            }
        }

        ImGui.PopTextWrapPos();
        ImGui.EndChild();

        if (shownFrom > new Version(0, 0, 0, 0) && list.Length < Entries.Length)
        {
            if (Ui.Button("##changesall", "Show older changes", new Vector2(180, 32), "cream", true, null)) shownFrom = new Version(0, 0, 0, 0);
            ImGui.SameLine();
        }

        if (Ui.Button("##changesdiscord", "Join the Discord", new Vector2(160, 32), "blue", true, "Update news, help and testing builds (18+). Opens in your browser.")) Plugin.OpenDiscord();
        ImGui.SameLine();
        if (Ui.Button("##changesok", "Got it", new Vector2(120, 32), "sage", true, null)) IsOpen = false;
    }
}
