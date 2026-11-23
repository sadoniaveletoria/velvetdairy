using System;
using System.Collections.Generic;
using System.Linq;

namespace VelvetDairy.Game;

public sealed record Grade(string Name, float Price, string Blurb);

public static class Grades
{
    public static readonly IReadOnlyList<Grade> All = new[]
    {
        new Grade("C", 6.0f, "Thin"),
        new Grade("B", 8.0f, "Good"),
        new Grade("A", 10.0f, "Rich"),
        new Grade("S", 12.0f, "Cream"),
        new Grade("S+", 14.0f, "Velvet"),
    };

    public static int Count => All.Count;
    public static string Name(int i) => i >= 0 && i < All.Count ? All[i].Name : "-";

    /// <summary>Days without being milked before your grade slips one step (and one more every this many days after).</summary>
    public const double FreshDays = 3;

    /// <summary>When freshness is counted from: your last milking, or the end of a Dry-off tonic if that's later.</summary>
    public static DateTime FreshSince(CowState c) => FreshSince(c.LastMilkedUtc, c.FreshHoldUntilUtc);

    public static DateTime FreshSince(DateTime? lastMilked, DateTime holdUntil)
    {
        var now = DateTime.UtcNow;
        var since = lastMilked ?? now;
        var hold = holdUntil > now ? now : holdUntil;
        return hold > since ? hold : since;
    }

    /// <summary>Grade steps lost to going too long without being milked.</summary>
    public static int Slip(CowState c) => Slip(c.LastMilkedUtc, c.FreshHoldUntilUtc);

    public static int Slip(DateTime? lastMilked, DateTime holdUntil)
    {
        var days = (DateTime.UtcNow - FreshSince(lastMilked, holdUntil)).TotalDays;
        return days < FreshDays ? 0 : 1 + (int)((days - FreshDays) / FreshDays);
    }

    /// <summary>When the next slip happens (null while a tonic holds it).</summary>
    public static DateTime? NextSlipUtc(CowState c) => NextSlipUtc(c.LastMilkedUtc, c.FreshHoldUntilUtc);

    public static DateTime? NextSlipUtc(DateTime? lastMilked, DateTime holdUntil)
    {
        if (holdUntil > DateTime.UtcNow) return null;
        return FreshSince(lastMilked, holdUntil).AddDays(FreshDays * (Slip(lastMilked, holdUntil) + 1));
    }

    /// <summary>
    /// Milk quality score needed before your milk can reach each grade. Everyone starts at C.
    /// You earn quality from every ounce you're milked (more by hand), creamery deliveries, and quality treats.
    /// </summary>
    public static readonly float[] QualityNeed = { 0, 150, 800, 2600, 6000 };

    public const float QualityPerOz = 5f;
    public const float QualityPerOzHand = 7.5f;
    public const float QualityPerOrder = 10f;

    /// <summary>
    /// Quality treats share one cooldown so they can't be spammed: at most 6 a day, usually 3-4.
    /// Over three full days that's roughly 600 quality from treats on top of ~2,000 from milking and ~240 from deliveries.
    /// </summary>
    public const double QualityTreatHours = 1;

    /// <summary>Best grade your milk quality allows.</summary>
    public static int Cap(float quality)
    {
        var g = 0;
        for (var i = 0; i < QualityNeed.Length; i++)
            if (quality >= QualityNeed[i]) g = i;
        return g;
    }

    /// <summary>
    /// Your grade comes from your milk quality (earned by being milked). Going days without milking slips it, and the off taste
    /// from dumped milk takes one step off while it lasts. Hand milking makes up one lost step, but never goes past your quality.
    /// Needs don't change your grade; they only add milk speed.
    /// </summary>
    public static int For(CowState c, bool hand, float quality) => For(Cap(quality), Slip(c), c.Taint > 0, hand);

    /// <summary>A bull's seed grade: the same rules, from seed quality, seed freshness and seed off taste.</summary>
    public static int SeedFor(CowState c, bool hand, float seedQuality) =>
        For(Cap(seedQuality), Slip(c.SeedLastMilkedUtc, c.FreshHoldUntilUtc), c.SeedTaint > 0, hand);

    public static int For(int cap, int slip, bool taint, bool hand) => Math.Clamp(cap - slip - (taint ? 1 : 0) + (hand ? 1 : 0), 0, cap);
}

/// <summary>A feed or treat you buy, keep in the pantry and use inside your dairy.</summary>
public sealed record FeedItem(
    string Id, string Name, string Icon, int Price, int Unlock, string Desc,
    float Fed = 0, float Water = 0, float Happy = 0, int BoostMinutes = 0, float Quality = 0, int HoldDays = 0, bool Baked = false,
    // Bull and cow-and-bull foods: who can buy it ("" anyone, "bull", "both" = a cow who's also a bull), and what it does
    string Role = "", float XpBoost = 0, int XpBoostMinutes = 0, int RestoreBreeds = 0, float NextSeedBonus = 0, bool StudTonic = false, int RestHours = 0,
    // Pump drops: milk and seed faster for this long. Udder balm: this many hand milkings give more. Churn oil: jugs fill faster.
    int DropsMinutes = 0, int BalmMilkings = 0, int OilMinutes = 0);

