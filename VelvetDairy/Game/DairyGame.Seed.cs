using System;
using System.Collections.Generic;
using System.Linq;

namespace VelvetDairy.Game;

/// <summary>
/// A bull's seed works like a cow's milk: the same needs (shared with milk if you're both), vials instead of bottles,
/// grades from seed quality, freshness and spoiling, stalls and hand milking, pay and a tank at the dairy.
/// The difference is how much: vials hold a tenth of a bottle, and seed comes in at 10% of milk's speed at Grade C,
/// rising to 20% at S+. Each oz pays ten times milk's price.
/// </summary>
public sealed partial class DairyGame
{
    /// <summary>Vials hold a tenth of your bottles, and milking draws seed at a tenth of milk's speed.</summary>
    public const float SeedRatio = 0.10f;

    /// <summary>Seed speed as a share of milk speed, by seed grade (C to S+).</summary>
    public static readonly float[] SeedRateByGrade = { 0.10f, 0.125f, 0.15f, 0.175f, 0.20f };

    /// <summary>Seed pays this many times milk's price per oz (there's only a tenth as much of it).</summary>
    public const float SeedPriceMult = 10f;

    public bool ProducesSeed => IsBull;

    /// <summary>The level the Shop goes by: your cow level, or your bull level if that's higher (bulls buy the same bottles, feed and upgrades).</summary>
    public int ShopLevel => IsBull ? Math.Max(Level, BullLevel) : Level;

    /// <summary>Makes something to be milked: a cow (milk), a bull (seed), or both.</summary>
    public bool Producer => ProducesMilk || ProducesSeed;

    public float VialSize => BottleSize * SeedRatio;
    public float SeedCapacity => Capacity * SeedRatio;
    public int FullVials => Math.Min(BottleCount, (int)((Cow.Seed + 0.001f) / VialSize));
    public bool SeedFull => Cow.Seed >= SeedCapacity - 0.001f;
    public bool SeedSpoiled => Cow.SeedSpoiled;

    public TimeSpan? SeedSpoilsIn => Cow.SeedSpoiled || Cow.SeedFullSinceUtc == null ? null
        : Cow.SeedFullSinceUtc.Value.AddHours(SpoilHours) - DateTime.UtcNow;

    public int SeedGrade => Grades.SeedFor(Cow, false, F.SeedQuality);
    public int SeedGradeCap => Grades.Cap(F.SeedQuality);
    public int SeedSlip => Grades.Slip(Cow.SeedLastMilkedUtc, Cow.FreshHoldUntilUtc);
    public DateTime? SeedNextSlipUtc => Grades.NextSlipUtc(Cow.SeedLastMilkedUtc, Cow.FreshHoldUntilUtc);
    public float? NextSeedQuality => SeedGradeCap + 1 < Grades.QualityNeed.Length ? Grades.QualityNeed[SeedGradeCap + 1] : null;

    /// <summary>Seed speed as a share of milk speed at your seed grade right now.</summary>
    public float SeedShare => SeedRateByGrade[Math.Clamp(SeedGrade, 0, SeedRateByGrade.Length - 1)];

    /// <summary>What goes into your seed speed: the same needs and bonuses as milk, your bull level, and your seed grade.</summary>
    public List<RatePart> SeedRateParts() => RateParts(true, true);

    public float SeedPerMinute() => SeedRateParts().Aggregate(BaseRate, (r, p) => r * p.Multiplier);

    public bool SeedComing => ProducesSeed && !Paused && !SeedFull && !Cow.SeedSpoiled && Blocked() == null && SeedPerMinute() > 0;

    /// <summary>Something's waiting to be milked: enough milk or seed for a milking.</summary>
    public bool HasEnoughToMilk => (ProducesMilk && Cow.Milk >= 0.5f) || (ProducesSeed && Cow.Seed >= 0.5f * SeedRatio);

    /// <summary>Enough waiting for a stall to start by itself.</summary>
    private bool HasEnoughForStall => (ProducesMilk && Cow.Milk >= 1) || (ProducesSeed && Cow.Seed >= SeedRatio);

    /// <summary>Half a bottle (or vial) waiting: sitting back down starts the stall again.</summary>
    private bool HalfBottleWaiting => (ProducesMilk && Cow.Milk >= Math.Min(BottleSize * 0.5f, Capacity)) ||
                                      (ProducesSeed && Cow.Seed >= Math.Min(VialSize * 0.5f, SeedCapacity));

