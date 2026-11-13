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
    public static DateTime FreshSince(CowState c)
    {
        var now = DateTime.UtcNow;
        var since = c.LastMilkedUtc ?? now;
        var hold = c.FreshHoldUntilUtc > now ? now : c.FreshHoldUntilUtc;
        return hold > since ? hold : since;
    }

    /// <summary>Grade steps lost to going too long without being milked.</summary>
    public static int Slip(CowState c)
    {
        var days = (DateTime.UtcNow - FreshSince(c)).TotalDays;
        return days < FreshDays ? 0 : 1 + (int)((days - FreshDays) / FreshDays);
    }

    /// <summary>When the next slip happens (null while a tonic holds it).</summary>
    public static DateTime? NextSlipUtc(CowState c)
    {
        if (c.FreshHoldUntilUtc > DateTime.UtcNow) return null;
        var since = FreshSince(c);
        return since.AddDays(FreshDays * (Slip(c) + 1));
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
    public static int For(CowState c, bool hand, float quality)
    {
        var cap = Cap(quality);
        var g = cap - Slip(c) - (c.Taint > 0 ? 1 : 0) + (hand ? 1 : 0);
        return Math.Clamp(g, 0, cap);
    }
}

/// <summary>A feed or treat you buy, keep in the pantry and use inside your dairy.</summary>
public sealed record FeedItem(
    string Id, string Name, string Icon, int Price, int Unlock, string Desc,
    float Fed = 0, float Water = 0, float Happy = 0, int BoostMinutes = 0, float Quality = 0, int HoldDays = 0, bool Baked = false);

/// <summary>Something you buy once or in levels.</summary>
public sealed record Upgrade(string Id, string Name, string Icon, int[] Prices, int[] Unlocks, string Desc, bool Cosmetic = false, bool Farmer = false)
{
    public int MaxLevel => Prices.Length;
}

