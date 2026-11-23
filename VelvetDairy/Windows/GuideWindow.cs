using System;
using System.Numerics;
using VelvetDairy.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VelvetDairy.Windows;

/// <summary>
/// How to play: every part of Eorzea Milkers explained, one topic at a time.
/// Opened from the "?" in the main window, "/milkers guide", or the end of the first-run walkthrough.
/// </summary>
public sealed class GuideWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private int topic;

    private sealed record Topic(string Title, string[] Paragraphs);

    // Keep this in step with the game's numbers (Catalog.cs, DairyGame.cs, server/src/index.js).
    private static readonly Topic[] Topics =
    {
        new("Welcome", new[]
        {
            "Eorzea Milkers is a dairy life game for adults (18+). You play a cow, a farmer, a bull or a dairy owner, alone or with friends.",
            "Finding your way: six places down the left side. Today (quota, bottles, care, milking, goals, and your Cow and Bull pages), Herd (farm work and your Farmer page), Dairy, Market (Shop, Pantry, Creamery), Dairies and Progress. Settings and this Guide are at the bottom.",
            "The loop: cows make milk over time. Getting milked (at a stall or by a farmer's hand) empties their bottles and earns XP; reaching the daily quota is what pays coins. The milk goes into the dairy's tank, and farmers and the owner deliver it to other dairies and the Grand Creamery's weekly contract.",
            "Everything happens in Eorzea Milkers's own windows and your own chat log. Nothing is ever said in public chat, and nothing moves your character for you.",
            "This game is a work in progress: numbers change, and new things arrive in updates. The HUD tells you when there's an update.",
        }),
        new("Roles", new[]
        {
            "Cow: makes milk and gets milked. Paid for reaching a daily quota (a farmer or the owner sets its size), not per ounce. Cows drink, rest and have treats themselves; farmers feed them from the dairy's feed bin.",
            "Farmer: hand milks the dairy's cows (and gets a 25% share of what that milk is worth, 30% from farmer level 45), fills jugs from the dairy tank and delivers milk. Farmers can invite and give roles to others, farmer included.",
            "Bull: makes seed and gets milked, the way a cow makes milk: the same needs and care, vials instead of bottles (a tenth the size), seed grades, stalls and hand milking, a daily quota in seed, and a seed tank at the dairy. Seed comes in at 15% of milk speed at Grade C, up to 20% at S+. Seed gives a bit less xp than milk would, since bulls also level from breeding. With the Corruption plugin, bulls also breed their dairy's cows.",
            "Owner: runs the dairy. Marks out where it is, sets up stalls, invites farmers, cows and bulls, spends the dairy fund and posts buy orders. The owner can also play any mix of roles.",
            "You can hold several roles at once (a Cow + Farmer + Bull can take care of everyone). Add cow or bull to yourself any time on Dairy > Overview. Farmer is given by a farmer or the owner. Only the owner takes roles away.",
            "Each role has its own level: cow level (milk speed and shop unlocks), farmer level (unlocks bigger farmer tanks) and bull level. Adding or losing a role never loses its progress. The role pages has a page for each: cow level and milk quality, farmer level and tank, bull level and seed quality.",
            "You can belong to several dairies at once, and own one of your own as well. Invites show up on the Dairy tab; accepting one keeps your other dairies. Everything you do (milking, stalls, contracts, the tank) counts for the dairy you're playing in: walking into another of your dairies switches to it, or press Play here under Your dairies on the Dairy tab. Your levels and progress are yours and go with you to every dairy. In someone else's dairy and want your own? Press Start my own dairy on the Dairy tab.",
        }),
        new("Your cow", new[]
        {
            "Cows have four bonuses: Fed (a farmer or the owner feeds you from the dairy's feed bin, Herd Care > Feed; playing alone off the network, eat feed yourself), Watered (drink), Comfy (sit on furniture in your dairy: after 10 minutes seated you get 30 minutes of Comfy, and each further 10 minutes adds 30 more, up to an hour; the Comfy bar doesn't drain while you sit) and Happy (have a treat). Each use fills half its bar (30 minutes, up to a full hour), then the bar drains as the bonus wears off. In your dairy the bars drain half as fast, so the same top-up shows as 60 minutes there. Eating, drinking and treats each have a 15-minute cooldown. Using one again adds time, not a bigger bonus; better food and treats give bigger bonuses. All four at once makes you Contented, +10% more.",
            "Bonuses only count down while you're awake and playing; they wait while you're asleep or logged out. When one runs out you're just plain: it never slows you down or lowers your grade. The newest food or treat of a kind replaces the last.",
            "Care happens inside your dairy (Today page): Drink and Rest (sitting on furniture) are free; feed comes from your Pantry.",
            "Market > Pantry: everything you own. Use treats, sell back what you don't need for half price, or hide it from your lists. On the network your Fed bar comes from the dairy's feed bin, so feed in your pantry isn't eaten (farmers bake with wheat grain).",
            "Market > Shop: Feed & treats (treats, tonics, Pump drops, Udder balm, quality treats) and Level rewards. Bottles, bedding, wearables and the rest of the old upgrades now come free at their level (Market > Shop > Level rewards). Farmers still buy bigger farmer tanks.",
            "Quality treats (cream biscuit, golden alfalfa, star clover) raise your milk quality toward the next grade and make milk come 30% faster for 30 minutes. You can have one every hour.",
            "Clover, salt licks and cream clover also give a treat boost (+30% milk) on top of their Happy bonus. A farmer's baked treats from the dairy shelf are better than anything you buy. Pump drops: +25% milk and seed for 30 minutes. Udder balm: your next 3 hand milkings give 20% more. Second wind tonic: you tire 2 hours later today.",
            "Going away for a while? A Dry-off tonic (Shop, 150 coins) stops your grade slipping for 7 days. Take it anywhere, even outside your dairy.",
        }),
        new("Milk and bottles", new[]
        {
            "Milk fills your bottles while you're awake and logged in. No milk is made while you're logged out.",
            "Bottles: milk bottle 8 oz, cream jar 12, dairy jug 20, big jug 32, milk churn 48, Velvet churn 64. Bigger bottle upgrades make each one hold more; Extra bottle upgrades add more bottles (up to 4 in all, the 4th at level 19).",
            "Full bottles spoil after 72 hours (real time, logged in or not; 96 with the Cooling mist upgrade). Empty them before you log off for a while. Spoiled milk has to be dumped (HUD button) for no pay, and your milk tastes off (one grade lower) until care tasks, milkings and deliveries wear it off.",
        }),
        new("Getting milked", new[]
        {
            "Stalls: sit on one of your dairy's stalls (on the seat itself, not next to it) and the milking machine starts by itself and empties your bottles (and a bull's seed vials). Better stall tiers get more milk out.",
            "You don't have to get up between milkings: stay seated on the stall and it starts again by itself once half a bottle has refilled. Sitting on the stall counts as sitting in your dairy, so it builds Comfy and holds off dozing. Getting up stops a milking that's going.",
            "One catch: if you sit without doing anything for 30 minutes you start dozing, and a dozing cow's stall doesn't start a new milking. Move, emote or click anything in Eorzea Milkers and it starts again.",
            "Hand milking: a farmer of your dairy stands within 3 yalms, targets you and presses Milk on you (HUD > Herd Care). Their plugin does their milking emote or pose (/beesknees, a sit pose like /groundsit, or whatever they picked) and tells yours they mean you; someone just doing an emote nearby never milks you. Milk then flows until you're empty, or until they stop, step away or change target. If they started with the Milk button, their emote ends by itself once you're empty (with a quick pat); a sit pose is left for them to get up from. Today > Cow lets you allow any emote or sit pose (the default) or only the ones you list. It gets more out than a basic stall, counts toward your quota, and pays the farmer a share of what the milk is worth. You can turn off 'Farmers can start milking me without asking' on Today > Cow: then a farmer pressing Milk on you pops up a request, and milking (and their emote) only starts if you press Allow.",
            "A bar with more than an hour left is full enough: eating, drinking, grooming, the feeding bell and sitting for Comfy won't add to it until it's under an hour.",
            "In a dairy (yours, or a public one you're visiting) your Fed, Watered, Comfy and Happy bars drain half as fast, so your bonuses last longer while you stay.",
            "Guests: in someone else's public dairy you're a guest. You're milked on its stalls and cared for there (drink, Comfy, treats) like at home, with your own grade, pay and xp, and your milk (or seed) goes into that dairy's tank. Its farmers can hand milk, groom and feed you; hand milking always asks you to Allow first.",
            "Breeding with a bull from another dairy: when one is in a Corruption scene with you, you get a pop-up. If you Allow, their finish counts as a breeding; if not, it doesn't.",
            "Cow and bull are yours: you add or drop them on your role pages (Today > Cow, Today > Bull, Herd > Farmer) and they hold in every dairy you're in. Farmer is given by each dairy.",
            "Breeding a partner who, like you, is both cow and bull: whoever is doing the breeding targets the other and presses Breed on the HUD (or right-click them > Eorzea Milkers > Breed). Only that way counts; until one of you presses it, finishing doesn't breed either of you. Press it again to cancel. It's used up after you breed them. The Kitchen button moved off the HUD: it's on Herd > Farmer.",
            "Scanning: target another player with Eorzea Milkers (within 15 yalms) and press Scan on the HUD, or right-click them > Eorzea Milkers > Scan. You don't need to be a farmer or owner. It shows their milk and seed, every bonus speeding them up and the total, their bred bonus (who bred them, how much and for how long), pregnancy and Sire, when a cow can be bred again, and a bull's seed output. Only players in a dairy can be scanned, and each picks who can scan them in Settings: anyone (the default), their dairy only, or nobody.",
            "Farmers: the Herd Care button on the HUD (or Herd > Farmer) opens a window with your dairy's cows, how close they are, how full, and which emotes milk each of them.",
            "The Kitchen (Herd > Farmer > Kitchen): farmers fill their jugs from the dairy tank at the top, and bake with milk from their jugs, making butter and flour for their pantry and cheese, butter cookies, cream buns, honey cream cakes and velvet tarts for the dairy shelf. Better recipes unlock with farmer level.",
            "Farmer chores: Groom a cow or bull (in the Herd Care window) you're standing next to for 20 farmer xp; it adds 30 minutes to their Happy bar (once every 15 minutes each). Feed one a treat from the dairy shelf: press Feed and pick which treat (free for you, farmer xp, one treat per cow or bull every 15 minutes, whoever gives it). Clean a stall nobody is sitting on for 25 farmer xp; cows milked there get +10% speed and xp for an hour, and it can be freshened again once half an hour is left. Stalls never get worse than their tier.",
            "Feed from the shelf (Herd Care window): give a cow or bull something your dairy baked, straight from the dairy shelf. It's free for the farmer (nobody is paid for it) and earns 10 farmer xp; cows get the treat's Happy and milk boost, bulls get bull xp. Once every 15 minutes per cow or bull, and never to yourself.",
            "You're paid for your daily quota: reach it and claim it in Goals. A farmer or the owner picks its size (Light, Steady, Heavy or Devoted; bigger pays more). Your grade, your wearables and the dairy's level raise it (Market > Creamery > Your pay). Farmers have a separate farmer quota for milk delivered, which the owner sets. Daily goals pay 20 coins each, 40 more for all four. Everything daily resets at midnight Central time.",
        }),
        new("Grades and pay", new[]
        {
            "Everyone starts at Grade C. Milk quality builds as you get milked, fill goals and eat quality treats: B at 150, A at 800, S at 2,600 and S+ at 6,000.",
            "Quota pay by grade: C x1, B x1.15, A x1.3, S x1.45, S+ x1.6. Better grades also give more XP.",
            "There's no daily limit on pay: everything is paid in full. After 12 hours of making milk in a day you get tired, and milk (and its pay) comes in at half speed until the daily reset.",
            "Your grade also makes milking faster: B +10%, A +20%, S +35%, S+ +50%. (Your stall's tier decides how much is collected.)",
            "Milk itself has no grade: once it's in the dairy tank, any milk fills any order, contract or recipe.",
            "Your grade is your milk quality, and it stays there as long as you're milked. Go 3 days without being milked and it slips one grade (one more every 3 days after). One milking brings it right back. A Dry-off tonic stops it slipping while you're away.",
            "Hand milking by a farmer makes up one grade lost to slipping or off taste.",
        }),
        new("Levels and XP", new[]
        {
            "You get about 14 XP for every oz of milk you make (more at better grades), plus a bonus when you're emptied at a stall or by hand, plus daily goals and care.",
            "Max level is 100, and each level takes longer than the last: the first ten go by in an evening or two, the 90s take days each. Your cow level makes milk come 2% faster per level up to level 50 (about twice a new cow), and unlocks shop items and upgrades. Farmer and bull levels climb the same way.",
            "Farmers level separately: hand milking pay, filling jugs, deliveries and contracts give farmer XP, and farmer levels unlock bigger farmer tanks to buy in the Shop. Bulls level from making seed, being milked, care and breeding; their level speeds up seed the way cow level speeds up milk.",
            "Daily goals (Progress) reset at midnight Central time and pay coins and XP. Cows and farmers each have their own four; if you're both, you get both sets. Cow and bull goals: spend 30 minutes in a dairy, get milked at a stall, do 3 care tasks and get hand milked.",
        }),
        new("Staying awake", new[]
        {
            "Milk is for people who are actually playing. Moving or turning, targeting, emotes, sending a chat message and clicking in Eorzea Milkers's windows keep you awake. Moving the mouse alone doesn't.",
            "After 15 minutes with none of that you start dozing: milk at half speed, and stalls don't start a new milking.",
            "Resting on furniture in your dairy holds it off: you start dozing at 30 minutes and fall asleep at 45. Need bars and treat boosts wait while you're dozing, asleep or logged out.",
            "After 30 minutes you're asleep: milking stops and you make no milk or XP until you do anything at all.",
            "After 12 hours of making milk in one day you're tired: half speed until the next day (14 hours with the Steady producer upgrade).",
        }),
        new("Starting a dairy (owners)", new[]
        {
            "1. Go to where you want your dairy: a field, a quiet corner of a city, or a room in any house.",
            "2. Dairy tab: give it a name, choose public or private, whether to show your name, and what you play as.",
            "3. Stand in the middle and press Start marking here. A box is drawn on the ground: pink sides, blue front. Use Width, Length and Turn (and Face my way) until it covers your dairy. Height is how far up or down still counts.",
            "4. Press Register my dairy. Boxes can't overlap another dairy in the same zone and world.",
            "5. Stalls: sit on a seat inside the box (or your private place) and press Add on Dairy > Stalls. The first stall is free; after that there's no limit by level, but each new one costs more from the dairy fund (500, 2,000, 4,500, 8,000...).",
            "6. Invite people: right-click a player > Eorzea Milkers > Invite as cow, bull or farmer, or target them and use the Members panel. For people already in your dairy, the same menu (or Manage members) gives them more roles.",
            "You can move or resize the box any time, even to a different zone: Dairy > Listing > Move or resize. To move a stall, sit on its new seat and press Move here on Dairy > Stalls; it keeps its name and tier. Stalls left outside a moved box stop working until you move them.",
            "You can also give the dairy a private place just for members, like your home or apartment (Dairy > Listing > Private place): use the whole house, or draw borders around part of it. Members are in the dairy there, so milking, stalls, resting and chores all work; visitors, the directory and deliveries only ever see the public area.",
        }),
        new("Your dairy", new[]
        {
            "Dairy > Overview: who's in it, invites, and the members summary. Manage members opens a window with search, filters, role changes and removal.",
            "Dairy level: members earn dairy XP by milking and caring inside it (up to 300 each a day), and from filled buy orders and contracts. Higher levels give better stall tiers (3 from level 5, 4 from level 12), 2% more quota pay per level, and put the dairy higher in the Dairies list.",
            "Dairy fund: members donate, milk your farmers drop off at other dairies pays 2 coins an oz into it, and the weekly contract adds more. The owner spends it on stalls, stall tiers, feed for the feed bin (Dairy > Feed & pump) and the Velvet pump (everyone's milk and seed +15% for 2 hours).",
            "Dairy tank: every milking inside the dairy adds that milk to the tank, by grade. Farmers and the owner fill jugs from it.",
        }),
        new("Jugs and deliveries", new[]
        {
            "Farmers and the owner fill jugs from the dairy tank (top of the Kitchen window: Herd > Farmer > Kitchen), inside the dairy, up to what their farmer tank holds: a 32 oz milk pail at first, up to a 400 oz grand tanker (Market > Shop > Farmer tank, unlocked by farmer level). Filling takes time: 1 oz a second (twice as fast with Churn oil), and you have to stay in the dairy.",
            "Drop-offs (Market > Creamery > Deliveries): take milk in your jugs to any other dairy and leave it in their tank, no order needed. The Grand Creamery pays you, your own dairy fund gets 2 coins an oz, and you get farmer XP. You can deliver 30 oz a day to other dairies at farmer level 1, plus 5 a level, in up to 10 deliveries a day, and drop off at each dairy once a day (filling a buy order counts as a delivery too).",
            "Deliveries: other dairies' buy orders (public or private) are listed on Market > Creamery. Fill them from inside your own dairy (or in person) with milk in your jugs; it goes straight into the buying dairy's tank.",
            "Any owner, public or private, can post buy orders (Market > Creamery > Deliveries): the coins are held until a delivery fills it or you cancel.",
        }),
        new("Weekly contract", new[]
        {
            "Every week (Monday 00:00 UTC) the Grand Creamery gives each dairy a contract: 600 oz at dairy level 1, plus 150 oz a level (up to 3,500). Any milk counts. Fill it for the full reward; if it isn't full by Monday, your dairy still gets the share it delivered (half the milk, half the reward).",
            "Farmers and the owner fill jugs at the dairy, take them to the Grand Creamery (Dairy > Contract shows where, with Show on map) and ship them there.",
            "When it's full the dairy gets dairy XP (which counts on the weekly leaderboard) and coins for the fund, and everyone is told.",
        }),
        new("Dairies and the network", new[]
        {
            "Dairies tab: how many people are playing right now, every dairy (public ones show where they are), and this week's top dairies.",
            "The feeding bell (Herd Care, farmers and the owner, in the dairy, once an hour): everyone in the dairy hears it, and each cow and bull picks one bar to fill right up, plus a 20 minute boost.",
            "Weekly contract: 600 oz at dairy level 1, growing 150 oz a level up to 3,500 oz. Weekend milk contest (Saturday and Sunday, UTC): milk pumped into the tank also fills the contract, so it counts twice; the Contract card shows the top pumpers and last weekend's champion.",
            "Details (Dairies tab): a dairy's address, owner, what it's looking for and description, its tank (milk and seed) and stalls, and who's there now with roles and levels, members or visiting, and whether each cow can be bred. Players who don't share (scan setting) are only counted.",
            "Visit: with the Lifestream plugin it travels to a dairy in a house, or to the ward of an outdoor dairy in a residential district (and flags the spot on your map). Other outdoor dairies are flagged on your map. Without Lifestream it copies the address.",
            "You can turn the network off in Settings; the game then runs on its own.",
        }),
        new("Commands and settings", new[]
        {
            "/milkers opens the main window. /milkers hud shows or hides the HUD. /milkers guide opens this guide. /milkers tutorial runs the walkthrough again.",
            "Settings: HUD, chat echo, sounds and volume, the network, who can scan you.",
            "Your save is protected: editing the plugin's files is caught, and a save that fails its check is kept aside instead of lost.",
            "Before you play, you set up your save backup once: back up and confirm your recovery code (or choose to play without the network). Backup and restore (Settings, or /milkers restore): your save is backed up on the server every few minutes. On a new PC or install, get it all back (with your dairy) with your recovery code, or with a restore key from #restore-save in the Discord (it verifies your character on the Lodestone). The plugin can also verify on the Lodestone itself.",
        }),
    };

    public GuideWindow(Plugin plugin, Fonts fonts) : base("How to play Eorzea Milkers##VelvetDairyGuide", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        Size = new Vector2(820, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560, 360), MaximumSize = new Vector2(1600, 1200) };
    }

    public void Open(string? title = null)
    {
        if (title != null)
        {
            var i = Array.FindIndex(Topics, t => t.Title == title);
            if (i >= 0) topic = i;
        }

        IsOpen = true;
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        // Topics on the left
        ImGui.BeginChild("##guidetopics", new Vector2(210, 0), true);
        for (var i = 0; i < Topics.Length; i++)
            if (ImGui.Selectable($"{Topics[i].Title}##guide{i}", topic == i))
                topic = i;
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (Ui.Button("##guidediscord", "Join the Discord", new Vector2(ImGui.GetContentRegionAvail().X, 30), "blue", true,
                "Questions, finding a dairy, update news and testing builds (18+). Opens in your browser."))
            Plugin.OpenDiscord();
        ImGui.EndChild();
        ImGui.SameLine();

        // The topic
        ImGui.BeginChild("##guidebody", Vector2.Zero, true);
        var t = Topics[Math.Clamp(topic, 0, Topics.Length - 1)];
        using (fonts.For(fonts.Heading, t.Title)!.Push()) ImGui.TextColored(Theme.RoseDeep, t.Title);
        ImGui.Spacing();
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        foreach (var p in t.Paragraphs)
        {
            ImGui.TextWrapped(Units.Text(p));
            ImGui.Spacing();
        }

        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (topic > 0 && ImGui.Button("< Back##guideback")) topic--;
        if (topic > 0) ImGui.SameLine();
        if (topic < Topics.Length - 1 && ImGui.Button($"Next: {Topics[topic + 1].Title} >##guidenext")) topic++;
        ImGui.EndChild();
    }
}