/// <summary>Something you buy once or in levels.</summary>
public sealed record Upgrade(string Id, string Name, string Icon, int[] Prices, int[] Unlocks, string Desc, bool Cosmetic = false, bool Farmer = false, bool Bull = false, bool Cow = false)
{
    public int MaxLevel => Prices.Length;

    /// <summary>Since 0.9.99 cow, bull and shared upgrades are free rewards at their level; only farmer tanks (and the
    /// solo stall kit) are still bought.</summary>
    public bool LevelReward => !Farmer && Id != "stallkit";
}

public static class Catalog
{
    // Each need is a milk bonus that lasts 30 minutes; getting it again sets it back to 30. Each use has a 15-minute cooldown.
    // Food sets Fed, the trough or a mineral block sets Watered, resting sets Comfy, treats set Happy.
    // Using one again adds time, not more bonus; the newest one of a kind sets the bonus. All four at once: Contented on top.
    // ----- Making things (farmers): ingredients go in the pantry; they're only used to make other things -----
    public sealed record Ingredient(string Id, string Name, string Icon, string Desc);

    /// <summary>Turn milk from your jugs into something. MilkOz of any grade from MinGrade up, at FarmerLevel or higher.</summary>
    /// <summary>Uses/UsesCount: an ingredient from your pantry each one needs. ToShelf: what's made goes on the dairy shelf, not your pantry.</summary>
    public sealed record Recipe(string Id, string Name, string Makes, int Amount, int MilkOz, int MinGrade, int FarmerLevel, int Xp, int Seconds, string Desc,
        string Uses = "", int UsesCount = 0, bool ToShelf = false, string Uses2 = "", int UsesCount2 = 0)
    {
        /// <summary>Every ingredient this takes per batch (one or two kinds).</summary>
        public IEnumerable<(string Id, int Count)> Needs()
        {
            if (UsesCount > 0) yield return (Uses, UsesCount);
            if (UsesCount2 > 0) yield return (Uses2, UsesCount2);
        }
    }

    /// <summary>Each one in a batch takes a quarter longer than the one before, so big batches take much longer.</summary>
    public static float BatchSeconds(Recipe r, int index) => r.Seconds * (1f + 0.25f * index);

    public static float TotalSeconds(Recipe r, int count, int from = 0)
    {
        var t = 0f;
        for (var i = from; i < count; i++) t += BatchSeconds(r, i);
        return t;
    }

    public static readonly IReadOnlyList<Ingredient> Ingredients = new[]
    {
        new Ingredient("butter", "Butter", "biscuit", "Churned from milk. An ingredient for butter cookies and cream buns."),
        new Ingredient("flour", "Flour", "grain", "Milled from wheat grain. An ingredient for cream buns."),
    };

    public static readonly IReadOnlyList<Recipe> Recipes = new[]
    {
        new Recipe("butter", "Butter", "butter", 1, 16, 0, 1, 12, 30, "Churn 16 oz of milk from your jugs into a pat of butter."),
        new Recipe("flour", "Flour", "flour", 1, 0, 0, 6, 6, 20, "Mill 2 wheat grain (from the Shop) into flour for the dairy pantry.",
            Uses: "hay", UsesCount: 2),
        new Recipe("cheese", "Cheese", "cheese", 2, 16, 0, 1, 10, 40,
            "16 oz of milk makes 2 cheese for the dairy shelf: the simplest treat, nothing else needed. Members buy them; you get 80% of the price.",
            ToShelf: true),
        new Recipe("cookies", "Butter cookies", "cookies", 3, 8, 0, 3, 15, 45,
            "1 butter and 8 oz of milk make 3 butter cookies for the dairy shelf. Members buy them; you get 80% of the price.",
            Uses: "butter", UsesCount: 1, ToShelf: true),
        new Recipe("buns", "Cream buns", "buns", 2, 12, 0, 10, 25, 75,
            "1 flour, 1 butter and 12 oz of milk make 2 cream buns for the dairy shelf: a bigger treat than cookies. Members buy them; you get 80% of the price.",
            Uses: "flour", UsesCount: 1, ToShelf: true, Uses2: "butter", UsesCount2: 1),
        new Recipe("cake", "Honey cream cake", "cake", 2, 16, 0, 35, 40, 100,
            "1 flour, 2 butter and 16 oz of milk make 2 honey cream cakes for the dairy shelf: a big treat. Members buy them; you get 80% of the price.",
            Uses: "flour", UsesCount: 1, ToShelf: true, Uses2: "butter", UsesCount2: 2),
        new Recipe("tart", "Velvet tart", "tart", 2, 24, 0, 70, 60, 130,
            "2 flour, 2 butter and 24 oz of milk make 2 velvet tarts for the dairy shelf: the best treat there is. Members buy them; you get 80% of the price.",
            Uses: "flour", UsesCount: 2, ToShelf: true, Uses2: "butter", UsesCount2: 2),
    };

