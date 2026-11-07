using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using VelvetDairy.Network;

namespace VelvetDairy.Game;

public enum SessionKind { Stall, Hand }

/// <summary>A milking in progress.</summary>
public sealed class Session
{
    public SessionKind Kind;
    public Stall? Stall;
    public Farmhand? Hand;
    public int Grade;
    public float Extracted;        // oz taken from you so far
    public float StartMilk;        // how full you were at the start (for the progress bar)
    public double Started;
    public double LastActivity;    // last stroke (hand) or last second seated (stall)
    public int Strokes;

    public float Yield => Kind == SessionKind.Hand ? Catalog.HandYield : Catalog.StallYield[Math.Clamp(Stall?.Tier ?? 1, 1, 4)];
    public float Progress => StartMilk <= 0 ? 1f : Math.Clamp(Extracted / StartMilk, 0f, 1f);
}

/// <summary>Where you are right now, refreshed every frame.</summary>
public readonly record struct Whereabouts(bool LoggedIn, bool InHouse, bool InDairy, bool Seated, Stall? AtStall, string Why);

/// <summary>Live readout of a farmhand for the Dairy tab (so you can see what the plugin sees).</summary>
public readonly record struct FarmhandSight(bool Present, float Distance, ushort Emote, bool TargetingYou);

public sealed class DairyGame
{
    private readonly Plugin plugin;
    private FarmSave F => plugin.Config.Farm;
    private CowState Cow => F.Cow;

    public Session? Active { get; private set; }
    public Whereabouts Where { get; private set; }
    public event Action<string, int>? Toast;   // message, kind (0 info, 1 good, 2 warn)
    public event Action? Stroked;              // a farmhand stroke drew milk (for the squirt sound)

    private const float BaseRate = 0.07f;  // oz per minute with every need full, before bonuses and the Milk speed setting
    private double now;
    private bool autoHold;                  // after a stall milking, wait for half a bottle before it starts again
    private int spoilWarned;                // 0 none, 1 warned at 24 h left, 2 warned at 2 h left
    private double simAccum;
    private bool caughtUp;
    private readonly Random rng = new();

    // Farmhand watching
    private readonly Dictionary<string, ushort> lastEmote = new();
    private readonly Dictionary<string, double> lastStroke = new();
    private double nextScan;
    private readonly Dictionary<string, IPlayerCharacter?> handObjects = new();
    private double nextTestStroke;

    public DairyGame(Plugin plugin)
    {
        this.plugin = plugin;
    }

    // =================================================================
    // Derived numbers
    // =================================================================

    public int Level => Leveling.FromXp(F.Xp).Level;

    // ----- Settings only the developer can change; everyone else always gets the normal game -----
    public bool Dev => plugin.Dev;
    public float MilkSpeed => Dev ? F.MilkSpeed : 1f;
    public bool Paused => Dev && F.Paused;
    public bool AutoStall => !Dev || F.AutoStall;
    /// <summary>How close to a stall's seat you must be sitting. Tight, so sitting next to it doesn't count.</summary>
    public float StallRange => Dev ? F.StallRange : 0.9f;
    public float HandRange => Dev ? F.HandRange : 3f;
    public bool HandNeedsTarget => !Dev || F.HandNeedsTarget;

    // =================================================================
    // Network mode: when the Velvet Dairy Network is up, your dairy is the one you belong to on the server
    // (as its owner, or invited as a farmer, cow or bull). Without the network everything runs locally as before.
    // =================================================================

    public NetClient Net => plugin.Net;

    /// <summary>The game uses the network's dairies, roles and stalls.</summary>
    public bool Networked => F.NetEnabled && (F.NetMember != null || (Net.Api != null && Net.Last != null));

    /// <summary>Your dairy on the network (kept in the save so it still works if the server is briefly unreachable).</summary>
    public NetMember? Member => Networked ? F.NetMember : null;

    /// <summary>Your role: owner's play role or what you were invited as. Locally everyone is a cow.</summary>
    public string Role => !Networked ? "cow" : F.NetMember?.Role ?? "cow";
    public bool IsOwner => !Networked || F.NetMember?.IsOwner == true;
    public bool IsCow => Role == "cow";
    public bool CanInvite => Networked && F.NetMember != null && (F.NetMember.IsOwner || F.NetMember.Role == "farmer");

    /// <summary>Only cows make and give milk. Bulls and farmers keep their needs but have no bottle (yet).</summary>
    public bool ProducesMilk => IsCow;

    public string DairyName => Networked ? F.NetMember?.Dairy ?? "" : F.Dairy?.Name ?? "";

    private List<Stall> netStalls = new();
    private List<Farmhand> netFarmers = new();

    /// <summary>The stalls you can use: your dairy's (set up by its owner) on the network, your own locally.</summary>
    public List<Stall> Stalls => Networked ? netStalls : F.Stalls!;

    /// <summary>Who can hand milk you: your dairy's farmers on the network, your registered farmhands locally. Plus the dev test farmhand.</summary>
    public List<Farmhand> Farmhands => Networked ? netFarmers : F.Farmhands!;

    /// <summary>Server told us our membership (every heartbeat).</summary>
    public void OnMembership(MembershipInfo? m)
    {
        var before = F.NetMember;
        if (m == null)
        {
            if (before != null)
            {
                F.NetMember = null;
                netStalls = new List<Stall>();
                netFarmers = Dev ? F.Farmhands!.Where(f => f.IsTest).ToList() : new List<Farmhand>();
                Stop("You're no longer in a dairy.");
                plugin.SaveConfig();
            }

            return;
        }

        if (before != null && before.DairyId == m.DairyId && before.Level != m.Level)
        {
            if (m.Level > before.Level) Say($"{m.Dairy} reached dairy level {m.Level}!", 1);
            before.Level = m.Level;
            plugin.SaveConfig();
        }

        if (before == null || before.DairyId != m.DairyId || before.Role != m.Role || before.IsOwner != m.IsOwner || before.Dairy != m.Dairy)
        {
            F.NetMember = new NetMember
            {
                DairyId = m.DairyId, Dairy = m.Dairy, Role = m.Role, IsOwner = m.IsOwner, Level = m.Level,
                HouseId = before?.DairyId == m.DairyId ? before.HouseId : "",
            };
            if (before != null && before.Role != m.Role) Say($"You're now a {m.Role} at {m.Dairy}.", 1);
            if (!ProducesMilk) Stop();
            plugin.SaveConfig();
        }
    }

    /// <summary>Your dairy's details: address, stalls and members (farmers become your hand milkers).</summary>
    public void OnMyDairy(MyDairyReply? r)
    {
        if (r?.Dairy == null || F.NetMember == null) return;
        if (F.NetMember.HouseId != r.Dairy.HouseId)
        {
            F.NetMember.HouseId = r.Dairy.HouseId;
            plugin.SaveConfig();
        }

        // Keep the same Stall objects where possible so a running session isn't cut off by a refresh
        var stalls = new List<Stall>();
        foreach (var si in r.Stalls)
        {
            var s = netStalls.FirstOrDefault(x => x.Id == si.Id) ?? new Stall { Id = si.Id };
            s.Name = si.Name;
            s.X = si.X;
            s.Y = si.Y;
            s.Z = si.Z;
            s.Tier = Math.Clamp(si.Tier, 1, 4);
            stalls.Add(s);
        }

        netStalls = stalls;

        var me = Plugin.ObjectTable.LocalPlayer;
        var myName = me?.Name.TextValue ?? "";
        var farmers = new List<Farmhand>();
        foreach (var m in r.Members.Where(m => m.Role == "farmer" && !m.Name.Equals(myName, StringComparison.OrdinalIgnoreCase)))
        {
            var fh = netFarmers.FirstOrDefault(f => !f.IsTest && f.Name == m.Name && f.WorldName == m.World)
                     ?? new Farmhand { Name = m.Name, WorldName = m.World, WorldId = LookupWorld(m.World) };
            farmers.Add(fh);
        }

        if (Dev) farmers.AddRange(F.Farmhands!.Where(f => f.IsTest));
        netFarmers = farmers;
    }

    private static uint LookupWorld(string name)
    {
        foreach (var w in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>())
            if (w.Name.ExtractText().Equals(name, StringComparison.OrdinalIgnoreCase)) return w.RowId;
        return 0;
    }

    /// <summary>Messages from other parts of the plugin (network results).</summary>
    public void Notify(string text, int kind)
    {
        if (kind == 2) Fail(text);
        else Say(text, kind);
    }

