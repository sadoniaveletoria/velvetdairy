using System;
using System.Collections.Generic;
using System.Linq;

namespace VelvetDairy.Game;

public sealed record Grade(string Name, float Price, string Blurb);

public static class Grades
{
    public static readonly IReadOnlyList<Grade> All = new[]
    {
        new Grade("C", 5.0f, "Thin"),
        new Grade("B", 7.0f, "Good"),
        new Grade("A", 10.0f, "Rich"),
        new Grade("S", 14.0f, "Cream"),
        new Grade("S+", 20.0f, "Velvet"),
    };

    public static int Count => All.Count;
    public static string Name(int i) => i >= 0 && i < All.Count ? All[i].Name : "-";

    /// <summary>Grade from your needs and feed quality. Hand milking adds one step.</summary>
    public static int FromState(CowState c, bool hand)
    {
        var score = Score(c);
        var g = score < 35 ? 0 : score < 55 ? 1 : score < 72 ? 2 : score < 88 ? 3 : 4;
        if (hand) g++;
        return Math.Min(g, All.Count - 1);
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
    public const double QualityTreatHours = 4;

    /// <summary>Best grade your milk quality allows.</summary>
    public static int Cap(float quality)
    {
        var g = 0;
        for (var i = 0; i < QualityNeed.Length; i++)
            if (quality >= QualityNeed[i]) g = i;
        return g;
    }

    /// <summary>Your grade: care and feed decide it, hand milking adds one, and your milk quality caps it.</summary>
    public static int For(CowState c, bool hand, float quality) => Math.Min(FromState(c, hand), Cap(quality));

    /// <summary>Points the off taste from dumped milk takes off your grade score (up to 40).</summary>
    public static float TaintPenalty(CowState c) => c.Taint * 0.4f;

    public static float Score(CowState c) =>
        Math.Max(0, (c.Hunger + c.Thirst + c.Comfort + c.Mood) / 4f * 0.6f + c.Richness * 0.4f - TaintPenalty(c));
}

/// <summary>A feed or treat you buy, keep in the pantry and use inside your dairy.</summary>
public sealed record FeedItem(
    string Id, string Name, string Icon, int Price, int Unlock, string Desc,
    float Hunger = 0, float Thirst = 0, float Comfort = 0, float Mood = 0, float Richness = 0, int BoostMinutes = 0, float Quality = 0);

/// <summary>Something you buy once or in levels.</summary>
public sealed record Upgrade(string Id, string Name, string Icon, int[] Prices, int[] Unlocks, string Desc, bool Cosmetic = false, bool Farmer = false)
{
    public int MaxLevel => Prices.Length;
}

public static class Catalog
{
    public static readonly IReadOnlyList<FeedItem> Feed = new[]
    {
        new FeedItem("hay", "Meadow hay", "hay", 4, 1, "Plain and filling.", Hunger: 30, Richness: 4),
        new FeedItem("grain", "Sweet grain", "grain", 12, 2, "Makes richer milk.", Hunger: 24, Richness: 14),
        new FeedItem("clover", "Fresh clover", "clover", 15, 3, "A treat. Lifts your mood and speeds up your milk for a while.", Hunger: 6, Mood: 22, BoostMinutes: 15),
        new FeedItem("salt", "Salt lick", "salt", 20, 4, "Mood and richness, but it makes you thirsty. Short milk boost.", Thirst: -12, Mood: 14, Richness: 10, BoostMinutes: 10),
        new FeedItem("premium", "Velvet blend", "premium", 45, 8, "The best feed money buys. Long milk boost.", Hunger: 40, Mood: 10, Richness: 32, BoostMinutes: 30),
        // Quality treats: raise your milk quality score so better grades unlock sooner
        new FeedItem("biscuit", "Cream biscuit", "biscuit", 30, 1, "A rich little treat. Builds your milk quality.", Hunger: 4, Mood: 6, Quality: 20),
        new FeedItem("alfalfa", "Golden alfalfa", "alfalfa", 80, 1, "Prized hay. Builds your milk quality a lot.", Hunger: 12, Richness: 8, Quality: 50),
        new FeedItem("starclover", "Star clover", "starclover", 200, 1, "Rare and sweet. A big step up in milk quality.", Mood: 15, Richness: 12, Quality: 100),
    };

    public static FeedItem? FeedById(string id) => Feed.FirstOrDefault(f => f.Id == id);