    public static Ingredient? IngredientById(string id) => Ingredients.FirstOrDefault(i => i.Id == id);

    /// <summary>The name of something you can make: an ingredient or a baked treat.</summary>
    public static string MadeName(string id) => IngredientById(id)?.Name ?? FeedById(id)?.Name ?? id;
    public static string MadeIcon(string id) => IngredientById(id)?.Icon ?? FeedById(id)?.Icon ?? "bottle";
    public static Recipe? RecipeById(string id) => Recipes.FirstOrDefault(r => r.Id == id);

    /// <summary>Treats are baked; ingredients (butter, flour) are made.</summary>
    // Cookies and buns are baked; butter, flour and cheese are made
    public static bool IsBaked(Recipe r) => r.Id is "cookies" or "buns" or "cake" or "tart";
    public static string Verb(Recipe r) => IsBaked(r) ? "Bake" : "Make";
    public static string Doing(Recipe r) => IsBaked(r) ? "Baking" : "Making";

    public static readonly IReadOnlyList<FeedItem> Feed = new[]
    {
        new FeedItem("hay", "Wheat grain", "grain", 4, 1, "Plain and filling: the dairy's feed bin always has it. Farmers buy it to mill into flour in the Kitchen.", Fed: 0.08f),
        new FeedItem("grain", "Sweet grain", "grain", 12, 2, "A better meal, for the dairy's feed bin.", Fed: 0.12f),
        new FeedItem("clover", "Fresh clover", "clover", 15, 3, "A treat: a little milk bonus for a while (a farmer's baking is better).", Happy: 0.07f, BoostMinutes: 15),
        new FeedItem("salt", "Salt lick", "salt", 20, 4, "A salty treat: a little milk bonus for a while.", Happy: 0.06f, BoostMinutes: 10),
        new FeedItem("premium", "Velvet blend", "premium", 45, 8, "A rich feed for the dairy's feed bin.", Fed: 0.20f),
        new FeedItem("honeyoats", "Honey oats", "grain", 18, 7, "Sweet and filling, for the dairy's feed bin.", Fed: 0.15f),
        new FeedItem("mineral", "Mineral block", "salt", 22, 11, "Better than plain water.", Water: 0.10f),
        new FeedItem("creamclover", "Cream clover", "clover", 35, 13, "A lush treat: a bigger milk bonus for a while.", Happy: 0.09f, BoostMinutes: 45),
        new FeedItem("goldenmash", "Golden mash", "premium", 70, 22, "A feast: the best feed for the dairy's feed bin.", Fed: 0.25f),
        // Quality treats: raise your milk quality score so better grades unlock sooner
        new FeedItem("biscuit", "Cream biscuit", "biscuit", 30, 1, "A rich little treat that builds your milk quality.", Happy: 0.06f, Quality: 20, BoostMinutes: 30),
        new FeedItem("alfalfa", "Golden alfalfa", "alfalfa", 80, 1, "Prized hay that builds your milk quality a lot.", Quality: 50, BoostMinutes: 30),
        new FeedItem("starclover", "Star clover", "starclover", 200, 1, "Rare and sweet: a big step up in milk quality.", Happy: 0.10f, Quality: 100, BoostMinutes: 30),
        // Baked by your dairy's farmers and bought off the dairy shelf (not sold in the Shop)
        new FeedItem("cheese", "Cheese", "cheese", 0, 1, "Made by your dairy's farmers. A little happy, and a milk boost for 10 minutes.", Happy: 0.07f, BoostMinutes: 10, Baked: true),
        new FeedItem("cake", "Honey cream cake", "cake", 0, 1, "Baked by your dairy's farmers. Filling, very happy, and a milk boost for 25 minutes.", Fed: 0.10f, Happy: 0.15f, BoostMinutes: 25, Baked: true),
        new FeedItem("tart", "Velvet tart", "tart", 0, 1, "Baked by your dairy's farmers. The best treat: filling, very happy, and a milk boost for 30 minutes.", Fed: 0.13f, Happy: 0.18f, BoostMinutes: 30, Baked: true),
        new FeedItem("cookies", "Butter cookies", "biscuit", 0, 1, "Baked by your dairy's farmers. Happy, and a milk boost for 15 minutes.", Happy: 0.10f, BoostMinutes: 15, Baked: true),
        new FeedItem("buns", "Cream buns", "biscuit", 0, 1, "Baked by your dairy's farmers. Filling, happy, and a milk boost for 20 minutes.", Fed: 0.08f, Happy: 0.12f, BoostMinutes: 20, Baked: true),
        // Going away: keeps your grade from slipping
        // Bulls only (unlocked by bull level)
        new FeedItem("ironoats", "Iron oats", "grain", 30, 8, "A hearty bull drink of soaked oats.", Water: 0.10f, Role: "bull"),
        new FeedItem("bullsalt", "Bull's salt lick", "salt", 35, 12, "A coarse salt lick, and +25% bull xp for 30 minutes.", Happy: 0.07f, Role: "bull", XpBoost: 0.25f, XpBoostMinutes: 30),
        new FeedItem("seedtea", "Seed tea", "water", 40, 15, "A warm herbal brew. Your next seed milking gives 25% more.", Role: "bull", NextSeedBonus: 0.25f),
        new FeedItem("studtonic", "Stud draught", "water", 120, 20, "Your next breeding gives the cow 50% more bred bonus, and it lasts 15 minutes longer.", Role: "bull", StudTonic: true),
        // Cows who are also bulls (unlocked by the lower of the two levels)
        new FeedItem("twinblend", "Twin blend", "premium", 60, 10, "A treat for a cow and bull.", Happy: 0.09f, BoostMinutes: 30, Role: "both"),
        new FeedItem("bonbon", "Velvet & brass bonbon", "biscuit", 50, 15, "A rich little sweet, and +20% cow and bull xp for 30 minutes.", Happy: 0.09f, Role: "both", XpBoost: 0.20f, XpBoostMinutes: 30),
        new FeedItem("balancetea", "Second wind tonic", "water", 150, 10, "You get tired 2 hours later today. Once a day.", RestHours: 2),
        new FeedItem("pumpdrops", "Pump drops", "water", 60, 5, "Milk and seed come in 25% faster for 30 minutes.", DropsMinutes: 30),
        new FeedItem("udderbalm", "Udder balm", "cushion", 60, 9, "Your next 3 hand milkings by a farmer give 20% more milk (and seed).", BalmMilkings: 3),
        new FeedItem("churnoil", "Churn oil", "bucket", 40, 5, "Farmers: your jugs fill twice as fast for an hour.", Role: "farmer", OilMinutes: 60),
        new FeedItem("tonic", "Dry-off tonic", "water", 150, 1, "Going away? Your grade won't slip for 7 days, however long since you were milked. Use another to add 7 more.", HoldDays: 7),
    };