    /// <summary>Applies coins, milk and notices the server was holding for you.</summary>
    public void ApplyInbox(List<InboxItem> items)
    {
        foreach (var i in items)
        {
            switch (i.Kind)
            {
                case "payout":
                    Earn(i.Coins);
                    AddXp(Math.Max(5, i.Coins / 8));
                    break;
                case "refund":
                    F.Coins += i.Coins;
                    break;
                case "milk":
                    var g = Math.Clamp(i.Grade, 0, Grades.Count - 1);
                    F.Tank![g] += i.Oz;
                    break;
            }

            Say(i.Note.Length > 0 ? i.Note : $"Network: {i.Kind}.", i.Kind == "notice" ? 0 : 1);
        }

        if (items.Count > 0) plugin.SaveConfig();
    }
    public int BottleTier => Math.Min(Catalog.BottleSizes.Length - 1, 1 + Owned("bottle"));
    public float BottleSize => Catalog.BottleSizes[BottleTier];
    public string BottleName => Catalog.BottleNames[BottleTier];
    public int BottleCount => 1 + Owned("rack");
    public float Capacity => BottleSize * BottleCount;
    public int FullBottles => Math.Min(BottleCount, (int)((Cow.Milk + 0.01f) / BottleSize));
    public TimeSpan BoostLeft => Cow.BoostUntilUtc > DateTime.UtcNow ? Cow.BoostUntilUtc - DateTime.UtcNow : TimeSpan.Zero;
    public bool Contented => Cow.Hunger >= 75 && Cow.Thirst >= 75 && Cow.Comfort >= 75 && Cow.Mood >= 75;
    public bool Spoiled => Cow.Spoiled;

    /// <summary>How long until full bottles spoil, or null if they aren't all full.</summary>
    public TimeSpan? SpoilsIn => Cow.Spoiled || Cow.FullSinceUtc == null ? null
        : Cow.FullSinceUtc.Value.AddHours(Catalog.SpoilHours) - DateTime.UtcNow;
    public int Owned(string upgrade) => F.Upgrades.GetValueOrDefault(upgrade);
    public int Count(string item) => F.Pantry.GetValueOrDefault(item);
    public float NeedsAverage => (Cow.Hunger + Cow.Thirst + Cow.Comfort + Cow.Mood) / 4f;
    public int CurrentGrade => Grades.For(Cow, false, F.Quality);
    public int GradeCap => Grades.Cap(F.Quality);
    /// <summary>The grade your care would give if your level allowed it.</summary>
    public int CareGrade => Grades.FromState(Cow, false);
    public bool Overfull => Cow.Milk >= Capacity - 0.01f;
    public int MaxStalls => Networked ? Net.Progress?.StallSlots ?? 2 : 1 + Owned("stallkit");
    public float PriceBonus => 1f + (Owned("ribbon") > 0 ? 0.05f : 0) + (Owned("cap") > 0 ? 0.10f : 0);
    public float TankTotal => F.Tank!.Sum();