    public static readonly IReadOnlyList<Upgrade> Upgrades = new[]
    {
        new Upgrade("stallkit", "Stall kit", "bucket", new[] { 200, 500 }, new[] { 6, 12 }, "Lets you register one more stall."),
        new Upgrade("cushion", "Plush bedding", "cushion", new[] { 250 }, new[] { 4 }, "Comfort drops half as fast. Resting restores more."),
        new Upgrade("bottle", "Bigger bottle", "bottle", new[] { 180, 450, 1000, 2200 }, new[] { 3, 8, 14, 21 }, "Each bottle holds more."),
        new Upgrade("rack", "Extra bottle", "milk", new[] { 300, 800, 1800, 3500 }, new[] { 6, 12, 19, 25 }, "One more bottle. When one fills, the next one starts."),
        // Farmers (and owners) buy a bigger tank to carry milk from the dairy tank; unlocks go by farmer level
        new Upgrade("farmtank", "Bigger farmer tank", "bucket", new[] { 200, 500, 1200, 2500, 5000 }, new[] { 2, 5, 10, 16, 22 },
            "Carry more milk from the dairy tank to customers, other dairies and the Grand Creamery.", Farmer: true),
        new Upgrade("trough", "Clean trough", "water", new[] { 160 }, new[] { 5 }, "Thirst drops a third slower."),
        new Upgrade("bell", "Bell collar", "bell", new[] { 80 }, new[] { 3 }, "A brass bell. Mood drops a little slower.", true),
        new Upgrade("ribbon", "Show ribbon", "ribbon", new[] { 600 }, new[] { 15 }, "Wear your ribbon. +5% creamery prices.", true),
        new Upgrade("cap", "Gold-cap bottles", "coin", new[] { 1500 }, new[] { 25 }, "Bottles with your name on them. +10% creamery prices.", true),
    };

    public static Upgrade? UpgradeById(string id) => Upgrades.FirstOrDefault(u => u.Id == id);

    // ----- Stalls -----
    public static readonly string[] StallTierNames = { "", "Wooden stall", "Padded stall", "Velvet stall", "Gilded stall" };
    public static readonly float[] StallSpeed = { 0, 0.2f, 0.3f, 0.4f, 0.6f };    // oz per second
    public static readonly float[] StallYield = { 0, 1.0f, 1.1f, 1.2f, 1.35f };   // collected per oz
    public static readonly int[] StallTierPrice = { 0, 0, 150, 400, 900 };
    public static readonly int[] StallTierUnlock = { 0, 1, 5, 10, 18 };

    public const float HandYield = 1.25f;
    public const float HandStrokeOz = 1f;
    public const float HandSpeed = 0.35f;        // oz per second while a farmer keeps milking (a looping emote, close, targeting you)
    public const float BottleOz = 8f;    // a cup: what the creamery buys by

    // Spoiling: full bottles left this long go bad and have to be dumped
    public const double SpoilHours = 72;
    public const float TaintOnDump = 100f;     // off taste after dumping (costs up to 40 grade points)
    public const float TaintPerCare = 6f;      // each care task clears this much
    public const float TaintPerMilking = 12f;  // each milking
    public const float TaintPerOrder = 25f;    // each creamery delivery

    // Your bottles: size tier 1 is what you start with, the "bottle" upgrade raises it
    // Farmer tanks: what you carry milk in (Bigger farmer tank upgrades)
    public static readonly string[] FarmerTankNames = { "Milk pail", "Milk can", "Carrying churn", "Hand cart", "Dairy wagon", "Tanker cart" };
    public static readonly int[] FarmerTankSizes = { 32, 64, 96, 144, 192, 256 };

    public static readonly string[] BottleNames = { "", "Milk bottle", "Cream jar", "Dairy jug", "Big jug", "Milk churn" };
    // Milk is sized at about five times a real person's yield: roughly 125-150 oz a day with good care
    public static readonly float[] BottleSizes = { 0, 8, 12, 20, 32, 48 };

    public const float LevelBonus = 0.04f;      // +4% milk per level above 1
    public const float ContentBonus = 0.25f;    // all four needs at 75 or more
    public const float TreatBonus = 0.30f;      // while a treat boost lasts
    public const float RestBonus = 0.5f;        // resting in your dairy

    public static readonly string[] Customers =
    {
        "The Bismarck", "Carline Canopy", "The Quicksand", "The Forgotten Knight", "Mor Dhona tea stall",
        "Hingan sweet shop", "Ishgard bakery", "Gold Saucer café", "Sharlayan study hall", "Moogle bakery",
    };
}

public static class Leveling
{
    public const int MaxLevel = 30;

    public static int Need(int level) => 80 + 30 * (level - 1);

    // ----- Where XP comes from -----
    public const float OzPerXpTick = 0.1f;        // +1 xp for every 0.1 oz of milk you make (awake, bottles not full)
    public const float XpPerOzStall = 2f;         // bonus for emptying at a stall, times the stall's tier bonus
    public const float XpPerOzHand = 6f;          // bonus for being hand milked by a farmer
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
    };

    public static string Rank(int level) => Ranks.Last(r => level >= r.Level).Name;

    public static string? NextRank(int level)
    {
        var next = Ranks.FirstOrDefault(r => r.Level > level);
        return next.Name == null ? null : $"{next.Name} at level {next.Level}";
    }

    /// <summary>What a level gives you (shown when you level up and on the Progress tab).</summary>
    public static IEnumerable<string> UnlocksAt(int level)
    {
        foreach (var f in Catalog.Feed.Where(f => f.Unlock == level && level > 1)) yield return f.Name;
        foreach (var u in Catalog.Upgrades)
            for (var i = 0; i < u.Unlocks.Length; i++)
                if (u.Unlocks[i] == level) yield return u.MaxLevel > 1 ? $"{u.Name} {i + 1}" : u.Name;
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
        new Award("gilded", "Gilded", "Upgrade a stall to Gilded.", f => f.Stalls != null && f.Stalls.Any(s => s.Tier >= 4)),
        new Award("rich", "Well off", "Earn 5,000 coins.", f => f.Stats.CoinsEarned >= 5000),
    };
}