    public static FeedItem? FeedById(string id) => Feed.FirstOrDefault(f => f.Id == id);

    /// <summary>Feed that fills the Fed bar: only farmers give it, from the dairy's feed bin (the server's FEED_BIN order).</summary>
    public static readonly string[] BinOrder = { "hay", "grain", "honeyoats", "premium", "goldenmash" };
    // Deliveries to other dairies (buy orders and drop-offs): a daily limit by farmer level, and what a drop-off pays (matches the server)
    public const int DairyGoalMinutes = 30;
    public const int BellMinutes = 15;   // the feeding bell: its bonus goes on top of a bar's for this long
    // On the network a treat you buy gives its own bonus for a while instead of time on your bars: 10 minutes for the
    // cheap ones, 20 for the better ones (35 coins and up)
    public static int TreatMinutes(FeedItem f) => f.Price >= 35 ? 20 : 10;   // daily goal: spend this long (awake) in a dairy
    public const int DeliveriesPerDay = 10;   // drop-offs and buy orders filled, together, a day (one drop-off per dairy a day)
    public const int DeliverOzBase = 30, DeliverOzPerLevel = 5;   // 30 oz at level 1, 275 at 50, 525 at 100
    public const float DeliverPay = 0.25f;                        // of milk's price an oz (the Grand Creamery pays)
    public const float DeliverXpPerOz = 2.5f;                     // farmer xp an oz dropped off

    public static bool IsBinFeed(string id) => Array.IndexOf(BinOrder, id) >= 0;

    // ----- Daily quota: the main pay (milking itself doesn't pay since 0.9.99) -----
    // A farmer or the owner picks each member's size. Cows give milk, bulls seed, farmers deliver milk (orders, visitors,
    // contract). Reaching it once a day pays QuotaCoins x the grade multiplier x your wearables.
    // Quotas are typed in: a cow's is 1 to 50 oz of milk, a bull's 0.1 to 8.5 oz of seed (about a sixth as much)
    public const int QuotaMinOz = 1, QuotaMaxOz = 50, QuotaDefaultOz = 12;
    public const float QuotaMinSeed = 0.1f, QuotaMaxSeed = 8.5f, QuotaDefaultSeed = 2f;
    public const float QuotaCoinsPerOz = 5f;         // at Grade C: 12 oz pays 60, 50 oz pays 250
    public const float QuotaCoinsPerSeedOz = 29.4f;  // the same pay for the same work: 2 oz of seed pays about what 12 oz of milk does
    public static readonly float[] QuotaGradeMult = { 1f, 1.15f, 1.3f, 1.45f, 1.6f };