    public static bool IsLalafell()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return false;
        var c = me.Customize;
        return c.Length > (int)CustomizeIndex.Race && c[(int)CustomizeIndex.Race] == 3;
    }

    /// <summary>Null when you can play; otherwise why not.</summary>
    public string? Blocked()
    {
        if (!F.Confirmed18) return "Confirm you're 18+ in Settings to start.";
        if (Plugin.ObjectTable.LocalPlayer == null) return "Not logged in.";
        if (IsLalafell()) return "Velvet Dairy isn't available for Lalafell characters.";
        return null;
    }

    public sealed record RatePart(string Name, float Multiplier);

    /// <summary>What goes into your milk speed right now, as multipliers.</summary>
    public List<RatePart> RateParts(bool includeResting = true)
    {
        var parts = new List<RatePart>();
        var care = Cow.Hunger < 10 || Cow.Thirst < 10 ? 0.1f : 0.2f + 0.8f * NeedsAverage / 100f;
        parts.Add(new RatePart(Cow.Hunger < 10 || Cow.Thirst < 10 ? "Starving or parched" : "How well you're kept", care));
        if (Level > 1) parts.Add(new RatePart($"Level {Level}", 1f + Catalog.LevelBonus * (Level - 1)));
        if (Contented) parts.Add(new RatePart("Contented (all needs 75+)", 1f + Catalog.ContentBonus));
        if (BoostLeft > TimeSpan.Zero) parts.Add(new RatePart("Treat boost", 1f + Catalog.TreatBonus));
        if (includeResting && Where.InDairy && Where.Seated && Wake == Wakefulness.Awake) parts.Add(new RatePart("Resting in your dairy", 1f + Catalog.RestBonus));
        if (includeResting && Wake == Wakefulness.Dozing) parts.Add(new RatePart("Dozing (no activity for 15 min)", 0.5f));
        if (includeResting && Wake == Wakefulness.Asleep) parts.Add(new RatePart("Asleep (no activity for 30 min)", 0f));
        if (Tired) parts.Add(new RatePart($"Tired ({TiredAfterHours} h milking today)", 0.5f));
        if (Math.Abs(MilkSpeed - 1f) > 0.01f) parts.Add(new RatePart("Milk speed setting", MilkSpeed));
        return parts;
    }

    /// <summary>oz per minute right now.</summary>
    public float ProductionPerMinute() => RateParts().Aggregate(BaseRate, (r, p) => r * p.Multiplier);

    // =================================================================
    // Frame update
    // =================================================================

    // =================================================================
    // Awake, dozing, asleep: no milk for standing around with nobody at the keyboard
    // =================================================================

    public enum Wakefulness { Awake, Dozing, Asleep }

    public const double DozeAfter = 15 * 60;
    public const double SleepAfter = 30 * 60;
    public const int TiredAfterHours = 12;

    private double lastActive;
    private Vector3 lastPos;
    private float lastRot;
    private ulong lastTarget;
    private ushort myLastEmote;
    private Wakefulness lastWake;

    /// <summary>Seconds since you last did something.</summary>
    public double IdleSeconds => now - lastActive;

    public Wakefulness Wake =>
        Dev && !F.DevCanSleep ? Wakefulness.Awake
        : IdleSeconds >= SleepAfter ? Wakefulness.Asleep
        : IdleSeconds >= DozeAfter ? Wakefulness.Dozing
        : Wakefulness.Awake;

    /// <summary>After 12 hours of making milk in one day, milk comes in at half speed until tomorrow.</summary>
    public bool Tired => F.DayAwakeMinutes >= TiredAfterHours * 60;

    /// <summary>Something real happened (a click in the plugin, care, a farmer's stroke...).</summary>
    public void Touch() => lastActive = now;

    /// <summary>Watches for signs someone is playing: moving, turning, targeting, emotes, and the game's own input timer.</summary>
    private void WatchActivity()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return;
        var target = Plugin.TargetManager.Target?.GameObjectId ?? 0;
        var emote = Emotes.Current(me);
        if (Vector3.Distance(me.Position, lastPos) > 0.05f || Math.Abs(me.Rotation - lastRot) > 0.05f || target != lastTarget || emote != myLastEmote)
            Touch();
        lastPos = me.Position;
        lastRot = me.Rotation;
        lastTarget = target;
        myLastEmote = emote;

        var w = Wake;
        if (w != lastWake)
        {
            if (w == Wakefulness.Dozing) Say("You're dozing off. Milk comes in at half speed until you do something.", 2);
            else if (w == Wakefulness.Asleep)
            {
                Stop("You fell asleep.");
                Say("You fell asleep. No milk or XP until you wake up (move, emote or click anything).", 2);
            }
            else if (lastWake == Wakefulness.Asleep) Say("You wake up.", 0);
            lastWake = w;
        }
    }

    public void Update(float dt)
    {
        now += dt;
        Where = Locate();
        WatchActivity();

        if (Blocked() != null || Paused)
        {
            // Not playing (logged out, character select, paused): the spoil clock waits too
            if (Cow.FullSinceUtc != null && !Cow.Spoiled) Cow.FullSinceUtc = Cow.FullSinceUtc.Value.AddSeconds(dt);
            Cow.LastTickUtc = DateTime.UtcNow;
            return;
        }

        if (!caughtUp)
        {
            caughtUp = true;
            CatchUp();
        }

        WatchFarmhands();

        simAccum += dt;
        if (simAccum >= 1.0)
        {
            var step = (float)simAccum;
            simAccum = 0;
            Simulate(step);
            if (Wake == Wakefulness.Awake && ProducesMilk && Cow.Milk < Capacity - 0.01f) F.DayAwakeMinutes += step / 60f;
            Cow.LastTickUtc = DateTime.UtcNow;
        }

        UpdateSession(dt);
    }

    private Whereabouts Locate()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return new Whereabouts(false, false, false, false, null, "Not logged in.");

        var (ward, plot) = Housing.Current();
        var inHouse = ward >= 0;
        var seated = Emotes.InPositionLoop(me);
        var d = F.Dairy;

        string why;
        var inDairy = false;
        if (Networked)
        {
            var m = F.NetMember;
            var here = Estate.Current();
            if (m == null) why = "You aren't in a dairy yet. Register your own estate or accept an invite (Dairy tab).";
            else if (here == null) why = $"You're away from {m.Dairy}.";
            else if (m.HouseId.Length == 0) why = "Loading your dairy...";
            else if (!ulong.TryParse(m.HouseId, out var hid) || !Estate.Same(hid, here.Value.HouseId)) why = $"This isn't {m.Dairy}.";
            else
            {
                inDairy = true;
                why = $"In {m.Dairy}.";
            }
        }
        else if (d == null) why = "You haven't registered a dairy yet (Dairy tab).";
        else if (!inHouse) why = $"You're away from {d.Name}.";
        else if (Plugin.ClientState.TerritoryType != d.Territory || me.CurrentWorld.RowId != d.World || ward != d.Ward || plot != d.Plot)
            why = $"This isn't {d.Name}.";
        else if (F.DairyRadius > 0 && Vector3.Distance(me.Position, new Vector3(d.X, d.Y, d.Z)) > F.DairyRadius)
            why = $"Too far from the dairy floor (over {F.DairyRadius:0} y).";
        else
        {
            inDairy = true;
            why = $"In {d.Name}.";
        }

        Stall? at = null;
        if (inDairy && seated)
        {
            at = Stalls
                .Select(s => (s, dist: Vector3.Distance(me.Position, new Vector3(s.X, s.Y, s.Z))))
                .Where(x => x.dist <= StallRange)
                .OrderBy(x => x.dist)
                .Select(x => x.s)
                .FirstOrDefault();
        }

        return new Whereabouts(true, inHouse, inDairy, seated, at, why);
    }

    /// <summary>
    /// Time spent logged out: no milk is made and the spoil countdown pauses, so a vacation never costs you anything.
    /// Needs drift down gently (a quarter speed, never below 20) so a fresh login still has a little care to do.
    /// </summary>
    private void CatchUp()
    {
        var away = DateTime.UtcNow - Cow.LastTickUtc;
        if (away.TotalMinutes < 1) return;

        // The spoil clock only runs while you're logged in
        if (Cow.FullSinceUtc != null && !Cow.Spoiled) Cow.FullSinceUtc = Cow.FullSinceUtc.Value + away;

        var minutes = (float)Math.Min(away.TotalMinutes, 8 * 60);
        static float Floor(float v, float d) => Math.Max(Math.Min(v, 20), v - d);
        Cow.Hunger = Floor(Cow.Hunger, 0.35f * 0.25f * minutes);
        Cow.Thirst = Floor(Cow.Thirst, 0.5f * 0.25f * minutes);
        Cow.Comfort = Floor(Cow.Comfort, 0.25f * 0.25f * minutes);
        Cow.Mood = Floor(Cow.Mood, 0.2f * 0.25f * minutes);
        Cow.Richness = Math.Max(0, Cow.Richness - 0.2f * 0.25f * minutes);
        Cow.LastTickUtc = DateTime.UtcNow;
        CheckSpoil();
    }

    private void Simulate(float seconds)
    {
        var m = seconds / 60f;
        if (Cow.Milk > Capacity) Cow.Milk = Capacity;
        var fullBefore = FullBottles;
        var resting = Where.InDairy && Where.Seated;
        var cushion = Owned("cushion") > 0;

        Cow.Hunger = Clamp(Cow.Hunger - 0.35f * m);
        Cow.Thirst = Clamp(Cow.Thirst - (Owned("trough") > 0 ? 0.33f : 0.5f) * m);
        if (resting) Cow.Comfort = Clamp(Cow.Comfort + (cushion ? 3.0f : 2.0f) * m);
        else Cow.Comfort = Clamp(Cow.Comfort - (cushion ? 0.12f : 0.25f) * m);

        // Mood drifts toward how well you're kept, and drops a little on its own
        var target = (Cow.Hunger + Cow.Thirst + Cow.Comfort) / 3f;
        var moodDecay = Owned("bell") > 0 ? 0.17f : 0.2f;
        Cow.Mood = Clamp(Cow.Mood - moodDecay * m + (target - Cow.Mood) * 0.01f * m);
        Cow.Richness = Math.Max(0, Cow.Richness - 0.2f * m);

        if (Overfull)
        {
            Cow.Comfort = Clamp(Cow.Comfort - 1.0f * m);
            Cow.Mood = Clamp(Cow.Mood - 0.5f * m);
        }
        else if (Active == null && ProducesMilk)
        {
            Cow.Milk = Math.Min(Capacity, Cow.Milk + ProductionPerMinute() * m);
            if (FullBottles > fullBefore)
                Say(FullBottles >= BottleCount ? "All your bottles are full. Time to get milked." : $"Bottle {FullBottles} of {BottleCount} is full.", 0);
        }

        // Spoiling: the clock runs while every bottle is full
        if (Overfull && Cow.FullSinceUtc == null) Cow.FullSinceUtc = DateTime.UtcNow;
        else if (!Overfull && !Cow.Spoiled) Cow.FullSinceUtc = null;
        CheckSpoil();

        RollDay();
        RefillOrders();
        CheckAwards();
    }

    private static float Clamp(float v) => Math.Clamp(v, 0f, 100f);

    // =================================================================
    // Care (dairy only)
    // =================================================================

    private double waterReady;
    private double stretchReady;

    public string? CareProblem()
    {
        var b = Blocked();
        if (b != null) return b;
        if (!Where.InDairy) return "Care is only possible inside your dairy. " + Where.Why;
        return null;
    }

    public bool UseFeed(string id)
    {
        var item = Catalog.FeedById(id);
        if (item == null) return false;
        var p = CareProblem();
        if (p != null) return Fail(p);
        if (Count(id) <= 0) return Fail($"You're out of {item.Name}. Buy more in the Shop.");

        if (item.Quality > 0 && QualityTreatWait > TimeSpan.Zero)
            return Fail($"You can have another quality treat in {Wait(QualityTreatWait)}.");
        var useful = (item.Hunger > 0 && Cow.Hunger < 95) || (item.Mood > 0 && Cow.Mood < 95) || item.Richness >= 10 || item.Quality > 0;
        if (!useful) return Fail("You don't feel like that right now.");

        F.Pantry[id] = Count(id) - 1;
        Cow.Hunger = Clamp(Cow.Hunger + item.Hunger);
        Cow.Thirst = Clamp(Cow.Thirst + item.Thirst);
        Cow.Comfort = Clamp(Cow.Comfort + item.Comfort);
        Cow.Mood = Clamp(Cow.Mood + item.Mood);
        Cow.Richness = Math.Min(100, Cow.Richness + item.Richness);
        if (item.Quality > 0)
        {
            AddQuality(item.Quality);
            Cow.QualityTreatReadyUtc = DateTime.UtcNow.AddHours(Grades.QualityTreatHours);
        }
        if (item.BoostMinutes > 0)
        {
            // Boosts don't stack past an hour
            var from = Cow.BoostUntilUtc > DateTime.UtcNow ? Cow.BoostUntilUtc : DateTime.UtcNow;
            var until = from.AddMinutes(item.BoostMinutes);
            var cap = DateTime.UtcNow.AddMinutes(60);
            Cow.BoostUntilUtc = until > cap ? cap : until;
        }

        CareDone($"You eat some {item.Name.ToLowerInvariant()}." + (item.BoostMinutes > 0 ? $" Milk boost for {item.BoostMinutes} min." : ""));
        return true;
    }

    /// <summary>Time left before you can eat another quality treat (zero when ready).</summary>
    public TimeSpan QualityTreatWait => Cow.QualityTreatReadyUtc > DateTime.UtcNow ? Cow.QualityTreatReadyUtc - DateTime.UtcNow : TimeSpan.Zero;

    public static string Wait(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m" : $"{Math.Max(1, t.Seconds)}s";

    public double WaterCooldown => Math.Max(0, waterReady - now);
    public double StretchCooldown => Math.Max(0, stretchReady - now);

    public bool Drink()
    {
        var p = CareProblem();
        if (p != null) return Fail(p);
        if (WaterCooldown > 0) return Fail("Give it a moment.");
        if (Cow.Thirst >= 95) return Fail("You're not thirsty.");
        Cow.Thirst = Clamp(Cow.Thirst + 40);
        waterReady = now + 20;
        CareDone("You drink from the trough.");
        return true;
    }

    public bool Stretch()
    {
        var p = CareProblem();
        if (p != null) return Fail(p);
        if (StretchCooldown > 0) return Fail("You stretched a moment ago.");
        Cow.Comfort = Clamp(Cow.Comfort + 12);
        Cow.Mood = Clamp(Cow.Mood + 4);
        stretchReady = now + 300;
        CareDone("You stretch out and shake off the stiffness.");
        return true;
    }

    private void CareDone(string text)
    {
        Touch();
        EaseTaint(Catalog.TaintPerCare);
        F.Stats.CareTasks++;
        F.DayCare++;
        AddXp(3);
        EarnDairyXp(2);
        Say(text, 0);
        plugin.SaveConfig();
    }

    // =================================================================
    // Milking
    // =================================================================

    private void CheckSpoil()
    {
        if (Cow.Spoiled || Cow.FullSinceUtc == null)
        {
            if (!Cow.Spoiled) spoilWarned = 0;
            return;
        }

        var left = Cow.FullSinceUtc.Value.AddHours(Catalog.SpoilHours) - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            Cow.Spoiled = true;
            Stop("Your milk spoiled.");
            Say("Your milk has spoiled. Dump it from the HUD or the Barn tab before you can be milked again.", 2);
            Plugin.ChatGui.Print("Your milk has spoiled. Dump it from the HUD or the Barn tab.", "Velvet Dairy");
            plugin.SaveConfig();
        }
        else if (left.TotalHours <= 2 && spoilWarned < 2)
        {
            spoilWarned = 2;
            Say($"Your full bottles spoil in {(int)left.TotalMinutes} minutes. Get milked!", 2);
            Plugin.ChatGui.Print($"Your full bottles spoil in {(int)left.TotalMinutes} minutes. Get milked!", "Velvet Dairy");
        }
        else if (left.TotalHours <= 24 && spoilWarned < 1)
        {
            spoilWarned = 1;
            Say($"Your bottles have been full a while. They spoil in {(int)left.TotalHours} hours.", 2);
            Plugin.ChatGui.Print($"Your bottles have been full a while. They spoil in {(int)left.TotalHours} hours.", "Velvet Dairy");
        }
    }

    /// <summary>Pours out spoiled milk. You get nothing for it, and it leaves an off taste that lowers your grade for a while.</summary>
    public bool DumpMilk()
    {
        if (!Cow.Spoiled) return Fail("Your milk is fine.");
        var oz = Cow.Milk;
        Cow.Milk = 0;
        Cow.Spoiled = false;
        Cow.FullSinceUtc = null;
        Cow.Taint = Catalog.TaintOnDump;
        Cow.Mood = Clamp(Cow.Mood - 10);
        F.Stats.TimesDumped++;
        F.Stats.OzDumped += oz;
        spoilWarned = 0;
        DumpSerial++;
        Say($"You dumped {oz:0.#} oz of spoiled milk. Your milk will taste off until you're well cared for, milked and delivering again.", 2);
        plugin.SaveConfig();
        return true;
    }

    /// <summary>Goes up each time milk is dumped, so the windows snap the bottle to empty instead of easing down.</summary>
    public int DumpSerial { get; private set; }

    private void EaseTaint(float amount)
    {
        if (Cow.Taint <= 0) return;
        Cow.Taint = Math.Max(0, Cow.Taint - amount);
        if (Cow.Taint <= 0) Say("The off taste is gone. Your milk is back to normal.", 1);
    }

    public string? StallProblem()
    {
        var p = CareProblem();
        if (p != null) return p.Replace("Care is", "Milking is");
        if (!ProducesMilk) return $"Only cows are milked. You're a {Role} here.";
        if (Cow.Spoiled) return "Your milk has spoiled. Dump it first.";
        if (Stalls.Count == 0) return Networked && !IsOwner ? "Your dairy's owner hasn't set up any stalls yet." : "Register a stall first (Dairy tab).";
        if (!Where.Seated) return "Sit down in one of your stalls.";
        if (Wake == Wakefulness.Asleep) return "You're asleep. Move, emote or click anything to wake up.";
        if (Where.AtStall == null) return Where.Seated ? "Sit on a stall's own seat to be milked (you're sitting somewhere else)." : "Sit on one of the dairy's stalls to be milked.";
        if (Cow.Milk < 0.5f) return "You don't have enough milk yet.";
        return null;
    }

    public bool StartStall()
    {
        if (Active != null) return false;
        var p = StallProblem();
        if (p != null) return Fail(p);
        Begin(SessionKind.Stall, Where.AtStall, null);
        Say($"The {Catalog.StallTierNames[Where.AtStall!.Tier].ToLowerInvariant()} starts milking you.", 0);
        return true;
    }

    public string? HandProblem(Farmhand fh)
    {
        var p = CareProblem();
        if (p != null) return p.Replace("Care is", "Milking is");
        if (!ProducesMilk) return $"Only cows are milked. You're a {Role} here.";
        if (Wake == Wakefulness.Asleep) return "You're asleep. Move, emote or click anything to wake up.";
        if (Cow.Spoiled) return "Your milk has spoiled. Dump it first.";
        if (Cow.Milk < 0.5f) return "You don't have enough milk yet.";
        if (fh.IsTest) return Dev ? null : "The test farmhand only works in developer mode.";
        var sight = Sight(fh);
        if (!sight.Present) return $"{fh.Name} isn't here.";
        if (sight.Distance > HandRange) return $"{fh.Name} needs to be within {HandRange:0.#} y of you.";
        return null;
    }

    /// <summary>Starts a hand milking. It only moves while the farmhand does one of the milking emotes on you.</summary>
    public bool StartHand(Farmhand fh)
    {
        if (Active != null) return false;
        var p = HandProblem(fh);
        if (p != null) return Fail(p);
        Begin(SessionKind.Hand, null, fh);
        nextTestStroke = now + 1.5;
        var emotes = string.Join(", ", F.HandEmotes!);
        Say(fh.IsTest
            ? "The test farmhand starts milking you."
            : $"{fh.Name} can milk you now. Each {emotes} on you draws milk.", 0);
        return true;
    }

    private void Begin(SessionKind kind, Stall? stall, Farmhand? fh)
    {
        Active = new Session
        {
            Kind = kind,
            Stall = stall,
            Hand = fh,
            Grade = Grades.For(Cow, kind == SessionKind.Hand, F.Quality),
            StartMilk = Cow.Milk,
            Started = now,
            LastActivity = now,
        };
    }

    public void Stop(string? reason = null)
    {
        if (Active == null) return;
        Finish(reason);
    }

    private void UpdateSession(float dt)
    {
        var s = Active;

        // Sitting in a stall starts it by itself
        if (s == null)
        {
            if (Where.AtStall == null || Cow.Milk >= Math.Min(BottleSize * 0.5f, Capacity)) autoHold = false;
            if (AutoStall && !autoHold && Where.AtStall != null && Cow.Milk >= 1 && Wake == Wakefulness.Awake && StallProblem() == null) StartStall();
            return;
        }

        if (!Where.InDairy)
        {
            Finish("You left the dairy.");
            return;
        }

        if (s.Kind == SessionKind.Stall)
        {
            if (Where.AtStall == s.Stall) s.LastActivity = now;
            else if (now - s.LastActivity > 1)
            {
                Finish("You got up.");
                return;
            }
            else return;

            Draw(Catalog.StallSpeed[Math.Clamp(s.Stall!.Tier, 1, 4)] * dt);
        }
        else
        {
            if (s.Hand!.IsTest && now >= nextTestStroke)
            {
                nextTestStroke = now + 2.5;
                Stroke(s.Hand);
            }

            if (now - s.LastActivity > 45)
            {
                Finish("Your farmhand stopped.");
                return;
            }
        }

        if (Cow.Milk < 0.1f) Finish(null);
    }

    private void Draw(float oz)
    {
        if (Active == null || oz <= 0) return;
        oz = Math.Min(oz, Cow.Milk);
        Cow.Milk -= oz;
        Active.Extracted += oz;
    }

    private void Stroke(Farmhand fh)
    {
        var s = Active;
        if (s == null || s.Kind != SessionKind.Hand || s.Hand != fh) return;
        s.Strokes++;
        s.LastActivity = now;
        Draw(Catalog.HandStrokeOz);
        Stroked?.Invoke();
    }

    private void Finish(string? reason)
    {
        var s = Active;
        Active = null;
        if (s == null) return;
        autoHold = true;

        if (s.Extracted < 0.05f)
        {
            Say(reason != null ? $"Milking stopped. {reason}" : "Milking stopped.", 2);
            return;
        }

        var collected = s.Extracted * s.Yield;
        EaseTaint(Catalog.TaintPerMilking);
        AddQuality(collected * (s.Kind == SessionKind.Hand ? Grades.QualityPerOzHand : Grades.QualityPerOz));
        F.Tank![s.Grade] += collected;
        F.Stats.TotalOz += collected;
        F.DayOz += collected;
        F.Stats.BestGrade = Math.Max(F.Stats.BestGrade, s.Grade);
        EarnDairyXp(collected * (s.Kind == SessionKind.Hand ? 1.5f : 1f));
        F.Stats.BestSessionOz = Math.Max(F.Stats.BestSessionOz, collected);

        // Being milked feels good and resets the pressure
        Cow.Comfort = Clamp(Cow.Comfort + Math.Min(15, s.Extracted * 0.4f));
        Cow.Mood = Clamp(Cow.Mood + (s.Kind == SessionKind.Hand ? 10 : 4));

        int xp;
        if (s.Kind == SessionKind.Stall)
        {
            F.Stats.StallSessions++;
            s.Stall!.Sessions++;
            xp = (int)Math.Round(collected * 5f);
        }
        else
        {
            F.Stats.HandSessions++;
            F.DayHand++;
            s.Hand!.Sessions++;
            s.Hand.OzMilked += collected;
            xp = (int)Math.Round(collected * 7.5f) + 10;
        }

        AddXp(xp);
        var by = s.Kind == SessionKind.Hand ? $" by {(s.Hand!.IsTest ? "the test farmhand" : s.Hand.Name)}" : "";
        Say($"Milked{by}: {collected:0.#} oz of Grade {Grades.Name(s.Grade)} milk. +{xp} xp" + (reason != null ? $" ({reason})" : ""), 1);
        plugin.SaveConfig();
    }

    // =================================================================
    // Farmhands
    // =================================================================

    public Farmhand? FindFarmhand(string name, uint world) =>
        Farmhands.FirstOrDefault(f => !f.IsTest && f.WorldId == world && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public string? RegisterProblem()
    {
        if (Blocked() != null) return Blocked();
        if (Networked) return "On the network, farmers join by invite (Dairy tab or right-click them).";
        if (F.Dairy == null) return "Register your dairy first.";
        if (!Where.InDairy) return "Farmhands are signed on inside your dairy. " + Where.Why;
        return null;
    }

    public bool AddFarmhand(string name, uint worldId, string worldName)
    {
        var p = RegisterProblem();
        if (p != null) return Fail(p);
        if (FindFarmhand(name, worldId) != null) return Fail($"{name} already works at your dairy.");
        F.Farmhands!.Add(new Farmhand { Name = name, WorldId = worldId, WorldName = worldName });
        Say($"{name} is now a registered farmhand at {F.Dairy!.Name}.", 1);
        plugin.SaveConfig();
        return true;
    }

    public bool AddFarmhandFromTarget()
    {
        if (Plugin.TargetManager.Target is not IPlayerCharacter pc) return Fail("Target the player first.");
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me != null && pc.GameObjectId == me.GameObjectId) return Fail("You can't be your own farmhand.");
        return AddFarmhand(pc.Name.TextValue, pc.HomeWorld.RowId, pc.HomeWorld.ValueNullable?.Name.ExtractText() ?? "");
    }

    public void AddTestFarmhand()
    {
        if (!Dev || F.Farmhands!.Any(f => f.IsTest)) return;
        var test = new Farmhand { Name = "Test farmhand", IsTest = true };
        F.Farmhands!.Add(test);
        if (Networked) netFarmers.Add(test);
        plugin.SaveConfig();
    }

    public void RemoveFarmhand(Farmhand fh)
    {
        if (Active?.Hand == fh) Stop("Farmhand removed.");
        F.Farmhands!.Remove(fh);
        netFarmers.Remove(fh);
        plugin.SaveConfig();
    }

    private IPlayerCharacter? HandObject(Farmhand fh)
    {
        if (fh.IsTest) return null;
        return handObjects.GetValueOrDefault(Key(fh));
    }

    private static string Key(Farmhand fh) => $"{fh.Name}@{fh.WorldId}";

    public FarmhandSight Sight(Farmhand fh)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        var o = HandObject(fh);
        if (me == null || o == null) return new FarmhandSight(false, 0, 0, false);
        return new FarmhandSight(true, Vector3.Distance(me.Position, o.Position), Emotes.Current(o), o.TargetObjectId == me.GameObjectId);
    }

    public HashSet<ushort> HandEmoteIds() => F.HandEmotes!.Select(Emotes.Find).Where(id => id != 0).ToHashSet();

    /// <summary>Adds whatever emote this farmhand is doing right now to the milking emotes.</summary>
    public bool RecordEmote(Farmhand fh)
    {
        var sight = Sight(fh);
        if (!sight.Present || sight.Emote == 0) return Fail("They aren't doing an emote right now.");
        var cmd = Emotes.Command(sight.Emote);
        if (cmd.Length == 0) return Fail("That emote has no text command.");
        if (F.HandEmotes!.Any(e => e.Equals(cmd, StringComparison.OrdinalIgnoreCase))) return Fail($"{cmd} is already a milking emote.");
        F.HandEmotes!.Add(cmd);
        Say($"{cmd} now counts as a milking emote.", 1);
        plugin.SaveConfig();
        return true;
    }

    /// <summary>Finds your farmhands nearby and turns their emotes into strokes.</summary>
    private void WatchFarmhands()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return;

        if (now >= nextScan)
        {
            nextScan = now + 0.5;
            handObjects.Clear();
            var wanted = Farmhands.Where(f => !f.IsTest).ToList();
            if (wanted.Count > 0)
            {
                foreach (var o in Plugin.ObjectTable)
                {
                    if (o is not IPlayerCharacter pc || pc.GameObjectId == me.GameObjectId) continue;
                    var fh = wanted.FirstOrDefault(f => f.WorldId == pc.HomeWorld.RowId && f.Name.Equals(pc.Name.TextValue, StringComparison.OrdinalIgnoreCase));
                    if (fh != null) handObjects[Key(fh)] = pc;
                }
            }
        }

        var s = Active;
        if (s?.Kind != SessionKind.Hand || s.Hand!.IsTest) return;

        var fhand = s.Hand;
        var key = Key(fhand);
        var sight = Sight(fhand);
        var prev = lastEmote.GetValueOrDefault(key);
        lastEmote[key] = sight.Emote;

        if (!sight.Present || sight.Emote == 0 || sight.Distance > HandRange) return;
        if (HandNeedsTarget && !sight.TargetingYou) return;
        if (!HandEmoteIds().Contains(sight.Emote)) return;

        // A new emote counts at once; one that keeps going counts again every 2.5 seconds
        var last = lastStroke.GetValueOrDefault(key, -99);
        if (prev != sight.Emote || now - last >= 2.5)
        {
            lastStroke[key] = now;
            Stroke(fhand);
        }
    }

    // =================================================================
    // Dairy and stalls
    // =================================================================

    /// <summary>Null when you can register the house you're standing in; otherwise why not.</summary>
    public string? DairyHereProblem()
    {
        if (Blocked() != null) return Blocked();
        var here = Estate.Current();
        if (here == null) return "Go inside the house you want to use as your dairy.";
        if (!Estate.IsOwnEstate(here.Value) && !Dev) return "You can only register your own estate: your house, your FC's house, your apartment or a shared estate.";
        if (Networked && F.NetMember != null && !F.NetMember.IsOwner) return $"You're a member of {F.NetMember.Dairy}. Leave it first to start your own dairy.";
        return null;
    }

    /// <summary>Registers your own estate as your dairy (on the network when it's up, otherwise locally).</summary>
    public bool RegisterDairyHere(string name)
    {
        var p = DairyHereProblem();
        if (p != null) return Fail(p);
        var me = Plugin.ObjectTable.LocalPlayer!;
        var here = Estate.Current()!.Value;
        var clean = string.IsNullOrWhiteSpace(name) ? "My Dairy" : name.Trim();

        if (Networked)
        {
            PublishDairy(clean, here);
            return true;
        }

        var (ward, plot) = Housing.Current();
        var moved = F.Dairy != null;
        F.Dairy = new DairyPlace
        {
            Name = clean,
            Territory = Plugin.ClientState.TerritoryType,
            World = me.CurrentWorld.RowId,
            WorldName = me.CurrentWorld.ValueNullable?.Name.ExtractText() ?? "",
            Ward = ward,
            Plot = plot,
            X = me.Position.X,
            Y = me.Position.Y,
            Z = me.Position.Z,
        };
        if (moved)
        {
            F.Stalls!.Clear(); // old stall spots belong to the old house
            Stop("Dairy moved.");
        }

        Say($"{F.Dairy.Name} is registered ({here.Describe}).", 1);
        plugin.SaveConfig();
        return true;
    }

    /// <summary>Sends your dairy and its listing settings to the network. Also used to save listing changes.</summary>
    public void PublishDairy(string name, Address? at = null)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return;
        Address addr;
        if (at.HasValue) addr = at.Value;
        else if (F.NetMember?.HouseId is { Length: > 0 } h && ulong.TryParse(h, out var hid))
            addr = Estate.FromId(hid);
        else
        {
            Fail("Go to your dairy to save it.");
            return;
        }

        var world = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRowOrDefault(addr.World);
        var body = new
        {
            houseId = addr.HouseId.ToString(),
            name,
            description = F.ListDescription,
            isPublic = F.ListPublic,
            showOwner = F.ListShowName,
            ownerName = me.Name.TextValue,
            world = addr.World,
            worldName = world?.Name.ExtractText() ?? "",
            dcName = world?.DataCenter.ValueNullable?.Name.ExtractText() ?? "",
            district = addr.District,
            districtKind = addr.DistrictKind,
            ward = addr.Ward,
            plot = addr.Plot,
            room = addr.Room,
            role = F.OwnerRole,
        };
        Net.PutDairy(body, _ =>
        {
            Say(F.NetMember?.IsOwner == true ? $"{name} is updated on the network." : $"{name} is registered on the network. You own it.", 1);
        }, e => Fail(e));
    }

    public void RenameDairy(string name)
    {
        if (F.Dairy == null || string.IsNullOrWhiteSpace(name)) return;
        F.Dairy.Name = name.Trim();
        plugin.SaveConfig();
    }

    public string? StallHereProblem()
    {
        if (Networked)
        {
            if (F.NetMember == null) return "Join or register a dairy first.";
            if (!F.NetMember.IsOwner) return "Only the dairy's owner sets up stalls.";
        }
        else if (F.Dairy == null) return "Register your dairy first.";
        if (!Where.InDairy) return "Stalls go inside your dairy. " + Where.Why;
        if (!Where.Seated) return "Sit on the furniture you want to use as a stall.";
        if (Where.AtStall != null) return $"That's already {Where.AtStall.Name}.";
        if (Networked)
        {
            if (Stalls.Count >= MaxStalls) return $"Your dairy has room for {MaxStalls} stall(s) at level {DairyLevel}. Level up the dairy for more.";
            var price = Net.Progress?.NextStallPrice ?? 0;
            if (price > (Net.Progress?.Fund ?? 0)) return $"The next stall costs {price} coins from the dairy fund.";
        }
        else if (Stalls.Count >= MaxStalls) return $"You have {MaxStalls} stall(s). Buy a Stall kit for more.";
        return null;
    }

    public bool AddStallHere()
    {
        var p = StallHereProblem();
        if (p != null) return Fail(p);
        var me = Plugin.ObjectTable.LocalPlayer!;
        if (Networked)
        {
            var name = $"Stall {Stalls.Count + 1}";
            Net.AddStall(name, me.Position.X, me.Position.Y, me.Position.Z, MaxStalls, () => Say($"{name} is set up here for everyone in your dairy.", 1), e => Fail(e));
            return true;
        }

        var stall = new Stall { Name = $"Stall {F.Stalls!.Count + 1}", X = me.Position.X, Y = me.Position.Y, Z = me.Position.Z };
        F.Stalls.Add(stall);
        Say($"{stall.Name} is set up here.", 1);
        plugin.SaveConfig();
        return true;
    }

    public void RemoveStall(Stall s)
    {
        if (Active?.Stall == s) Stop("Stall removed.");
        if (Networked)
        {
            if (!IsOwner)
            {
                Fail("Only the dairy's owner can remove stalls.");
                return;
            }

            Net.RemoveStall(s.Id, e => Fail(e));
            return;
        }

        F.Stalls!.Remove(s);
        plugin.SaveConfig();
    }

    // =================================================================
    // Shop
    // =================================================================

    public string? BuyFeedProblem(FeedItem item, int qty)
    {
        if (Blocked() != null) return Blocked();
        if (Level < item.Unlock) return $"Unlocks at level {item.Unlock}.";
        if (F.Coins < item.Price * qty) return "Not enough coins.";
        return null;
    }

    public bool BuyFeed(FeedItem item, int qty)
    {
        var p = BuyFeedProblem(item, qty);
        if (p != null) return Fail(p);
        F.Coins -= item.Price * qty;
        F.Pantry[item.Id] = Count(item.Id) + qty;
        Say($"Bought {qty} x {item.Name}.", 0);
        plugin.SaveConfig();
        return true;
    }

    /// <summary>What the shop gives back for one: half price, rounded down, at least 1.</summary>
    public static int SellBackPrice(FeedItem item) => Math.Max(1, item.Price / 2);

    public bool SellFeed(FeedItem item, int qty)
    {
        if (Blocked() != null) return Fail(Blocked()!);
        qty = Math.Min(qty, Count(item.Id));
        if (qty <= 0) return Fail($"You don't have any {item.Name}.");
        F.Pantry[item.Id] = Count(item.Id) - qty;
        var coins = SellBackPrice(item) * qty;
        F.Coins += coins;   // not counted as earnings
        Say($"Sold {qty} x {item.Name} back for {coins} coins.", 0);
        plugin.SaveConfig();
        return true;
    }

    public void SetFeedHidden(FeedItem item, bool hidden)
    {
        F.HiddenFeed!.Remove(item.Id);
        if (hidden) F.HiddenFeed.Add(item.Id);
        plugin.SaveConfig();
    }

    public string? UpgradeProblem(Upgrade u)
    {
        if (Blocked() != null) return Blocked();
        var have = Owned(u.Id);
        if (have >= u.MaxLevel) return "Owned.";
        if (Level < u.Unlocks[have]) return $"Unlocks at level {u.Unlocks[have]}.";
        if (F.Coins < u.Prices[have]) return "Not enough coins.";
        return null;
    }

    public bool BuyUpgrade(Upgrade u)
    {
        var p = UpgradeProblem(u);
        if (p != null) return Fail(p);
        var have = Owned(u.Id);
        F.Coins -= u.Prices[have];
        F.Upgrades[u.Id] = have + 1;
        Say($"Bought {u.Name}{(u.MaxLevel > 1 ? $" {have + 1}" : "")}.", 1);
        plugin.SaveConfig();
        return true;
    }

    public string? StallUpgradeProblem(Stall s)
    {
        if (Blocked() != null) return Blocked();
        if (Networked && !IsOwner) return "Only the dairy's owner upgrades stalls.";
        if (s.Tier >= 4) return "Fully upgraded.";
        var next = s.Tier + 1;
        if (Networked)
        {
            var p = Net.Progress;
            if (p == null) return "Loading your dairy...";
            if (next > p.MaxTier) return $"Your dairy needs level {(next >= 4 ? 12 : 5)} for this.";
            if (p.Fund < p.TierPrice(next)) return $"Needs {p.TierPrice(next)} coins in the dairy fund.";
            return null;
        }

        if (Level < Catalog.StallTierUnlock[next]) return $"Unlocks at level {Catalog.StallTierUnlock[next]}.";
        if (F.Coins < Catalog.StallTierPrice[next]) return "Not enough coins.";
        return null;
    }

    public bool UpgradeStall(Stall s)
    {
        var p = StallUpgradeProblem(s);
        if (p != null) return Fail(p);
        var next = s.Tier + 1;
        var price = Catalog.StallTierPrice[next];
        if (Networked)
        {
            // Paid from the dairy fund on the server
            Net.UpgradeStall(s.Id, next, () => Say($"{s.Name} is now a {Catalog.StallTierNames[next].ToLowerInvariant()} (paid from the dairy fund).", 1), e => Fail(e));
            return true;
        }

        s.Tier = next;
        F.Coins -= price;
        Say($"{s.Name} is now a {Catalog.StallTierNames[s.Tier].ToLowerInvariant()}.", 1);
        plugin.SaveConfig();
        return true;
    }

    // =================================================================
    // Creamery
    // =================================================================

    public int BottlePrice(int grade) => (int)Math.Round(Catalog.BottleOz * Grades.All[grade].Price * PriceBonus);
    public int Bottles(int grade) => (int)(F.Tank![grade] / Catalog.BottleOz);

    public bool SellBottles(int grade, int count)
    {
        if (Blocked() != null) return Fail(Blocked()!);
        count = Math.Min(count, Bottles(grade));
        if (count <= 0) return Fail("You need at least a cup (8 oz) to sell.");
        F.Tank![grade] -= count * Catalog.BottleOz;
        var coins = BottlePrice(grade) * count;
        Earn(coins);
        F.Stats.BottlesSold += count;
        Say($"Sold {count} cup{(count == 1 ? "" : "s")} of Grade {Grades.Name(grade)} for {coins} coins.", 1);
        plugin.SaveConfig();
        return true;
    }

    public float OzAtOrAbove(int grade) => F.Tank!.Skip(grade).Sum();

    private void RefillOrders()
    {
        var orders = F.Orders!;
        if (orders.Count >= 3) return;

        // The very first time, the board starts full
        if (orders.Count == 0 && F.NextOrderUtc == DateTime.MinValue)
        {
            for (var i = 0; i < 3; i++) orders.Add(NewOrder());
            F.NextOrderUtc = DateTime.UtcNow;
            return;
        }

        // After that, one new order arrives every 20 minutes while there's room
        if (DateTime.UtcNow < F.NextOrderUtc) return;
        orders.Add(NewOrder());
        F.NextOrderUtc = DateTime.UtcNow.AddMinutes(20);
    }

    private Order NewOrder()
    {
        var lvl = Level;
        var maxGrade = GradeCap;   // orders only ask for grades you can make
        var grade = rng.Next(0, maxGrade + 1);
        var oz = 4 * rng.Next(2, 3 + Math.Min(6, lvl / 3));
        var coins = (int)Math.Round(oz * Grades.All[grade].Price * 1.6f) + 10;
        return new Order
        {
            Id = ++F.OrderSerial,
            Customer = Catalog.Customers[rng.Next(Catalog.Customers.Length)],
            Oz = oz,
            MinGrade = grade,
            Coins = coins,
            Xp = (int)(oz * 2.5f) + 15 + grade * 10,
        };
    }

    public bool CanFill(Order o) => OzAtOrAbove(o.MinGrade) >= o.Oz;

    public bool Fill(Order o)
    {
        if (Blocked() != null) return Fail(Blocked()!);
        if (!CanFill(o)) return Fail($"You need {o.Oz} oz of Grade {Grades.Name(o.MinGrade)} or better.");

        // Use the lowest grade that still qualifies first
        var left = (float)o.Oz;
        for (var g = o.MinGrade; g < Grades.Count && left > 0; g++)
        {
            var take = Math.Min(left, F.Tank![g]);
            F.Tank[g] -= take;
            left -= take;
        }

        F.Orders!.Remove(o);
        Earn(o.Coins);
        AddXp(o.Xp);
        F.Stats.OrdersFilled++;
        F.DayOrders++;
        EaseTaint(Catalog.TaintPerOrder);
        AddQuality(Grades.QualityPerOrder);
        if (F.Orders.Count < 3 && DateTime.UtcNow >= F.NextOrderUtc) F.NextOrderUtc = DateTime.UtcNow.AddMinutes(20);
        Say($"{o.Customer} paid {o.Coins} coins. +{o.Xp} xp", 1);
        plugin.SaveConfig();
        return true;
    }

    public void Decline(Order o)
    {
        F.Orders!.Remove(o);
        F.NextOrderUtc = DateTime.UtcNow.AddMinutes(20);
        plugin.SaveConfig();
    }

    // =================================================================
    // Network: members, invites, buy orders, deliveries, visitor orders
    // =================================================================

    // ----- Dairy levels: members earn XP together; the shared fund pays for stalls -----

    private float pendingDairyXp;

    public int DairyLevel => Net.Progress?.Level ?? F.NetMember?.Level ?? 1;

    /// <summary>XP for your dairy from what you do inside it (the server checks you're there and caps it per day).</summary>
    private void EarnDairyXp(float amount)
    {
        if (Networked && F.NetMember != null && Where.InDairy && Wake == Wakefulness.Awake) pendingDairyXp += amount;
    }

    /// <summary>Hands the next heartbeat what's built up (whole points, at most 60).</summary>
    public int TakeDairyXp()
    {
        var n = (int)Math.Min(60, Math.Floor(pendingDairyXp));
        pendingDairyXp = Math.Max(0, pendingDairyXp - n);
        if (pendingDairyXp > 600) pendingDairyXp = 600;
        return n;
    }

    public string? DonateProblem(int coins)
    {
        if (!Networked || F.NetMember == null) return "Join a dairy first.";
        if (coins < 1) return "Donate at least 1 coin.";
        if (coins > F.Coins) return "You don't have that many coins.";
        return null;
    }

    public void Donate(int coins)
    {
        var p = DonateProblem(coins);
        if (p != null)
        {
            Fail(p);
            return;
        }

        F.Coins -= coins;
        plugin.SaveConfig();
        Net.Donate(coins, fund => Say($"You put {coins} coins into {DairyName}'s fund ({fund:N0} now).", 1), e =>
        {
            F.Coins += coins;
            plugin.SaveConfig();
            Fail(e);
        });
    }

    /// <summary>Roles you can invite: owners invite farmers, cows and bulls; farmers invite cows and bulls.</summary>
    public string[] InvitableRoles => !CanInvite ? Array.Empty<string>() : F.NetMember!.IsOwner ? new[] { "cow", "bull", "farmer" } : new[] { "cow", "bull" };

    public void InvitePlayer(string name, uint world, string role)
    {
        if (!InvitableRoles.Contains(role))
        {
            Fail(F.NetMember?.IsOwner == true ? "You can't invite that role." : "Only owners and farmers can invite, and only owners can invite farmers.");
            return;
        }

        Net.Invite(name, world, role, () => Say($"Invited {name} to {DairyName} as a {role}. They'll see it next time their plugin checks in.", 1), e => Fail(e));
    }

    public MemberInfo? MemberNamed(string name, string world) =>
        Net.Mine?.Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (world.Length == 0 || m.World.Equals(world, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Owners can remove anyone but themselves; farmers can remove cows and bulls.</summary>
    public bool CanRemove(MemberInfo m)
    {
        var me = F.NetMember;
        if (me == null || m.IsOwner || m.Id == Net.Mine?.You?.Id) return false;
        return me.IsOwner || (me.Role == "farmer" && m.Role is "cow" or "bull");
    }

    public void RemoveMember(MemberInfo m)
    {
        if (!CanRemove(m))
        {
            Fail("You can't remove them.");
            return;
        }

        Net.RemoveMember(m.Id, e => Fail(e));
        Say($"{m.Name} was removed from {DairyName}.", 0);
    }

    public void SetMemberRole(MemberInfo m, string role)
    {
        if (F.NetMember?.IsOwner != true) return;
        Net.SetRole(m.Id, role, e => Fail(e));
    }

    public void AcceptInvite(InviteInfo inv) =>
        Net.Accept(inv, r => Say(r.Left != null ? $"You left {r.Left} and joined {r.Dairy} as a {r.Role}." : $"You joined {r.Dairy} as a {r.Role}.", 1), e => Fail(e));

    public void DeclineInvite(InviteInfo inv) => Net.Decline(inv, e => Fail(e));

    public void LeaveDairy()
    {
        Stop();
        Net.Leave(() => Say("You left the dairy.", 0), e => Fail(e));
    }

    public void CloseDairy()
    {
        Stop();
        Net.CloseDairy(() => Say("Your dairy is closed. Its members were told.", 0), e => Fail(e));
    }

    // ----- Owner buy orders: you put up coins, other players' cows deliver milk in person -----

    public const int MaxOrderOz = 64;

    /// <summary>Fair coins for an order: a bit over what the creamery pays, so it's worth the trip.</summary>
    public static int SuggestedOrderCoins(int oz, int grade) => (int)Math.Round(oz * Grades.All[grade].Price * 1.5f);

    public string? PostOrderProblem(int oz, int grade, int coins)
    {
        if (!Networked || F.NetMember?.IsOwner != true) return "Only a dairy owner can post buy orders.";
        if (Net.Mine?.Dairy?.IsPublic != true) return "Make your dairy public so cows can find it and deliver.";
        if (oz < 1 || oz > MaxOrderOz) return $"Ask for 1 to {MaxOrderOz} oz.";
        if (coins < oz) return "Offer at least 1 coin per oz.";
        if (coins > oz * 40) return "That's more than 40 coins per oz.";
        if (F.Coins < coins) return $"You need {coins} coins.";
        return null;
    }

    public void PostBuyOrder(int oz, int grade, int coins)
    {
        var p = PostOrderProblem(oz, grade, coins);
        if (p != null)
        {
            Fail(p);
            return;
        }

        // The coins are held by the network until the order is filled or cancelled (then they come back in your inbox)
        F.Coins -= coins;
        plugin.SaveConfig();
        Net.PostOrder(oz, grade, coins, () => Say($"Posted: {oz} oz of Grade {Grades.Name(grade)} or better for {coins} coins.", 1), e =>
        {
            F.Coins += coins;
            plugin.SaveConfig();
            Fail(e);
        });
    }

    public void CancelBuyOrder(BuyOrder o) => Net.CancelOrder(o.Id, e => Fail(e));

    /// <summary>The dairy you're standing in if it's someone else's on the network.</summary>
    public HereInfo? VisitingDairy => Networked && Net.Last?.Here is { Mine: false } h && h.Id != F.NetMember?.DairyId ? h : null;

    public string? DeliverProblem(BuyOrder o)
    {
        if (Blocked() != null) return Blocked();
        if (!ProducesMilk) return "Only cows deliver milk.";
        if (VisitingDairy?.Id != o.DairyId) return "Go to that dairy to deliver in person.";
        if (OzAtOrAbove(o.MinGrade) < o.Oz) return $"You need {o.Oz} oz of Grade {Grades.Name(o.MinGrade)} or better in your tank.";
        return null;
    }

    public void Deliver(BuyOrder o)
    {
        var p = DeliverProblem(o);
        if (p != null)
        {
            Fail(p);
            return;
        }

        // Take the milk now (lowest grade that still qualifies first); give it back if the delivery fails
        var taken = new float[Grades.Count];
        var left = (float)o.Oz;
        var lowest = -1;
        for (var g = o.MinGrade; g < Grades.Count && left > 0; g++)
        {
            var take = Math.Min(left, F.Tank![g]);
            if (take <= 0) continue;
            if (lowest < 0) lowest = g;
            F.Tank[g] -= take;
            taken[g] = take;
            left -= take;
        }

        plugin.SaveConfig();
        Net.Fill(o, Math.Max(lowest, o.MinGrade), r =>
        {
            F.Stats.OrdersFilled++;
            F.DayOrders++;
            EaseTaint(Catalog.TaintPerOrder);
            AddQuality(Grades.QualityPerOrder);
            Say($"Delivered {r.Oz} oz to {r.Dairy}. Your {r.Coins} coins are on the way.", 1);
            plugin.SaveConfig();
        }, e =>
        {
            for (var g = 0; g < Grades.Count; g++) F.Tank![g] += taken[g];
            plugin.SaveConfig();
            Fail(e);
        });
    }

    // ----- Visitor orders: the game itself pays you for bringing milk to other public dairies (2 a day each) -----

    public List<Order> VisitorOrders(string dairyId)
    {
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (F.VisitDay != day)
        {
            F.VisitDay = day;
            F.VisitOrdersDone!.Clear();
        }

        var list = new List<Order>();
        var seed = 0;
        foreach (var c in dairyId + day) seed = seed * 31 + c;
        var r = new Random(seed);
        for (var i = 0; i < 2; i++)
        {
            var grade = Math.Min(GradeCap, r.Next(0, 3));
            var oz = 8 * r.Next(1, 4);
            var key = $"{dairyId}:{i}";
            if (F.VisitOrdersDone.Contains(key)) continue;
            list.Add(new Order
            {
                Id = i,
                Customer = Catalog.Customers[r.Next(Catalog.Customers.Length)],
                Oz = oz,
                MinGrade = grade,
                Coins = (int)Math.Round(oz * Grades.All[grade].Price * 2f) + 20,
                Xp = (int)(oz * 3f) + 25 + grade * 10,
            });
        }

        return list;
    }

    public bool FillVisitorOrder(string dairyId, Order o)
    {
        if (VisitingDairy?.Id != dairyId) return Fail("Go to that dairy to deliver in person.");
        if (!ProducesMilk) return Fail("Only cows deliver milk.");
        if (!VisitorOrders(dairyId).Any(x => x.Id == o.Id)) return Fail("That order is already done.");
        F.VisitOrdersDone!.Add($"{dairyId}:{o.Id}");
        pendingDairyXp += o.Oz;  // counts for your own dairy (the server checks you're at a public dairy)
        F.Orders!.Add(o);   // reuse the regular fill (milk, coins, xp, quality)
        if (!Fill(o))
        {
            F.Orders.Remove(o);
            F.VisitOrdersDone.Remove($"{dairyId}:{o.Id}");
            return false;
        }

        return true;
    }

    // =================================================================
    // Daily goals
    // =================================================================

    public sealed record Goal(string Name, float Have, float Need);

    public IReadOnlyList<Goal> Goals() => new[]
    {
        new Goal("Give 10 oz of milk", F.DayOz, 10),
        new Goal("Fill a creamery order", F.DayOrders, 1),
        new Goal("Do 3 care tasks", F.DayCare, 3),
        new Goal("Get hand milked", F.DayHand, 1),
    };

    public bool Claimed(int i) => (F.DayClaimed & (1 << i)) != 0;
    public const int GoalCoins = 30;
    public const int GoalXp = 40;
    public const int AllGoalsCoins = 60;

    public bool Claim(int i)
    {
        var g = Goals()[i];
        if (Claimed(i) || g.Have < g.Need) return false;
        F.DayClaimed |= 1 << i;
        Earn(GoalCoins);
        AddXp(GoalXp);
        F.Stats.DailiesDone++;
        var all = Enumerable.Range(0, Goals().Count).All(Claimed);
        if (all) Earn(AllGoalsCoins);
        Say(all ? $"All daily goals done! +{GoalCoins + AllGoalsCoins} coins" : $"Daily goal done. +{GoalCoins} coins, +{GoalXp} xp", 1);
        plugin.SaveConfig();
        return true;
    }

    public TimeSpan UntilReset => DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;

    private void RollDay()
    {
        var key = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (F.DayKey == key) return;
        F.DayKey = key;
        F.DayOz = 0;
        F.DayOrders = 0;
        F.DayCare = 0;
        F.DayHand = 0;
        F.DayAwakeMinutes = 0;
        F.DayClaimed = 0;
    }

    // =================================================================
    // XP, coins, awards, messages
    // =================================================================

    private void Earn(int coins)
    {
        F.Coins += coins;
        F.Stats.CoinsEarned += coins;
    }

    /// <summary>Raises your milk quality score and announces any grade it unlocks.</summary>
    private void AddQuality(float amount)
    {
        var before = Grades.Cap(F.Quality);
        F.Quality += amount;
        var after = Grades.Cap(F.Quality);
        for (var g = before + 1; g <= after; g++)
            Say($"Milk quality up! Your milk can now be Grade {Grades.Name(g)} ({Grades.All[g].Blurb}).", 1);
    }

    /// <summary>Quality needed for the next grade, or null at the top.</summary>
    public float? NextQuality => GradeCap + 1 < Grades.QualityNeed.Length ? Grades.QualityNeed[GradeCap + 1] : null;

    private void AddXp(int amount)
    {
        var before = Level;
        F.Xp += amount;
        var after = Level;
        for (var l = before + 1; l <= after; l++)
        {
            var unlocks = Leveling.UnlocksAt(l).ToList();
            Say($"Level {l}! You're a {Leveling.Rank(l)}." + (unlocks.Count > 0 ? " Unlocked: " + string.Join(", ", unlocks) : ""), 1);
        }
    }

    private void CheckAwards()
    {
        foreach (var a in Awards.All)
        {
            if (F.Awards.Contains(a.Id) || !a.Earned(F)) continue;
            F.Awards.Add(a.Id);
            Earn(25);
            Say($"Award: {a.Name}. +25 coins", 1);
        }
    }

    private bool Fail(string text)
    {
        Toast?.Invoke(text, 2);
        return false;
    }

    private void Say(string text, int kind)
    {
        Toast?.Invoke(text, kind);
        var log = F.Log!;
        log.Insert(0, $"{DateTime.Now:HH:mm}  {text}");
        if (log.Count > 60) log.RemoveRange(60, log.Count - 60);
        if (F.ChatEcho && kind != 2) Plugin.ChatGui.Print(text, "Velvet Dairy");
    }

    // ----- Dev helpers -----

    public void DevFillUp()
    {
        if (!Dev) return;
        Cow.Milk = Capacity;
        plugin.SaveConfig();
    }

    public void DevQuality(float amount)
    {
        if (!Dev) return;
        AddQuality(amount);
    }

    public void DevLevel()
    {
        if (!Dev) return;
        var (_, into, need) = Leveling.FromXp(F.Xp);
        if (need > 0) AddXp(need - into);
    }

    /// <summary>Testing: fill every bottle and jump the spoil clock to the end.</summary>
    public void DevSpoil()
    {
        if (!Dev) return;
        Stop();
        Cow.Milk = Capacity;
        Cow.FullSinceUtc = DateTime.UtcNow.AddHours(-Catalog.SpoilHours).AddSeconds(-1);
    }

    /// <summary>Testing: fill every bottle with only a minute left before it spoils.</summary>
    public void DevNearlySpoiled()
    {
        if (!Dev) return;
        Stop();
        Cow.Milk = Capacity;
        Cow.FullSinceUtc = DateTime.UtcNow.AddHours(-Catalog.SpoilHours).AddMinutes(1);
        spoilWarned = 1;
    }

    public void DevCoins()
    {
        if (!Dev) return;
        Earn(500);
    }

    public void DevResetNeeds()
    {
        if (!Dev) return;
        Cow.Hunger = Cow.Thirst = Cow.Comfort = Cow.Mood = 80;
        Cow.Richness = 30;
    }

    public void DevStroke()
    {
        if (!Dev) return;
        if (Active?.Kind == SessionKind.Hand) Stroke(Active.Hand!);
    }
}
