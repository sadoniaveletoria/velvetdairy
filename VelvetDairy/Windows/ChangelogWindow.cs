using System;
using System.Linq;
using System.Numerics;
using VelvetDairy.Game;
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
        new("0.9.84", "Cloud backup and restore", new[]
        {
            "Your save is backed up on the Eorzea Milkers server every few minutes (never your network key or developer phrase). New PC, reinstall or lost settings? You get everything back, with your dairy, memberships and roles.",
            "Restore with your recovery code (Settings > Backup and restore: write it down or screenshot it), or prove the character is yours by putting a code in your Lodestone Character Profile. The code comes from the plugin or from the Discord bot with /restore.",
            "A brand new save offers the restore by itself when that character has played on another install. /milkers restore opens it any time.",
            "If a save ever fails its check and has to start over, it keeps your dairy and network identity, the old save is kept on your PC, and your cloud backup is offered back (a reset save never replaces it).",
            "Builds loaded from Dev Plugin Locations keep their own save, so trying one can never touch your real progress.",
            "The Kitchen shows how many of each the dairy already has on the shelf or in the pantry.",
        }),
        new("0.9.82", "Feeding bell, weekend contest and bonuses that last in a dairy", new[]
        {
            "The feeding bell: farmers and the owner ring it from Herd Care (once an hour). Everyone in the dairy hears it, and each cow and bull picks a bar to fill right up, plus a 20 minute boost.",
            "Weekend milk contest, every Saturday and Sunday (UTC): milk pumped into your dairy's tank also fills the weekly contract. The Contract card shows the top pumpers and last weekend's champion.",
            "A dairy outdoors in a plot's yard shows its plot too (\"Empyreum, Ward 30, Plot 10 (outside)\"), and Visit takes you to that plot. It's added the next time the owner stands in their dairy.",
            "Breeding no longer waits 10 minutes into a scene: Corruption's arousal and climax decide when a breeding happens.",
            "A bar with more than an hour left is full enough: you can't eat, drink, be groomed or pick it at the feeding bell until it's under an hour.",
            "Weekly contracts are bigger: 1,500 oz at dairy level 1, growing 250 oz a level up to 6,000 oz (this week's grows to match).",
            "While you're in a dairy (yours, or a public one you're visiting), your Fed, Watered, Comfy and Happy bars drain half as fast, so your bonuses last twice as long while you stay.",
            "Herd Care lists each cow and bull once (even if they're in several dairies), and only those who are online. Guests at a public dairy see its stall markers.",
            "The version under the plugins list always fits, and the HUD just says Update needed when there's a new version.",
        }),
        new("0.9.73", "Several dairies, guests, bull items and a fairer Shop", new[]
        {
            "# Several dairies",
            "You can belong to several dairies at once, and own one of your own as well. Joining another keeps the ones you're in. In someone else's dairy and want your own? Press Start my own dairy on the Dairy tab.",
            "Everything you do counts for the dairy you're playing in: walking into another of yours switches to it, or press Play here under Your dairies. Your levels and progress go with you everywhere.",
            "Cow and bull are yours: they hold in every dairy you're in, and only you add or drop them. Farmer is given by each dairy.",
            "# Visiting public dairies",
            "In someone else's public dairy you're a guest: you can be milked on its stalls and cared for like at home (your milk goes into their tank). Its farmers can hand milk you after you press Allow.",
            "A bull from another dairy can breed you in a Corruption scene once you press Allow on the pop-up.",
            "# Bulls",
            "Bull-only Shop items by bull level: Nose ring (5), Stud book (10, 25 breedings a day at full output), Brass horn caps (20), Prize stud (25, +25% bull xp from breeding), Breeding pen (35, breedings give your dairy xp), Herd sire (50, seed 15% faster), Champion bull sash (75), Legendary stud (100, 30 breedings a day).",
            "Bull foods: Iron oats (Fed and Watered at once), Bull's salt lick (+25% bull xp for 30 min), Seed tea (next seed milking +25%), Stud draught (your next breeding is half again as strong and lasts 15 minutes longer).",
            "For cows who are also bulls: Twin blend (Fed and Happy at once, plus a boost), Velvet & brass bonbon (+20% cow and bull xp for 30 min), Balance tea (2 hours off your milking time today).",
            "# Breeding (still in testing)",
            "Cow and bull with a partner who's both? Target them and press Breed on the HUD to be the one breeding. The Breed button only shows when you're both cow and bull.",
            "Your roles in the dairy decide who breeds whom; Corruption's carry and breed settings only decide whether it can become a pregnancy, and the scene note says so.",
            "The Champion's ribbon is now a prized cow's ribbon: farmers who hand milk you earn 10% more, being bred lasts 45 minutes, and pregnancy gives 5% more milk.",
            "# A fairer Shop",
            "The Shop only shows what's for your roles. Cow wearables raise pay for milk, bull wearables pay for seed; Easy let-down and Udder balm speed up milk only.",
            "The Prize heifer sash and Deep rest are gone; if you had them, you got their coins back.",
            "# Comfy and resting",
            "Sitting in your dairy no longer speeds up milk by itself. Instead, sit 10 minutes for 30 minutes of Comfy (each further 10 minutes adds 30 more, up to an hour), and the Comfy bar doesn't drain while you sit.",
            "Plush bedding, Lavender bedding, Silk cushion and the Bell collar now say exactly what they do (how much slower each bar drains).",
            "# Also",
            "The owner HUD is gone; the mini HUD and the Dairy tab cover it. Delivering in person to another dairy pays the same as delivering from your own.",
            "The main window shows your Eorzea Milkers version at the bottom left. The mini HUD is a little wider so nothing gets cut off, and the Kitchen button moved to Roles > Farmer.",
            "Details: on the Dairies tab, see a dairy's tank, stalls and description, and who's there right now with their roles and levels (handy for finding a dairy with a bull). It replaces the Copy button.",
            "The Dairies list shows the ward of a dairy outdoors in a residential district, and Visit takes you there with Lifestream (with the spot flagged on your map).",
        }),
        new("0.9.61", "Scanning, breeding choices and a tidier HUD", new[]
        {
            "# Scan other players",
            "Target another player with Eorzea Milkers (within 15 yalms) and press Scan on the HUD, or right-click them > Eorzea Milkers > Scan. You don't need to be a farmer or owner. You see their milk and seed, every bonus speeding them up and the total, their bred bonus (who, how much, how long), pregnancy and Sire, when a cow can be bred again, and a bull's seed output.",
            "Choose who can scan you in Settings: anyone (the default), your dairy only, or nobody. The player you scan needs this version for their bonuses to show.",
            "# A tidier HUD",
            "The mini HUD now only shows while you're in a dairy (yours or one you're visiting), or while you're being milked or in a breeding scene. Want it everywhere? Untick \"Only while I'm in a dairy\" in Settings.",
            "# Breeding (still in testing)",
            "Pregnancy follows what Corruption says your bodies can do (who can breed, who can carry); the scene note tells you when Corruption won't roll one.",
            "If you and your partner are both cow and bull, whoever is breeding targets the other and presses Breed on the HUD (or right-click > Eorzea Milkers > Breed). Only that way counts.",
            "When a cow's bred bonus runs out, everyone in the dairy she's in gets a small pop-up with a soft bell. She gets one too.",
            "# Grand Creameries",
            "At a Grand Creamery you see everyone else standing there on any world and data center, on Dairy > Contract, and the HUD says how many are there.",
            "Each Grand Creamery counts what's been shipped there today: \"Delivered here today: 1,240 oz by 9 dairies.\"",
            "# Also",
            "Comfy has no Rest button any more: sit on furniture in your dairy (a stall seat counts). After 15 minutes seated you get Comfy (30 minutes on the bar), and every further 15 minutes seated adds 30 more, up to an hour. Get up sooner and that sit gives nothing. The Care row shows how long is left.",
            "Bulls who aren't cows get the hand milking settings (milking emotes, ask first) on the Bull tab.",
            "The Dairies list shows each dairy's real data center (Lich on Light, Lamia on Primal).",
            "Milk levels on member lists refresh about every 90 seconds, so the server writes less. Everything you act on stays instant.",
        }),
        new("0.9.52", "The Bull role and breeding", new[]
        {
            "# Bulls and breeding (in testing)",
            "The Bull role is here, with breeding through the Corruption plugin. This is still very much in a testing phase: we're gathering data on how it plays, so numbers and details will change. Please tell us on Discord what feels off.",
            "Bulls make seed and get milked, the way cows make milk. The same four needs (Fed, Watered, Comfy, Happy) speed it up, and care, treats, resting, levels, dozing and tiredness all work the same. If you're a cow and a bull, one set of needs feeds both.",
            "Seed fills vials a tenth the size of your bottles (the bottle upgrades you buy apply to both). It comes in at 10% of milk speed at Grade C, rising to 20% at S+. Seed has its own grades and seed quality, slips after days without milking, and spoils after 3 days full, like milk.",
            "Bulls are milked at stalls or by a farmer's hand. Each oz of seed pays ten times milk's price and gives bull xp; farmers get their usual 25% share for hand milking a bull. Every dairy now has a seed tank (on the Dairy tab); uses for it come later.",
            "Breeding needs Corruption (RaylaPetal/corruption, 0.3.5 or newer) and Cummy. When a bull of your own dairy finishes in a Corruption scene with you, you're bred: +5% milk for 30 minutes, once per scene and at most every 20 minutes. The bull gets up to 40 bull xp, less after 20 breedings in a day.",
            "In a scene with a cow of their dairy, a bull always finishes inside, so Corruption rolls for conception every time. The bull's Corruption needs \"Let other plugins control Corruption\" turned on (Settings > Other plugins), and the cow's Cummy blocks still apply. Whether it takes is up to Corruption.",
            "During a scene the HUD shows who you're with, your arousal and (for the cow) the chance of conceiving.",
            "Pregnancy shows once Corruption confirms it (a test, or once you're showing): on the HUD, under How you're doing, on Roles > Cow (trimester, due date, sire, cravings and past births) and in the member list. Pregnant cows give +5%, +10% and +15% milk by trimester, until the birth. Bulls see which cows of their dairy are pregnant by them.",
            "# A new look",
            "The tabs are now down the left side of the main window, with icons. Under them is the list of helper plugins (Penumbra, Corruption, Cummy, Lifestream) with a green dot for each one you have. If Corruption or Cummy is missing, you're told once, with the link to add it.",
            "The Barn is laid out fresh: your bottle and how you're doing on top, Care as one slim row of cards, and milking along the bottom. Progress has Today and Awards as wide panels.",
            "# Farmers",
            "Herd Care lists bulls too, so farmers can milk, groom and feed them. Feed now asks which treat from the dairy shelf to give.",
            "The Groom and Feed buttons wait the full time before you can use them again, instead of lighting up again after a minute.",
            "# Fixes",
            "The Dairies list shows the right data center for every dairy (Lich is on Light).",
            "Text that was cut off in the Creamery, network, Dairies search and seed panels now fits, and the HUD's grade seal no longer covers the text.",
            "The guide explains that you can stay seated on a stall between milkings and keep your resting bonus.",
        }),
        new("0.9.39", "Metric fix", new[]
        {
            "With metric on, the big milk number on the Barn and the HUD now shows in the same unit as your bottles (for example 2.43 / 2.84 L instead of 82.1 / 2.84 L).",
            "The \"+1 oz\" that floats up as milk comes in, and the milk quality tooltip, now show metric too.",
        }),
        new("0.9.38", "Levels to 100, Grand Creameries, metric and cheese", new[]
        {
            "# Cow levels 31 to 100",
            "New Shop upgrades: Extra bottle 5 at 33 (6 bottles, the most you can have), Bigger bottle 5 at 40 (the golden Velvet churn, 64 oz), Silk cushion at 45 (Comfy drains 15% slower), Heirloom bell at 55 (+3% pay), Night owl at 62 (tired 2 hours later), Royal halter at 70 (+3% pay), Endless spring at 85 (Watered drains 20% slower), Sweet tooth at 92 (treat boosts last 15% longer) and the Velvet crown at 100.",
            "New titles: Dairy Darling (35), Grand Champion (50), Eorzean Treasure (65), Living Legend (80) and Velvet Icon (100).",
            "From level 50 your title shows in gold along the top of the HUD. At 75 the HUD gets a velvet frame and a star shows next to your name in your dairy's member list. The Velvet crown puts a crown on both.",
            "# Farmer levels 23 to 100",
            "Bigger farmer tanks 6 and 7 at 30 and 60 (the Big tanker and Grand tanker).",
            "New recipes: Honey cream cake at 35 and Velvet tart at 70, bigger treats for the dairy shelf.",
            "At 45 your hand milking share goes from 25% to 30%. At 85 everything bakes 20% faster. At 100: Master Farmer, and a crown.",
            "# Bulls",
            "Bull titles: Strong Bull (25), Prize Bull (50), Champion Bull (75) and Legendary Bull (100).",
            "# Grand Creameries and the weekly contract",
            "There can now be a Grand Creamery at each Grand Company. Ship your weekly contract milk at whichever is closest: Dairy > Contract lists them all, each with a map link. While you're at one, the HUD and the location tag say so.",
            "Weekly contracts are bigger: 500 oz at dairy level 1, plus 50 oz a level (up to 1,500), starting with next Monday's. You don't have to fill it: if it isn't full by Monday, your dairy still gets the share it delivered (half the milk, half the reward). Dairy > Contract shows what you've earned so far.",
            "# Metric",
            "Prefer millilitres? Settings > Show milk in metric shows milk in ml and litres everywhere in the plugin's windows and messages (it still counts in ounces underneath, so nothing changes in the game).",
            "# Cheese",
            "New treat: Cheese. Farmers make it in the Kitchen from milk alone (16 oz makes 2) from farmer level 1, so it's the first treat you can make. It goes on the dairy shelf (25 coins by default) and gives cows Happy +4% and a 10 minute milk boost: a little less than butter cookies and cream buns.",
        }),
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
                ImGui.TextColored(Theme.Ink, Units.Text(n));
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