public static class Catalog
{
    // Each need is a milk bonus. Each use adds 30 minutes (half the bar, up to an hour) and has a 15-minute cooldown.
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
        new Recipe("butter", "Churn butter", "butter", 1, 16, 0, 1, 12, 30, "Churn 16 oz of milk from your jugs into a pat of butter."),
        new Recipe("flour", "Mill flour", "flour", 1, 0, 0, 6, 6, 20, "Mill 2 wheat grain (from the Shop) into flour for the dairy pantry.",
            Uses: "hay", UsesCount: 2),
        new Recipe("cheese", "Make cheese", "cheese", 2, 16, 0, 1, 10, 40,
            "16 oz of milk makes 2 cheese for the dairy shelf: the simplest treat, nothing else needed. Members buy them; you get 80% of the price.",
            ToShelf: true),
        new Recipe("cookies", "Bake butter cookies", "cookies", 3, 8, 0, 3, 15, 45,
            "1 butter and 8 oz of milk make 3 butter cookies for the dairy shelf. Members buy them; you get 80% of the price.",
            Uses: "butter", UsesCount: 1, ToShelf: true),
        new Recipe("buns", "Bake cream buns", "buns", 2, 12, 0, 10, 25, 75,
            "1 flour, 1 butter and 12 oz of milk make 2 cream buns for the dairy shelf: a bigger treat than cookies. Members buy them; you get 80% of the price.",
            Uses: "flour", UsesCount: 1, ToShelf: true, Uses2: "butter", UsesCount2: 1),
        new Recipe("cake", "Bake honey cream cake", "cake", 2, 16, 0, 35, 40, 100,
            "1 flour, 2 butter and 16 oz of milk make 2 honey cream cakes for the dairy shelf: a big treat. Members buy them; you get 80% of the price.",
            Uses: "flour", UsesCount: 1, ToShelf: true, Uses2: "butter", UsesCount2: 2),
        new Recipe("tart", "Bake velvet tart", "tart", 2, 24, 0, 70, 60, 130,
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
    public static bool IsBaked(Recipe r) => r.Name.StartsWith("Bake ", StringComparison.Ordinal);
    public static string Verb(Recipe r) => IsBaked(r) ? "Bake" : "Make";
    public static string Doing(Recipe r) => IsBaked(r) ? "Baking" : "Making";

    public static readonly IReadOnlyList<FeedItem> Feed = new[]
    {
        new FeedItem("hay", "Wheat grain", "grain", 4, 1, "Plain and filling. Farmers can also mill it into flour in the Kitchen.", Fed: 0.04f),
        new FeedItem("grain", "Sweet grain", "grain", 12, 2, "A better meal.", Fed: 0.07f),
        new FeedItem("clover", "Fresh clover", "clover", 15, 3, "A treat. Makes you happy and speeds up your milk for a while.", Happy: 0.05f, BoostMinutes: 15),
        new FeedItem("salt", "Salt lick", "salt", 20, 4, "A salty treat. Happy, and a short milk boost.", Happy: 0.04f, BoostMinutes: 10),
        new FeedItem("premium", "Velvet blend", "premium", 45, 8, "The best feed money buys. Long milk boost.", Fed: 0.12f, BoostMinutes: 30),
        new FeedItem("honeyoats", "Honey oats", "grain", 18, 7, "Sweet and filling. A nicer everyday feed.", Fed: 0.09f),
        new FeedItem("mineral", "Mineral block", "salt", 22, 11, "Better than plain water.", Water: 0.08f),
        new FeedItem("creamclover", "Cream clover", "clover", 35, 13, "A lush treat. Very happy, and faster milk for 45 minutes.", Happy: 0.08f, BoostMinutes: 45),
        new FeedItem("goldenmash", "Golden mash", "premium", 70, 22, "A feast. The best meal there is, and a milk boost.", Fed: 0.15f, BoostMinutes: 30),
        // Quality treats: raise your milk quality score so better grades unlock sooner
        new FeedItem("biscuit", "Cream biscuit", "biscuit", 30, 1, "A rich little treat. Builds your milk quality and speeds up your milk for 30 minutes.", Happy: 0.04f, Quality: 20, BoostMinutes: 30),
        new FeedItem("alfalfa", "Golden alfalfa", "alfalfa", 80, 1, "Prized hay. Builds your milk quality a lot and speeds up your milk for 30 minutes.", Fed: 0.06f, Quality: 50, BoostMinutes: 30),
        new FeedItem("starclover", "Star clover", "starclover", 200, 1, "Rare and sweet. A big step up in milk quality, and faster milk for 30 minutes.", Happy: 0.10f, Quality: 100, BoostMinutes: 30),
        // Baked by your dairy's farmers and bought off the dairy shelf (not sold in the Shop)
        new FeedItem("cheese", "Cheese", "cheese", 0, 1, "Made by your dairy's farmers. A little happy, and a milk boost for 10 minutes.", Happy: 0.04f, BoostMinutes: 10, Baked: true),
        new FeedItem("cake", "Honey cream cake", "biscuit", 0, 1, "Baked by your dairy's farmers. Filling, very happy, and a milk boost for 25 minutes.", Fed: 0.06f, Happy: 0.09f, BoostMinutes: 25, Baked: true),
        new FeedItem("tart", "Velvet tart", "biscuit", 0, 1, "Baked by your dairy's farmers. The best treat: filling, very happy, and a milk boost for 30 minutes.", Fed: 0.08f, Happy: 0.11f, BoostMinutes: 30, Baked: true),
        new FeedItem("cookies", "Butter cookies", "biscuit", 0, 1, "Baked by your dairy's farmers. Happy, and a milk boost for 15 minutes.", Happy: 0.06f, BoostMinutes: 15, Baked: true),
        new FeedItem("buns", "Cream buns", "biscuit", 0, 1, "Baked by your dairy's farmers. Filling, happy, and a milk boost for 20 minutes.", Fed: 0.05f, Happy: 0.07f, BoostMinutes: 20, Baked: true),
        // Going away: keeps your grade from slipping
        new FeedItem("tonic", "Dry-off tonic", "water", 150, 1, "Going away? Your grade won't slip for 7 days, however long since you were milked. Use another to add 7 more.", HoldDays: 7),
    };

    public static FeedItem? FeedById(string id) => Feed.FirstOrDefault(f => f.Id == id);

    public static readonly IReadOnlyList<Upgrade> Upgrades = new[]
    {
        new Upgrade("stallkit", "Stall kit", "bucket", new[] { 200, 500 }, new[] { 6, 12 }, "Lets you register one more stall."),
        new Upgrade("cushion", "Plush bedding", "cushion", new[] { 250 }, new[] { 4 }, "Resting makes you comfier: Comfy bonus +7% instead of +4%, and it lasts twice as long after you get up."),
        new Upgrade("bottle", "Bigger bottle", "bottle", new[] { 180, 450, 1000, 2200, 7500 }, new[] { 3, 8, 14, 21, 40 }, "Each bottle holds more."),
        new Upgrade("rack", "Extra bottle", "milk", new[] { 300, 800, 1800, 3500, 6000 }, new[] { 6, 12, 19, 25, 33 }, "One more bottle. When one fills, the next one starts."),
        // Farmers (and owners) buy a bigger tank to carry milk from the dairy tank; unlocks go by farmer level
        new Upgrade("farmtank", "Bigger farmer tank", "bucket", new[] { 200, 500, 1200, 2500, 5000, 8000, 14000 }, new[] { 2, 5, 10, 16, 22, 30, 60 },
            "Carry more milk from the dairy tank to customers, other dairies and the Grand Creamery.", Farmer: true),
        new Upgrade("trough", "Clean trough", "water", new[] { 160 }, new[] { 5 }, "Watered lasts half again as long."),
        new Upgrade("bell", "Bell collar", "bell", new[] { 80 }, new[] { 3 }, "A brass bell. Happy lasts a little longer.", true),
        new Upgrade("ribbon", "Show ribbon", "ribbon", new[] { 600 }, new[] { 15 }, "Wear your ribbon. +5% creamery prices.", true),
        new Upgrade("cap", "Gold-cap bottles", "coin", new[] { 1500 }, new[] { 25 }, "Bottles with your name on them. +10% creamery prices.", true),
        // Cow perks and wearables for the higher cow levels
        new Upgrade("udderbalm", "Udder balm", "cushion", new[] { 400 }, new[] { 9 }, "Hand milking by a farmer flows 15% faster."),
        new Upgrade("letdown", "Easy let-down", "bucket", new[] { 600 }, new[] { 10 }, "Stall milking is 10% faster."),
        new Upgrade("deeprest", "Deep rest", "cushion", new[] { 900 }, new[] { 16 }, "Resting in your dairy speeds up milk by 75% instead of 50%."),
        new Upgrade("halter", "Silk halter", "ribbon", new[] { 1000 }, new[] { 17 }, "A soft silk halter. +3% pay.", true),
        new Upgrade("steady", "Steady producer", "milk", new[] { 1200 }, new[] { 18 }, "You get tired after 14 hours of milk a day instead of 12."),
        new Upgrade("heifer", "Prize heifer sash", "ribbon", new[] { 1600 }, new[] { 20 }, "Milk quality builds 10% faster.", true),
        new Upgrade("lavender", "Lavender bedding", "cushion", new[] { 1800 }, new[] { 23 }, "Comfy bonus +3% more, and Happy lasts twice as long."),
        new Upgrade("mist", "Cooling mist", "water", new[] { 2000 }, new[] { 24 }, "Full bottles keep 96 hours before they spoil instead of 72."),
        new Upgrade("velvetbell", "Velvet bell", "bell", new[] { 2600 }, new[] { 26 }, "A velvet-wrapped bell. +5% pay.", true),
        new Upgrade("champion", "Champion's ribbon", "ribbon", new[] { 3200 }, new[] { 28 }, "Milk quality builds 20% faster.", true),
        new Upgrade("velvetchamp", "Velvet champion sash", "coin", new[] { 5000 }, new[] { 30 }, "The best cow in Eorzea. +10% pay.", true),
        // Levels 31 to 100: fewer, bigger rewards
        new Upgrade("silkcushion", "Silk cushion", "cushion", new[] { 6500 }, new[] { 45 }, "Comfy drains 15% slower."),
        new Upgrade("heirloombell", "Heirloom bell", "bell", new[] { 9000 }, new[] { 55 }, "A family heirloom bell. +3% pay.", true),
        new Upgrade("nightowl", "Night owl", "milk", new[] { 10000 }, new[] { 62 }, "You get tired 2 hours later each day (on top of Steady producer)."),
        new Upgrade("royalhalter", "Royal halter", "ribbon", new[] { 12000 }, new[] { 70 }, "A halter fit for royalty. +3% pay.", true),
        new Upgrade("spring", "Endless spring", "water", new[] { 15000 }, new[] { 85 }, "Watered drains 20% slower."),
        new Upgrade("sweettooth", "Sweet tooth", "clover", new[] { 18000 }, new[] { 92 }, "Treat milk boosts last 15% longer."),
        new Upgrade("crown", "Velvet crown", "coin", new[] { 25000 }, new[] { 100 }, "The final prize: a gold crown on your HUD and next to your name in your dairy's member list.", true),
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

    // Everything you earn from the game in a day (milking, hand milking shares, orders, goals, awards) tapers off together:
    // the first FullEarnings coins are paid in full, then half up to HalfEarnings, then a tenth. Coins from other
    // players (shelf sales, buy orders) aren't touched. No cap, but more play pays less and less.
    public const int FullEarnings = 3000;
    public const int HalfEarnings = 8000;

    /// <summary>What `gross` coins are actually worth when `earnedToday` (gross) were already earned today.</summary>
    public static int Tapered(float earnedToday, int gross)
    {
        double pay = 0, at = earnedToday, left = gross;
        foreach (var (upTo, rate) in new (double, double)[] { (FullEarnings, 1.0), (HalfEarnings, 0.5), (double.MaxValue, 0.10) })
        {
            if (left <= 0) break;
            var room = Math.Max(0, upTo - at);
            var part = Math.Min(room, left);
            pay += part * rate;
            at += part;
            left -= part;
        }

        return (int)Math.Round(pay);
    }

    /// <summary>The share of each new coin you get right now (1, 0.5 or 0.1).</summary>
    public static float TaperRate(float earnedToday) => earnedToday < FullEarnings ? 1f : earnedToday < HalfEarnings ? 0.5f : 0.10f;
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
    public const float GroomHappy = 0.05f;      // Happy bonus a groom gives a cow who has none going
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

    public static readonly string[] BottleNames = { "", "Milk bottle", "Cream jar", "Dairy jug", "Big jug", "Milk churn", "Velvet churn" };
    // Milk is sized at about five times a real person's yield: roughly 125-150 oz a day with good care
    public static readonly float[] BottleSizes = { 0, 8, 12, 20, 32, 48, 64 };

    public const float LevelBonus = 0.04f;      // +4% milk per level above 1, up to Leveling.MilkBonusCap
    public const float ContentBonus = 0.10f;    // all four need bonuses active at once
    public const float TreatBonus = 0.30f;      // while a treat boost lasts
    public const float RestBonus = 0.5f;        // resting in your dairy

    // ----- Need bonuses -----
    public const float NeedMinutes = 60f;       // a full need bar (awake play only)
    public const float NeedFill = 50f;          // each use adds half a bar: 30 minutes
    public const double NeedCooldownMinutes = 15; // eat, drink, rest and treat each wait this long between uses
    public const double RestSettleMinutes = 15;   // stay seated this long after Rest or the Comfy it gave goes away when you stand
    public const float TroughBonus = 0.04f;     // Watered from the trough
    public const float ComfyBonus = 0.04f;      // Comfy from resting (Plush bedding 0.07, + Lavender bedding 0.03)
    public const float ComfyCushion = 0.07f;
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
    public const float OzPerXpTick = 0.2f;        // +1 xp for every 0.2 oz of milk you make (awake, bottles not full)
    public const float XpPerOzStall = 1f;         // bonus for emptying at a stall, times the stall's tier bonus
    public const float XpPerOzHand = 3f;          // bonus for being hand milked by a farmer
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

    /// <summary>Farmer level rewards that aren't Shop items (shown on level up and on the Roles tab).</summary>
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

    /// <summary>What a level gives you (shown when you level up and on the Progress tab).</summary>
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
        new Award("order1", "Open for business", "Fill a creamery order.", f => f.Stats.OrdersFilled >= 1),
        new Award("order25", "Regular supplier", "Fill 25 creamery orders.", f => f.Stats.OrdersFilled >= 25),
        new Award("care50", "Well kept", "Do 50 care tasks.", f => f.Stats.CareTasks >= 50),
        new Award("daily7", "Routine", "Finish 7 daily goals.", f => f.Stats.DailiesDone >= 7),
        new Award("gilded", "Gilded", "Upgrade a stall to Gilded.", f => f.Stats.BestStallTier >= 4 || (f.Stalls != null && f.Stalls.Any(s => s.Tier >= 4))),
        new Award("rich", "Well off", "Earn 5,000 coins.", f => f.Stats.CoinsEarned >= 5000),
    };
}