    /// <summary>Nothing left to draw.</summary>
    private bool EmptiedOut => (!ProducesMilk || Cow.Milk < 0.1f) && (!ProducesSeed || Cow.Seed < 0.1f * SeedRatio);

    /// <summary>Why you can't be milked because of spoiling, or null.</summary>
    private string? SpoiledProblem =>
        ProducesMilk && Cow.Spoiled ? "Your milk has spoiled. Dump it first."
        : ProducesSeed && Cow.SeedSpoiled ? "Your seed has spoiled. Dump it first."
        : null;

    private float seedMadeSinceTick;
    private float seedGradeExtra;
    private int seedSpoilWarned;
    private float pendingSeedPump;

    /// <summary>Seed for the dairy's seed tank since the last heartbeat.</summary>
    public float TakeSeedPumped()
    {
        var r = (float)Math.Round(pendingSeedPump, 3);
        pendingSeedPump = 0;
        return r;
    }

    /// <summary>Goes up each time seed is dumped, so the windows snap the vial to empty.</summary>
    public int SeedDumpSerial { get; private set; }

    private void SimulateSeed(float minutes)
    {
        if (!ProducesSeed) return;
        if (Cow.Seed > SeedCapacity) Cow.Seed = SeedCapacity;
        var fullBefore = FullVials;
        if (!SeedFull && Active == null && !Cow.SeedSpoiled)
        {
            var before = Cow.Seed;
            Cow.Seed = Math.Min(SeedCapacity, Cow.Seed + SeedPerMinute() * minutes);
            seedMadeSinceTick += Cow.Seed - before;
            if (FullVials > fullBefore)
                Say(FullVials >= BottleCount ? "All your seed vials are full. Time to get milked." : $"Seed vial {FullVials} of {BottleCount} is full.", 0);
        }

        if (SeedFull && Cow.SeedFullSinceUtc == null) Cow.SeedFullSinceUtc = DateTime.UtcNow;
        else if (!SeedFull && !Cow.SeedSpoiled) Cow.SeedFullSinceUtc = null;
        CheckSeedSpoil();
    }

    private void CheckSeedSpoil()
    {
        if (!ProducesSeed) return;
        if (Cow.SeedSpoiled || Cow.SeedFullSinceUtc == null)
        {
            if (!Cow.SeedSpoiled) seedSpoilWarned = 0;
            return;
        }

        var left = Cow.SeedFullSinceUtc.Value.AddHours(SpoilHours) - DateTime.UtcNow;
        string? warn = null;
        if (left <= TimeSpan.Zero)
        {
            Cow.SeedSpoiled = true;
            Stop("Your seed spoiled.");
            Say("Your seed has spoiled. Dump it from the HUD or the Barn tab before you can be milked again.", 2);
            Plugin.ChatGui.Print("Your seed has spoiled. Dump it from the HUD or the Barn tab.", "Eorzea Milkers");
            plugin.SaveConfig();
            return;
        }

        if (left.TotalHours <= 2 && seedSpoilWarned < 2)
        {
            seedSpoilWarned = 2;
            warn = $"Your full seed vials spoil in {(int)left.TotalMinutes} minutes. Get milked!";
        }
        else if (left.TotalHours <= 24 && seedSpoilWarned < 1)
        {
            seedSpoilWarned = 1;
            warn = $"Your seed vials have been full a while. They spoil in {(int)left.TotalHours} hours.";
        }

        if (warn == null) return;
        Say(warn, 2);
        Plugin.ChatGui.Print(warn, "Eorzea Milkers");
    }

