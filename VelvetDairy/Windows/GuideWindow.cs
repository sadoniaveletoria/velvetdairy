using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VelvetDairy.Windows;

/// <summary>
/// How to play: every part of Velvet Dairy explained, one topic at a time.
/// Opened from the "?" in the main window, "/vdairy guide", or the end of the first-run walkthrough.
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
            "Velvet Dairy is a dairy life game for adults (18+). You play a cow, a farmer, a bull or a dairy owner, alone or with friends.",
            "The loop: cows make milk over time. Getting milked (at a stall or by a farmer's hand) empties their bottles, and the dairy pays them for every ounce. The milk goes into the dairy's tank, and farmers and the owner deliver it to customers, other dairies and the Grand Creamery's weekly contract.",
            "Everything happens in Velvet Dairy's own windows and your own chat log. Nothing is ever said in public chat, and nothing moves your character for you.",
            "This game is a work in progress: numbers change, and new things arrive in updates. The HUD tells you when there's an update.",
        }),
        new("Roles", new[]
        {
            "Cow: makes milk and gets milked. Paid per ounce at milking time. Cows treat themselves (food, water, rest, treats) for faster milk.",
            "Farmer: hand milks the dairy's cows (and gets a 25% share of what the cow is paid, on top), fills jugs from the dairy tank and delivers milk. Farmers can invite and give roles to others, farmer included.",
            "Bull: a role only for now. More is coming in new updates.",
            "Owner: runs the dairy. Marks out where it is, sets up stalls, invites farmers, cows and bulls, spends the dairy fund and posts buy orders. The owner can also play any mix of roles.",
            "You can hold several roles at once (a Cow + Farmer + Bull can take care of everyone). Add cow or bull to yourself any time on Dairy > Overview. Farmer is given by a farmer or the owner. Only the owner takes roles away.",
            "Each role has its own level: cow level (milk speed and shop unlocks), farmer level (unlocks bigger farmer tanks) and bull level. Adding or losing a role never loses its progress. The Roles tab has a page for each: cow level and milk quality, farmer level and tank, bull level (seed quality is coming).",
            "You belong to one dairy at a time. Invites show up on the Dairy tab; accepting one leaves your old dairy.",
        }),
        new("Your cow", new[]
        {
            "Cows have four bonuses: Fed (eat food), Watered (drink), Comfy (rest on furniture in your dairy) and Happy (have a treat). Each use fills half its bar (30 minutes, up to a full hour), then the bar drains as the bonus wears off. Eating, drinking, resting and treats each have a 15-minute cooldown. Using one again adds time, not a bigger bonus; better food and treats give bigger bonuses. All four at once makes you Contented, +10% more.",
            "Bonuses only count down while you're awake and playing; they wait while you're asleep or logged out. When one runs out you're just plain: it never slows you down or lowers your grade. The newest food or treat of a kind replaces the last.",
            "Care happens inside your dairy (Barn tab): Drink and Rest (sitting on furniture) are free; feed comes from your Pantry.",
            "Pantry tab: everything you own. Eat it, sell back what you don't need for half price, or hide it from your lists.",
            "Shop tab: Feed & treats (everyday feed, quality treats), Upgrades (comfort, care and things to wear) and Bottles. Cow levels unlock new feed and cow upgrades all the way to level 30: faster stall and hand milking, deeper rest, longer before you tire or spoil, faster quality, and wearables that raise your pay.",
            "Quality treats (cream biscuit, golden alfalfa, star clover) raise your milk quality toward the next grade and make milk come 30% faster for 30 minutes. You can have one every hour.",
            "Clover, salt licks, cream clover, the Velvet blend and Golden mash also give a treat boost (+30% milk) on top of their bonus.",
            "Going away for a while? A Dry-off tonic (Shop, 150 coins) stops your grade slipping for 7 days. Take it anywhere, even outside your dairy.",
        }),
        new("Milk and bottles", new[]
        {
            "Milk fills your bottles while you're awake and logged in. No milk is made while you're logged out.",
            "Bottles: milk bottle 8 oz, cream jar 12, dairy jug 20, big jug 32, milk churn 48. Bigger bottle upgrades make each one hold more; Extra bottle upgrades add more bottles (up to 5, the 5th at level 25).",
            "Full bottles spoil after 72 hours (real time, logged in or not; 96 with the Cooling mist upgrade). Empty them before you log off for a while. Spoiled milk has to be dumped (HUD button) for no pay, and your milk tastes off (one grade lower) until care tasks, milkings and deliveries wear it off.",
        }),
        new("Getting milked", new[]
        {
            "Stalls: sit on one of your dairy's stalls (on the seat itself, not next to it) and the milking machine empties your bottles. Better stall tiers get more milk out.",
            "Hand milking: a farmer of your dairy stands within 3 yalms, targets you and holds a looping emote, like /beesknees (one-shot gestures like /pet can't be seen by the plugin). Milk then flows until you're empty, or until they stop, step away or change target. If they started with the Milk button, their emote ends by itself once you're empty (with a quick pat). Roles > Cow lets you allow any looping emote (the default) or only the ones you list. It gets more out than a basic stall and pays the farmer a share on top of your pay. You can turn off 'Farmers can start milking me without asking' on Roles > Cow and start it yourself instead.",
            "Farmers: Roles > Farmer > Hand milk a cow lists your dairy's cows, how close they are, how full, and which emotes milk each of them.",
            "Farmer chores (Roles > Farmer): Groom a cow you're standing next to for 20 farmer xp; it adds 30 minutes to their Happy bar (once every 15 minutes per cow). Clean a stall nobody is sitting on for 25 farmer xp; cows milked there get +10% speed and xp for an hour, and it can be freshened again once half an hour is left. Stalls never get worse than their tier.",
            "You're paid the moment you're milked: per ounce, by your grade, plus 2% for each dairy level and your looks bonus (Creamery > Your pay).",
        }),
        new("Grades and pay", new[]
        {
            "Everyone starts at Grade C. Milk quality builds as you get milked, fill goals and eat quality treats: B at 150, A at 800, S at 2,600 and S+ at 6,000.",
            "Pay per ounce: C 7, B 10, A 15, S 21, S+ 30 coins. Better grades also give more XP.",
            "Your grade is your milk quality, and it stays there as long as you're milked. Go 3 days without being milked and it slips one grade (one more every 3 days after). One milking brings it right back. A Dry-off tonic stops it slipping while you're away.",
            "Hand milking by a farmer makes up one grade lost to slipping or off taste.",
        }),
        new("Levels and XP", new[]
        {
            "You get +1 XP for every 0.2 oz of milk you make (more at better grades), plus a bonus when you're emptied at a stall or by hand, plus daily goals, orders and care.",
            "Max level is 100, and each level takes longer than the last: the first ten go by in an evening or two, the 90s take days each. Your cow level makes milk come 4% faster per level up to level 50 (about three times a new cow), and unlocks shop items and upgrades. Farmer and bull levels climb the same way.",
            "Farmers level separately: hand milking pay, filling jugs, deliveries and contracts give farmer XP, and farmer levels unlock bigger farmer tanks to buy in the Shop. Bulls have their own level too, for things coming later.",
            "Daily goals (Progress tab) reset each day and pay coins and XP.",
        }),
        new("Staying awake", new[]
        {
            "Milk is for people who are actually playing. Moving or turning, targeting, emotes, sending a chat message and clicking in Velvet Dairy's windows keep you awake. Moving the mouse alone doesn't.",
            "After 15 minutes with none of that you start dozing: milk at half speed, and stalls don't start a new milking.",
            "After 30 minutes you're asleep: milking stops and you make no milk or XP until you do anything at all.",
            "After 12 hours of making milk in one day you're tired: half speed until the next day (14 hours with the Steady producer upgrade).",
        }),
        new("Starting a dairy (owners)", new[]
        {
            "1. Go to where you want your dairy: a field, a quiet corner of a city, or a room in any house.",
            "2. Dairy tab: give it a name, choose public or private, whether to show your name, and what you play as.",
            "3. Stand in the middle and press Start marking here. A box is drawn on the ground: pink sides, blue front. Use Width, Length and Turn (and Face my way) until it covers your dairy. Height is how far up or down still counts.",
            "4. Press Register my dairy. Boxes can't overlap another dairy in the same zone and world.",
            "5. Stalls: sit on a seat inside the box and press Add on Dairy > Stalls. The first stall is free; more come from the dairy fund and dairy levels.",
            "6. Invite people: right-click a player > Velvet Dairy > Invite as cow, bull or farmer, or target them and use the Members panel. For people already in your dairy, the same menu (or Manage members) gives them more roles.",
            "You can move or resize the box any time, even to a different zone: Dairy > Listing > Move or resize. To move a stall, sit on its new seat and press Move here on Dairy > Stalls; it keeps its name and tier. Stalls left outside a moved box stop working until you move them.",
        }),
        new("Your dairy", new[]
        {
            "Dairy > Overview: who's in it, invites, and the members summary. Manage members opens a window with search, filters, role changes and removal.",
            "Dairy level: members earn dairy XP by milking and caring inside it (up to 300 each a day), and from filled buy orders and contracts. Higher levels give more stall slots (2 up to 8), better stall tiers (3 from level 5, 4 from level 12), 2% more pay per level, and put the dairy higher in the Dairies list.",
            "Dairy fund: anyone in the dairy can donate coins. The owner spends them on new stalls and stall upgrades.",
            "Dairy tank: every milking inside the dairy adds that milk to the tank, by grade. Farmers and the owner fill jugs from it.",
            "The owner HUD shows who's online, who's in the dairy, each cow's milk and grade, the contract and the tank.",
        }),
        new("Jugs and deliveries", new[]
        {
            "Farmers and the owner fill jugs from the dairy tank (Creamery > Jugs), inside the dairy, up to what their farmer tank holds: a 32 oz milk pail at first, up to a 256 oz tanker cart (Shop > Farmer tank, unlocked by farmer level).",
            "Customer orders (Creamery tab): the creamery's customers want milk of a grade. Deliver from your jugs for coins and XP.",
            "Deliveries: other public dairies' buy orders and two creamery visitor orders a day per dairy. Go there in person and deliver from your jugs. Milk from buy orders goes into the buying dairy's tank.",
            "Owners of public dairies can post buy orders (Creamery > Deliveries): the coins are held until a delivery fills it or you cancel.",
        }),
        new("Weekly contract", new[]
        {
            "Every week (Monday 00:00 UTC) the Grand Creamery gives each dairy a contract: about 200 oz at dairy level 1, plus 40 oz a level, with better grades asked for at higher levels.",
            "Farmers and the owner fill jugs at the dairy, take them to the Grand Creamery (Dairy > Contract shows where, with Show on map) and ship them there.",
            "When it's full the dairy gets dairy XP (which counts on the weekly leaderboard) and coins for the fund, and everyone is told.",
        }),
        new("Dairies and the network", new[]
        {
            "Dairies tab: how many people are playing right now, every dairy (public ones show where they are), and this week's top dairies.",
            "Visit: flags an outdoor dairy on your map, or travels to an in-house dairy with the Lifestream plugin (without it, it copies the address).",
            "You can turn the network off in Settings; the game then runs on its own.",
        }),
        new("Commands and settings", new[]
        {
            "/vdairy opens the main window. /vdairy hud shows or hides the HUD. /vdairy guide opens this guide. /vdairy tutorial runs the walkthrough again.",
            "Settings: HUD, chat echo, sounds and volume, the network, the owner HUD.",
            "Your save is protected; editing the plugin's files resets progress.",
        }),
    };

    public GuideWindow(Plugin plugin, Fonts fonts) : base("How to play Velvet Dairy##VelvetDairyGuide", ImGuiWindowFlags.NoCollapse)
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
            ImGui.TextWrapped(p);
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