    public const float HomeBonus = 0.10f;         // milk and seed when you're milked in your own dairy
    public const float PumpBonus = 0.15f;         // the Velvet pump, for everyone in the dairy
    public const float DropsBonus = 0.25f;        // Pump drops
    public const float BalmBonus = 0.20f;         // Udder balm: hand milkings give this much more
    public const float JugFillPerSecond = 1f;     // oz a second when filling jugs from the dairy tank (Churn oil: twice that)

    public static readonly IReadOnlyList<Upgrade> Upgrades = new[]
    {
        new Upgrade("stallkit", "Stall kit", "bucket", new[] { 200, 500 }, new[] { 6, 12 }, "Lets you register one more stall."),
        new Upgrade("cushion", "Plush bedding", "cushion", new[] { 70 }, new[] { 4 }, "Comfy bonus +10% instead of +6%."),
        new Upgrade("bottle", "Bigger bottle", "bottle", new[] { 50, 135, 275, 550 }, new[] { 5, 12, 20, 40 }, "Each bottle (and seed vial) holds 2 oz more (0.2 oz for vials)."),
        // Up to 4 bottles (6 before 0.9.97, when milk came in faster): the 5th and 6th were refunded
        new Upgrade("rack", "Extra bottle", "milk", new[] { 90, 350 }, new[] { 10, 30 }, "One more bottle (and seed vial). When one fills, the next one starts. Up to 3."),
        // Farmers (and owners) buy a bigger tank to carry milk from the dairy tank; unlocks go by farmer level
        new Upgrade("farmtank", "Bigger farmer tank", "bucket", new[] { 200, 400, 900, 1600, 2500, 4000, 7500 }, new[] { 2, 5, 10, 16, 22, 30, 40 },
            "Carry more milk from the dairy tank to other dairies and the Grand Creamery.", Farmer: true),
        new Upgrade("trough", "Clean trough", "water", new[] { 90 }, new[] { 5 }, "Watered bonus +2% more."),
        new Upgrade("bell", "Bell collar", "bell", new[] { 50 }, new[] { 3 }, "A brass bell. Happy bonus +1% more.", true),
        new Upgrade("ribbon", "Show ribbon", "ribbon", new[] { 275 }, new[] { 15 }, "Wear your ribbon. +5% to your quota pay.", true, Cow: true),
        new Upgrade("cap", "Gold-cap bottles", "coin", new[] { 550 }, new[] { 25 }, "Bottles with your name on them. +10% to your quota pay.", true, Cow: true),
        // Cow perks and wearables for the higher cow levels
        new Upgrade("letdown", "Easy let-down", "bucket", new[] { 120 }, new[] { 10 }, "Stall milking draws your milk 10% faster.", Cow: true),
        new Upgrade("halter", "Silk halter", "ribbon", new[] { 325 }, new[] { 17 }, "A soft silk halter. +3% to your quota pay.", true, Cow: true),
        new Upgrade("steady", "Steady producer", "milk", new[] { 300 }, new[] { 18 }, "You get tired after 14 hours of milk a day instead of 12."),
        new Upgrade("lavender", "Lavender bedding", "cushion", new[] { 600 }, new[] { 23 }, "Happy bonus +2% more."),
        new Upgrade("mist", "Cooling mist", "water", new[] { 475 }, new[] { 24 }, "Full bottles and seed vials keep 96 hours before they spoil instead of 72."),
        new Upgrade("velvetbell", "Velvet bell", "bell", new[] { 600 }, new[] { 26 }, "A velvet-wrapped bell. +5% to your quota pay.", true, Cow: true),
        new Upgrade("champion", "Champion's ribbon", "ribbon", new[] { 900 }, new[] { 28 }, "A prized cow: farmers who hand milk you get 10% more pay and farmer xp. Being bred lasts 45 minutes instead of 30, and pregnancy gives 5% more milk.", true, Cow: true),
        new Upgrade("velvetchamp", "Velvet champion sash", "coin", new[] { 1250 }, new[] { 30 }, "The best cow in Eorzea. +10% to your quota pay.", true, Cow: true),
        // Levels 31 to 100: fewer, bigger rewards
        new Upgrade("silkcushion", "Silk cushion", "cushion", new[] { 7000 }, new[] { 45 }, "Comfy bonus +2% more (on top of Plush bedding)."),
        new Upgrade("heirloombell", "Heirloom bell", "bell", new[] { 19000 }, new[] { 55 }, "A family heirloom bell. +3% to your quota pay.", true, Cow: true),
        new Upgrade("nightowl", "Night owl", "milk", new[] { 18000 }, new[] { 62 }, "You get tired 2 hours later each day (on top of Steady producer)."),
        new Upgrade("royalhalter", "Royal halter", "ribbon", new[] { 25000 }, new[] { 70 }, "A halter fit for royalty. +3% to your quota pay.", true, Cow: true),
        new Upgrade("spring", "Endless spring", "water", new[] { 50000 }, new[] { 85 }, "Watered bonus +3% more."),
        new Upgrade("sweettooth", "Sweet tooth", "clover", new[] { 45000 }, new[] { 92 }, "Treat milk boosts last 15% longer."),
        // Bulls: unlocked by bull level, only for bulls (a cow who's also a bull gets both sets)
        new Upgrade("nosering", "Nose ring", "ribbon", new[] { 125 }, new[] { 5 }, "A polished brass ring. +3% to your seed quota pay.", true, Bull: true),
        new Upgrade("studbook", "Stud book", "coin", new[] { 350 }, new[] { 10 }, "25 breedings a day at full seed output instead of 20.", Bull: true),
        new Upgrade("horncaps", "Brass horn caps", "bell", new[] { 700 }, new[] { 20 }, "Shining caps for your horns. +5% to your seed quota pay.", true, Bull: true),
        new Upgrade("prizestud", "Prize stud", "ribbon", new[] { 1000 }, new[] { 25 }, "Bull xp from breeding +25%.", Bull: true),
        new Upgrade("breedpen", "Breeding pen", "bucket", new[] { 2500 }, new[] { 35 }, "Each breeding in your dairy gives the dairy xp.", Bull: true),
        new Upgrade("herdsire", "Herd sire", "milk", new[] { 4000 }, new[] { 50 }, "Your seed fills your vials 15% faster.", Bull: true),
        new Upgrade("bullsash", "Champion bull sash", "coin", new[] { 25000 }, new[] { 75 }, "+10% to your seed quota pay, and your breeding output never drops below 60%.", true, Bull: true),
        new Upgrade("legendstud", "Legendary stud", "coin", new[] { 12000 }, new[] { 100 }, "30 breedings a day at full seed output.", Bull: true),
        new Upgrade("crown", "Velvet crown", "coin", new[] { 55000 }, new[] { 100 }, "The final prize: a gold crown on your HUD and next to your name in your dairy's member list.", true),
    };