    /// <summary>Pours out spoiled seed: nothing for it, and an off taste that lowers your seed grade for a while.</summary>
    public bool DumpSeed()
    {
        if (!Cow.SeedSpoiled) return Fail("Your seed is fine.");
        var oz = Cow.Seed;
        Cow.Seed = 0;
        Cow.SeedSpoiled = false;
        Cow.SeedFullSinceUtc = null;
        Cow.SeedTaint = Catalog.TaintOnDump;
        F.Stats.TimesDumped++;
        seedSpoilWarned = 0;
        SeedDumpSerial++;
        Say($"You dumped {Units.Oz(oz, "0.##")} of spoiled seed. Your seed grade is one lower until you're well cared for and milked again.", 2);
        plugin.SaveConfig();
        return true;
    }

    private void EaseSeedTaint(float amount)
    {
        if (Cow.SeedTaint <= 0) return;
        Cow.SeedTaint = Math.Max(0, Cow.SeedTaint - amount);
        if (Cow.SeedTaint <= 0) Say("The off taste is gone. Your seed is back to normal.", 1);
    }

    private void AddSeedQuality(float amount)
    {
        var before = Grades.Cap(F.SeedQuality);
        if (amount > 0) amount *= 1f + (Owned("heifer") > 0 ? 0.1f : 0) + (Owned("champion") > 0 ? 0.2f : 0);
        F.SeedQuality += amount;
        var after = Grades.Cap(F.SeedQuality);
        for (var g = before + 1; g <= after; g++)
            Say($"Seed quality up! You can now be a Grade {Grades.Name(g)} bull ({Grades.All[g].Blurb}): more seed, faster milking and better pay.", 1);
    }

    /// <summary>Bull xp ticks in as seed is made, the way cow xp does for milk (the same xp per milk-equivalent oz).</summary>
    private void TickSeedXp()
    {
        var per = Leveling.OzPerXpTick * SeedRatio;
        if (seedMadeSinceTick < per) return;
        var n = (int)(seedMadeSinceTick / per);
        seedMadeSinceTick -= n * per;
        seedGradeExtra += n * (Leveling.GradeBonus(SeedGrade) - 1f);
        var extra = (int)seedGradeExtra;
        seedGradeExtra -= extra;
        AddBullXp(n + extra);
    }

    /// <summary>The seed part of a finished milking: pay, bull xp, quality, the dairy's seed tank. Returns the message part.</summary>
    private string FinishSeed(Session s)
    {
        var hand = s.Kind == SessionKind.Hand;
        var collected = s.ExtractedSeed * s.SeedYield;
        var milkEquivalent = collected / SeedRatio;
        EaseSeedTaint(Catalog.TaintPerMilking);
        AddSeedQuality(milkEquivalent * (hand ? Grades.QualityPerOzHand : Grades.QualityPerOz));

        var paidOz = Math.Clamp(Catalog.DailyPaidOz * SeedRatio - F.DaySeedOz, 0, collected);
        var rateBefore = Catalog.TaperRate(F.DayEarned);
        var pay = Earn((int)Math.Round(paidOz * PayRate(s.SeedGrade) * SeedPriceMult));
        F.Stats.TotalSeedOz += collected;
        F.DaySeedOz += collected;
        EarnDairyXp(milkEquivalent * (hand ? 1.5f : 1f));
        if (Networked && F.NetMember != null && Where.InDairy) pendingSeedPump += collected;
        Cow.SeedLastMilkedUtc = DateTime.UtcNow;

        int xp;
        if (!hand)
            xp = (int)Math.Round(milkEquivalent * Leveling.XpPerOzStall * Leveling.StallXpBonus[Math.Clamp(s.Stall!.Tier, 1, 4)] * Leveling.GradeBonus(s.SeedGrade) *
                                 (Fresh(s.Stall) ? 1f + Catalog.CleanBonus : 1f));
        else
        {
            if (Networked && !s.Hand!.IsTest) Net.HandPay(s.Hand.Name, s.Hand.WorldName, collected, s.SeedGrade, true);   // the farmer's share
            xp = (int)Math.Round(milkEquivalent * Leveling.XpPerOzHand * Leveling.GradeBonus(s.SeedGrade)) + Leveling.XpHandSession;
        }

        AddBullXp(xp);
        return $"{Units.Oz(collected, "0.##")} of seed as a Grade {Grades.Name(s.SeedGrade)} bull. +{pay} coins, +{xp} bull xp" +
               (paidOz < collected - 0.005f ? $" (you've been paid for {Units.Oz(Catalog.DailyPaidOz * SeedRatio, "N0")} of seed today; more still gives xp)"
                   : Catalog.TaperRate(F.DayEarned) < rateBefore || rateBefore < 1f ? " (pay is tapered from what you've earned today)" : "");
    }
}
