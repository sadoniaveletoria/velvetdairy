using System;
using System.Collections.Generic;
using System.Linq;

namespace VelvetDairy.Game;

public sealed record Grade(string Name, float Price, string Blurb);

public static class Grades
{
    public static readonly IReadOnlyList<Grade> All = new[]
    {
        new Grade("C", 7.0f, "Thin"),
        new Grade("B", 10.0f, "Good"),
        new Grade("A", 15.0f, "Rich"),
        new Grade("S", 21.0f, "Cream"),
        new Grade("S+", 30.0f, "Velvet"),
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
    float Fed = 0, float Water = 0, float Happy = 0, int BoostMinutes = 0, float Quality = 0, int HoldDays = 0);

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
    public static readonly IReadOnlyList<FeedItem> Feed = new[]
    {
        new FeedItem("hay", "Meadow hay", "hay", 4, 1, "Plain and filling.", Fed: 0.04f),
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
        // Going away: keeps your grade from slipping
        new FeedItem("tonic", "Dry-off tonic", "water", 150, 1, "Going away? Your grade won't slip for 7 days, however long since you were milked. Use another to add 7 more.", HoldDays: 7),
    };

    public static FeedItem? FeedById(string id) => Feed.FirstOrDefault(f => f.Id == id);

    public static readonly IReadOnlyList<Upgrade> Upgrades = new[]
    {
        new Upgrade("stallkit", "Stall kit", "bucket", new[] { 200, 500 }, new[] { 6, 12 }, "Lets you register one more stall."),
        new Upgrade("cushion", "Plush bedding", "cushion", new[] { 250 }, new[] { 4 }, "Resting makes you comfier: Comfy bonus +7% instead of +4%, and it lasts twice as long after you get up."),
        new Upgrade("bottle", "Bigger bottle", "bottle", new[] { 180, 450, 1000, 2200 }, new[] { 3, 8, 14, 21 }, "Each bottle holds more."),
        new Upgrade("rack", "Extra bottle", "milk", new[] { 300, 800, 1800, 3500 }, new[] { 6, 12, 19, 25 }, "One more bottle. When one fills, the next one starts."),
        // Farmers (and owners) buy a bigger tank to carry milk from the dairy tank; unlocks go by farmer level
        new Upgrade("farmtank", "Bigger farmer tank", "bucket", new[] { 200, 500, 1200, 2500, 5000 }, new[] { 2, 5, 10, 16, 22 },
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
    public static readonly string[] FarmerTankNames = { "Milk pail", "Milk can", "Carrying churn", "Hand cart", "Dairy wagon", "Tanker cart" };
    public static readonly int[] FarmerTankSizes = { 32, 64, 96, 144, 192, 256 };

    public static readonly string[] BottleNames = { "", "Milk bottle", "Cream jar", "Dairy jug", "Big jug", "Milk churn" };
    // Milk is sized at about five times a real person's yield: roughly 125-150 oz a day with good care
    public static readonly float[] BottleSizes = { 0, 8, 12, 20, 32, 48 };

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
    };

    public static string Rank(int level) => Ranks.Last(r => level >= r.Level).Name;

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
        new Award("gilded", "Gilded", "Upgrade a stall to Gilded.", f => f.Stalls != null && f.Stalls.Any(s => s.Tier >= 4)),
        new Award("rich", "Well off", "Earn 5,000 coins.", f => f.Stats.CoinsEarned >= 5000),
    };
}