    public static Upgrade? UpgradeById(string id) => Upgrades.FirstOrDefault(u => u.Id == id);

    // ----- Stalls -----
    public static readonly string[] StallTierNames = { "", "Wooden stall", "Padded stall", "Velvet stall", "Gilded stall" };
    public static readonly float[] StallSpeed = { 0, 0.2f, 0.3f, 0.4f, 0.6f };    // oz per second
    public static readonly float[] StallYield = { 0, 1.0f, 1.1f, 1.2f, 1.35f };   // collected per oz
    public static readonly int[] StallTierPrice = { 0, 0, 150, 400, 900 };
    public static readonly int[] StallTierUnlock = { 0, 1, 5, 10, 18 };

    public const float HandYield = 1.25f;
    /// <summary>What can go on the dairy shelf, in the server's order (a "fed" inbox item says which by its place here).</summary>
    // Same order as the server's SHELF_ITEMS (a fed notice carries the item's place in it): new items go at the end
    public static readonly string[] ShelfOrder = { "cookies", "buns", "cheese", "cake", "tart" };
    public const int DailyPaidOz = 1500;   // milk you're paid for each day; past it you still get xp (matches the server's daily tank limit)

    // No daily limit on coins (removed in 0.10.0: it was 1,500 in full, then half up to 4,000, then a tenth). The only
    // brake is being Tired: after 12 hours of making milk in a day it comes in at half speed, and so does its pay.
    public const int FeedXp = 10;          // farmer xp for feeding a cow or bull from the shelf
    public const int FedBullXp = 20;       // bull xp for being fed
    /// <summary>Your cow grade (C, B, A, S, S+) milks you this much faster and gives this much more milk.</summary>
    public static readonly float[] GradeBoost = { 0f, 0.10f, 0.20f, 0.35f, 0.50f };
    public static float GradeMult(int grade) => 1f + GradeBoost[Math.Clamp(grade, 0, GradeBoost.Length - 1)];
    public const float HandStrokeOz = 1f;
    // ----- Farmer chores -----
    public const float CleanBonus = 0.10f;      // a freshly cleaned stall milks 10% faster and gives 10% more xp (never less than its tier)
    public const int CleanXp = 25;              // farmer xp per stall cleaned (each stall: once an hour, again when half an hour is left)
    public const int GroomXp = 20;              // farmer xp per groom (each cow: once every 15 minutes)
    public const float GroomHappy = 0.06f;      // a groom: Happy +6% and Comfy +4% for 20 minutes (a bigger bonus already going is kept)
    public const float GroomComfy = 0.04f;
    public const float FarmerWater = 0.10f;     // fresh water a farmer brings: Watered +10% (the trough is +6%)
    public const float StallSitRange = 1.5f;    // someone sitting this close to a stall is using it

    public const float HandSpeed = 0.35f;        // oz per second while a farmer keeps milking (a looping emote, close, targeting you)
    public const float BottleOz = 8f;    // a cup: what the creamery buys by

