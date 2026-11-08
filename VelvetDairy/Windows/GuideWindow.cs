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
            "Cow: makes milk and gets milked. Paid per ounce at milking time. Cows care for themselves (food, water, comfort) to keep milk quality up.",
            "Farmer: hand milks the dairy's cows (and gets a 25% share of what the cow is paid, on top), fills jugs from the dairy tank and delivers milk. Farmers can invite cows and bulls.",
            "Bull: a role only for now. More is coming in new updates.",
            "Owner: runs the dairy. Marks out where it is, sets up stalls, invites farmers, cows and bulls, spends the dairy fund and posts buy orders. The owner also picks a play role for themselves: cow, farmer, bull or none.",
            "You belong to one dairy at a time. Invites show up on the Dairy tab; accepting one leaves your old dairy.",
        }),
        new("Your cow", new[]
        {
            "Cows have four needs: hunger, thirst, comfort and mood. Keep them up and your milk comes faster and stays good. They drop slowly while you play, and gently while you're logged out (never below 20).",
            "Care happens inside your dairy (Barn tab): Drink and Stretch are free; feed comes from your Pantry.",
            "Pantry tab: everything you own. Eat it, sell back what you don't need for half price, or hide it from your lists.",
            "Shop tab: Feed & treats (everyday feed, quality treats), Upgrades (comfort, care and things to wear) and Bottles.",
            "Quality treats (cream biscuit, golden alfalfa, star clover) raise your milk quality toward the next grade. You can have one every 4 hours.",
            "Clover, salt licks and the Velvet blend give a short boost that makes milk come in faster.",
        }),
        new("Milk and bottles", new[]
        {
            "Milk fills your bottles while you're awake and logged in. No milk is made while you're logged out.",
            "Bottles: milk bottle 8 oz, cream jar 12, dairy jug 20, big jug 32, milk churn 48. Bigger bottle upgrades make each one hold more; Extra bottle upgrades add more bottles (up to 5, the 5th at level 25).",
            "Full bottles spoil after 72 hours (real time, logged in or not). Empty them before you log off for a while. Spoiled milk has to be dumped (HUD button) for no pay, and it lowers your grade until you care for yourself, get milked and the dairy delivers again.",
        }),
        new("Getting milked", new[]
        {
            "Stalls: sit on one of your dairy's stalls (on the seat itself, not next to it) and the milking machine empties your bottles. Better stall tiers get more milk out.",
            "Hand milking: a farmer of your dairy stands close, targets you and uses a milking emote. It gets more out than a basic stall and pays the farmer a share on top of your pay.",
            "You're paid the moment you're milked: per ounce, by your grade, plus 2% for each dairy level and your looks bonus (Creamery > Your pay).",
        }),
        new("Grades and pay", new[]
        {
            "Everyone starts at Grade C. Milk quality builds as you get milked, fill goals and eat quality treats: B at 150, A at 800, S at 2,600 and S+ at 6,000.",
            "Pay per ounce: C 5, B 7, A 10, S 14, S+ 20 coins. Better grades also give more XP.",
            "Your care decides the best grade you can make right now; let your needs drop and your grade drops with them.",
        }),
        new("Levels and XP", new[]
        {
            "You get +1 XP for every 0.1 oz of milk you make (more at better grades), plus a bonus when you're emptied at a stall or by hand, plus daily goals, orders and care.",
            "Each level needs 30 more XP than the last. Levels make your milk come faster and unlock shop items and upgrades. Max level is 30.",
            "Daily goals (Progress tab) reset each day and pay coins and XP.",
        }),
        new("Staying awake", new[]
        {
            "Milk is for people who are actually playing. Moving or turning, targeting, emotes, sending a chat message and clicking in Velvet Dairy's windows keep you awake. Moving the mouse alone doesn't.",
            "After 15 minutes with none of that you start dozing: milk at half speed, and stalls don't start a new milking.",
            "After 30 minutes you're asleep: milking stops and you make no milk or XP until you do anything at all.",
            "After 12 hours of making milk in one day you're tired: half speed until the next day.",
        }),
        new("Starting a dairy (owners)", new[]
        {
            "1. Go to where you want your dairy: a field, a quiet corner of a city, or a room in any house.",
            "2. Dairy tab: give it a name, choose public or private, whether to show your name, and what you play as.",
            "3. Stand in the middle and press Start marking here. A box is drawn on the ground: pink sides, blue front. Use Width, Length and Turn (and Face my way) until it covers your dairy. Height is how far up or down still counts.",
            "4. Press Register my dairy. Boxes can't overlap another dairy in the same zone and world.",
            "5. Stalls: sit on a seat inside the box and press Add on Dairy > Stalls. The first stall is free; more come from the dairy fund and dairy levels.",
            "6. Invite people: right-click a player > Velvet Dairy > Invite as cow, bull or farmer, or target them and use the Members panel.",
            "You can move or resize the box any time: Dairy > Listing > Move or resize.",
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
            "Farmers and the owner fill jugs from the dairy tank (Creamery > Jugs), up to 192 oz at a time, inside the dairy.",
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