    // Spoiling: full bottles left this long go bad and have to be dumped
    public const double SpoilHours = 72;
    public const float TaintOnDump = 100f;     // off taste after dumping (one grade lower until it wears off)
    public const float TaintPerCare = 6f;      // each care task clears this much
    public const float TaintPerMilking = 12f;  // each milking
    public const float TaintPerOrder = 25f;    // each creamery delivery

    // Your bottles: size tier 1 is what you start with, the "bottle" upgrade raises it
    // Farmer tanks: what you carry milk in (Bigger farmer tank upgrades)
    public static readonly string[] FarmerTankNames = { "Milk pail", "Milk can", "Carrying churn", "Hand cart", "Dairy wagon", "Tanker cart", "Big tanker", "Grand tanker" };
    public static readonly int[] FarmerTankSizes = { 32, 64, 96, 144, 192, 256, 320, 400 };

    public static readonly string[] BottleNames = { "", "Milk bottle", "Cream jar", "Dairy jug", "Big jug", "Velvet churn" };
    // 0.9.99: sized so full bottles take about 1-3 hours of well-cared-for play to fill, so getting milked stays part of a session
    public static readonly float[] BottleSizes = { 0, 8, 10, 12, 14, 16 };

    public const float LevelBonus = 0.02f;      // +2% milk per level above 1, up to Leveling.MilkBonusCap (+4% before 0.9.97)
    public const float ContentBonus = 0.15f;    // all four need bonuses active at once
    public const float TreatBonus = 0.30f;      // while a treat boost lasts

    // ----- Need bonuses -----
    // Each bonus lasts 20 minutes from when you get it (awake play only); getting it again sets it back to 20, it never
    // stacks past that. (Before: each use added 30 minutes up to an hour, and bars drained half as fast in a dairy.)
    public const float NeedMinutes = 20f;       // a full need bar: every bonus lasts 20 minutes (like a treat)
    public const float NeedFill = 100f;         // each use fills it: 20 minutes
    public const double NeedCooldownMinutes = 15; // eat, drink, rest and treat each wait this long between uses
    public const double RestSettleMinutes = 10;
    public const float InDairyDrain = 1f;   // (0.5 before: bars drained half as fast in a dairy)   // need bars drain this much as fast while you're in a dairy   // sit this long in your dairy and you get Comfy (each further stretch adds more)
    public const float TroughBonus = 0.06f;     // Watered from the trough
    public const float ComfyBonus = 0.06f;      // Comfy from resting (Plush bedding 0.10, + Lavender bedding 0.03)
    public const float ComfyCushion = 0.10f;
    public const float ComfyLavender = 0.03f;

    public static readonly string[] Customers =
    {
        "The Bismarck", "Carline Canopy", "The Quicksand", "The Forgotten Knight", "Mor Dhona tea stall",
        "Hingan sweet shop", "Ishgard bakery", "Gold Saucer café", "Sharlayan study hall", "Moogle bakery",
    };
}

public static class Leveling
{
    public const int MaxLevel = 100;

    /// <summary>XP to go from this level to the next. Gets steeper as you climb: about 6 months of grinding
    /// (Grade S, ~5 h a day) or a year of casual play (Grade A, ~3 h a day) to reach 100.</summary>
    public static int Need(int level) => 80 + (int)MathF.Round(4.2f * MathF.Pow(level - 1, 1.8f));

    /// <summary>The milk bonus from levels stops growing here; levels past it are for rank and unlocks.</summary>
    public const int MilkBonusCap = 50;

    // ----- Where XP comes from -----
    // Milk comes in slower since 0.9.97 (a little over half as fast), so each oz gives 2.75 times the xp it did: levels
    // still take about as long as before.
    public const float OzPerXpTick = 0.0727f;     // +1 xp for every 0.0727 oz of milk you make (awake, bottles not full)
    public const float XpPerOzStall = 2.75f;      // bonus for emptying at a stall, times the stall's tier bonus
    public const float XpPerOzHand = 8.25f;       // bonus for being hand milked by a farmer
    public const int XpHandSession = 10;          // plus this for each hand milking
    public static readonly float[] StallXpBonus = { 0, 1.0f, 1.15f, 1.3f, 1.5f };
    public static readonly float[] GradeXpBonus = { 1.0f, 1.2f, 1.4f, 1.7f, 2.0f };   // C, B, A, S, S+

    public static float GradeBonus(int grade) => GradeXpBonus[Math.Clamp(grade, 0, GradeXpBonus.Length - 1)];

    public static (int Level, int Into, int Need) FromXp(int xp)
    {
        var lvl = 1;
        while (lvl < MaxLevel && xp >= Need(lvl))
        {
            xp -= Need(lvl);
            lvl++;
        }

        return lvl >= MaxLevel ? (MaxLevel, 0, 0) : (lvl, xp, Need(lvl));
    }

    private static readonly (int Level, string Name)[] Ranks =
    {
        (1, "New Cow"), (3, "Dairy Cow"), (6, "Good Producer"), (10, "Prize Cow"),
        (15, "Blue Ribbon"), (21, "Champion"), (28, "Velvet Legend"),
        (35, "Dairy Darling"), (50, "Grand Champion"), (65, "Eorzean Treasure"), (80, "Living Legend"), (100, "Velvet Icon"),
    };

    public static string Rank(int level) => Ranks.Last(r => level >= r.Level).Name;

    /// <summary>Farmer and bull titles (fewer: farmers get one at 100, bulls one every 25 levels).</summary>
    public static string? FarmerRank(int level) => level >= 100 ? "Master Farmer" : null;

    public static string? BullRank(int level) => level >= 100 ? "Legendary Bull" : level >= 75 ? "Champion Bull" : level >= 50 ? "Prize Bull" : level >= 25 ? "Strong Bull" : null;

    /// <summary>Farmer level rewards that aren't Shop items (shown on level up and on your role pages (Today > Cow, Today > Bull, Herd > Farmer)).</summary>
    public static readonly (int Level, string Name)[] FarmerPerks =
    {
        (35, "Recipe: Honey cream cake"), (45, "Hand milking share 30%"), (70, "Recipe: Velvet tart"), (85, "Faster baking (20%)"), (100, "Title: Master Farmer and a crown"),
    };

    /// <summary>Cow level rewards that aren't Shop items.</summary>
    public static readonly (int Level, string Name)[] CowPerks =
    {
        (50, "Your title on the HUD, in gold"), (75, "Velvet HUD frame and a star in your dairy's member list"),
    };

    public static string? NextRank(int level)
    {
        var next = Ranks.FirstOrDefault(r => r.Level > level);
        return next.Name == null ? null : $"{next.Name} at level {next.Level}";
    }

    /// <summary>What a level gives you (shown when you level up and on Progress).</summary>
    /// <summary>What a cow level unlocks. Farmer tanks go by farmer level; on the network, stalls go by dairy level.</summary>
    public static IEnumerable<string> UnlocksAt(int level, bool networked = false)
    {
        foreach (var f in Catalog.Feed.Where(f => f.Unlock == level && level > 1)) yield return f.Name;
        foreach (var u in Catalog.Upgrades)
        {
            if (u.Farmer || (networked && u.Id == "stallkit")) continue;
            for (var i = 0; i < u.Unlocks.Length; i++)
                if (u.Unlocks[i] == level) yield return u.MaxLevel > 1 ? $"{u.Name} {i + 1}" : u.Name;
        }

        if (networked) yield break;
        for (var t = 2; t < Catalog.StallTierUnlock.Length; t++)
            if (Catalog.StallTierUnlock[t] == level) yield return Catalog.StallTierNames[t];
    }
}

public sealed record Award(string Id, string Name, string Desc, Func<FarmSave, bool> Earned);

public static class Awards
{
    public static readonly IReadOnlyList<Award> All = new[]
    {
        new Award("first", "First pail", "Get milked for the first time.", f => f.Stats.StallSessions + f.Stats.HandSessions >= 1),
        new Award("hand", "Personal touch", "Get hand milked by a farmhand.", f => f.Stats.HandSessions >= 1),
        new Award("oz100", "Twenty ounces", "Give 20 oz of milk.", f => f.Stats.TotalOz >= 20),
        new Award("oz1000", "Two hundred ounces", "Give 200 oz of milk.", f => f.Stats.TotalOz >= 200),
        new Award("oz5000", "Milk river", "Give 1,000 oz of milk.", f => f.Stats.TotalOz >= 1000),
        new Award("gradeA", "Rich and creamy", "Give Grade A milk.", f => f.Stats.BestGrade >= 2),
        new Award("gradeS", "Cream of the crop", "Give Grade S milk.", f => f.Stats.BestGrade >= 3),
        new Award("gradeSS", "Pure velvet", "Give Grade S+ milk.", f => f.Stats.BestGrade >= 4),
        new Award("order1", "Open for business", "Make your first delivery.", f => f.Stats.OrdersFilled >= 1),
        new Award("order25", "Regular supplier", "Make 25 deliveries.", f => f.Stats.OrdersFilled >= 25),
        new Award("care50", "Well kept", "Do 50 care tasks.", f => f.Stats.CareTasks >= 50),
        new Award("daily7", "Routine", "Finish 7 daily goals.", f => f.Stats.DailiesDone >= 7),
        new Award("gilded", "Gilded", "Upgrade a stall to Gilded.", f => f.Stats.BestStallTier >= 4 || (f.Stalls != null && f.Stalls.Any(s => s.Tier >= 4))),
        new Award("rich", "Well off", "Earn 5,000 coins.", f => f.Stats.CoinsEarned >= 5000),
    };
}
