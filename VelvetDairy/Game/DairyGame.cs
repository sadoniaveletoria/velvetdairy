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

    private const float BaseRate = 0.14f;  // oz per minute with every need full, before bonuses and the Milk speed setting
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

    /// <summary>Your cow level (the main one: it unlocks the shop and speeds up milk).</summary>
    public int Level => Leveling.FromXp(F.Xp).Level;

    /// <summary>Each role levels on its own; switching or adding roles never loses any of it.</summary>
    public int FarmerLevel => Leveling.FromXp(F.FarmerXp).Level;
    public int BullLevel => Leveling.FromXp(F.BullXp).Level;

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
    /// <summary>Your roles in your dairy (several at once). Locally, and before joining a dairy, you're a cow.</summary>
    public List<string> MyRoles => !Networked || F.NetMember == null ? new List<string> { "cow" } : Roles.From(F.NetMember.Roles, F.NetMember.Role);

    public bool Has(string role) => MyRoles.Contains(role);

    /// <summary>"Cow + Farmer" etc.</summary>
    public string Role => Roles.Text(MyRoles);
    public bool IsOwner => !Networked || F.NetMember?.IsOwner == true;
    public bool IsCow => Has("cow");
    public bool IsFarmer => Has("farmer");
    public bool CanInvite => Networked && F.NetMember != null && (F.NetMember.IsOwner || IsFarmer);

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

        var roles = m.AllRoles;
        if (before == null || before.DairyId != m.DairyId || !before.Roles.SequenceEqual(roles) || before.IsOwner != m.IsOwner || before.Dairy != m.Dairy)
        {
            F.NetMember = new NetMember
            {
                DairyId = m.DairyId, Dairy = m.Dairy, Role = m.Role, Roles = roles, IsOwner = m.IsOwner, Level = m.Level,
                HouseId = before?.DairyId == m.DairyId ? before.HouseId : "",
            };
            if (before != null && before.DairyId == m.DairyId && !before.Roles.SequenceEqual(roles)) Say($"Your roles at {m.Dairy}: {Roles.Text(roles)}.", 1);
            if (!ProducesMilk) Stop();
            plugin.SaveConfig();
        }
    }

    /// <summary>Your dairy's details: address, stalls and members (farmers become your hand milkers).</summary>
    public void OnMyDairy(MyDairyReply? r)
    {
        if (r?.Dairy == null || F.NetMember == null) return;
        if (F.NetMember.HouseId != r.Dairy.HouseId || !SameArea(F.NetMember.Area, r.Dairy.Area) || !SameArea(F.NetMember.PrivateArea, r.Dairy.PrivateArea))
        {
            F.NetMember.HouseId = r.Dairy.HouseId;
            F.NetMember.Area = r.Dairy.Area;
            F.NetMember.PrivateArea = r.Dairy.PrivateArea;
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
            s.CleanUntil = si.CleanUntil;
            stalls.Add(s);
        }

        netStalls = stalls;

        var me = Plugin.ObjectTable.LocalPlayer;
        var myName = me?.Name.TextValue ?? "";
        var farmers = new List<Farmhand>();
        foreach (var m in r.Members.Where(m => m.Has("farmer") && !m.Name.Equals(myName, StringComparison.OrdinalIgnoreCase)))
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
                    // Payouts come from farmer work: hand milking shares and deliveries
                    Earn(i.Coins);
                    AddFarmerXp(Math.Max(5, i.Coins / 8));
                    break;
                case "refund":
                    F.Coins += i.Coins;
                    break;
                case "groom":
                    // A farmer groomed you: you're Happy (+5%) even without a treat, and the bar gets 30 more minutes.
                    // A bigger treat bonus that's still going is kept.
                    if (IsCow)
                    {
                        Cow.HappyBonus = Cow.Mood <= 0 ? Catalog.GroomHappy : Math.Max(Cow.HappyBonus, Catalog.GroomHappy);
                        Cow.Mood = Fill(Cow.Mood);
                    }

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
    public bool Contented => Cow.Hunger > 0 && Cow.FedBonus > 0 && Cow.Thirst > 0 && Cow.WaterBonus > 0 && Cow.Comfort > 0 && Cow.ComfortBonus > 0 && Cow.Mood > 0 && Cow.HappyBonus > 0;
    public bool Spoiled => Cow.Spoiled;

    /// <summary>How long until full bottles spoil, or null if they aren't all full.</summary>
    public TimeSpan? SpoilsIn => Cow.Spoiled || Cow.FullSinceUtc == null ? null
        : Cow.FullSinceUtc.Value.AddHours(SpoilHours) - DateTime.UtcNow;
    public int Owned(string upgrade) => F.Upgrades.GetValueOrDefault(upgrade);
    public int Count(string item) => F.Pantry.GetValueOrDefault(item);
    public float NeedsAverage => (Cow.Hunger + Cow.Thirst + Cow.Comfort + Cow.Mood) / 4f;
    public int CurrentGrade => Grades.For(Cow, false, F.Quality);
    public int GradeCap => Grades.Cap(F.Quality);
    public int GradeSlip => Grades.Slip(Cow);
    public DateTime? NextSlipUtc => Grades.NextSlipUtc(Cow);
    public TimeSpan FreshHoldLeft => Cow.FreshHoldUntilUtc > DateTime.UtcNow ? Cow.FreshHoldUntilUtc - DateTime.UtcNow : TimeSpan.Zero;
    /// <summary>The Comfy bonus resting gives you with what you own.</summary>
    public float ComfyNow => (Owned("cushion") > 0 ? Catalog.ComfyCushion : Catalog.ComfyBonus) + (Owned("lavender") > 0 ? Catalog.ComfyLavender : 0);
    public bool Overfull => Cow.Milk >= Capacity - 0.01f;
    public int MaxStalls => Networked ? Net.Progress?.StallSlots ?? 16 : 1 + Owned("stallkit");
    public float PriceBonus => 1f + (Owned("ribbon") > 0 ? 0.05f : 0) + (Owned("cap") > 0 ? 0.10f : 0) +
                               (Owned("halter") > 0 ? 0.03f : 0) + (Owned("velvetbell") > 0 ? 0.05f : 0) + (Owned("velvetchamp") > 0 ? 0.10f : 0);

    /// <summary>Hours full bottles keep before spoiling (Cooling mist makes it 96).</summary>
    public double SpoilHours => Owned("mist") > 0 ? 96 : Catalog.SpoilHours;
    public float TankTotal => F.Tank!.Sum();

    // =================================================================
    // Pay: the dairy pays cows per oz the moment they're milked
    // =================================================================

    /// <summary>The dairy's level raises pay 2% a level (on the network).</summary>
    public float DairyPayBonus => Networked ? Net.Progress?.PayBonus ?? 1f : 1f;

    /// <summary>Coins per oz for milk of this grade.</summary>
    public float PayRate(int grade) => Grades.All[Math.Clamp(grade, 0, Grades.Count - 1)].Price * PriceBonus * DairyPayBonus;

    /// <summary>Farmers and owners carry jugs from the dairy's tank and deliver them; cows just get milked.</summary>
    public bool CanCarry => Networked && F.NetMember != null && (F.NetMember.IsOwner || IsFarmer);

    /// <summary>How much milk you can carry: your farmer tank (bought in the Shop, like a cow's bottles).</summary>
    public int FarmerTankTier => Math.Min(Owned("farmtank"), Catalog.FarmerTankSizes.Length - 1);
    public string FarmerTankName => Catalog.FarmerTankNames[FarmerTankTier];
    public int MaxCarry => Catalog.FarmerTankSizes[FarmerTankTier];
    public float Carried => F.Tank!.Sum();

    public string? TakeJugProblem(int grade, int oz)
    {
        if (Blocked() != null) return Blocked();
        if (!CanCarry) return "Only farmers and the owner fill jugs from the dairy tank.";
        if (!Where.InDairy) return "Go to your dairy to fill jugs from its tank.";
        if (Carried + oz > MaxCarry) return $"Your {FarmerTankName.ToLowerInvariant()} holds {MaxCarry} oz. Buy a bigger farmer tank in the Shop.";
        var inTank = Net.Progress?.Tank?.ByGrade is { } t && grade < t.Count ? t[grade] : 0;
        if (inTank < oz) return $"The tank has {inTank:0.#} oz of Grade {Grades.Name(grade)}.";
        return null;
    }

    public void TakeJug(int grade, int oz)
    {
        var p = TakeJugProblem(grade, oz);
        if (p != null)
        {
            Fail(p);
            return;
        }

        Touch();
        Net.TakeFromTank(grade, oz, r =>
        {
            F.Tank![r.Grade] += r.Oz;
            AddFarmerXp(Math.Max(1, r.Oz / 4));
            plugin.SaveConfig();
            Say($"Filled {r.Oz} oz of Grade {Grades.Name(r.Grade)} from the dairy tank ({r.Left:0.#} oz left).", 1);
        }, e => Fail(e));
    }

    // ----- Weekly creamery contract -----

    public float TankOz(int grade) => Net.Progress?.Tank?.ByGrade is { } t && grade >= 0 && grade < t.Count ? t[grade] : 0;

    /// <summary>The Grand Creamery (set by the developer), where contract milk is delivered.</summary>
    public CreameryInfo? Creamery => Net.Last?.Creamery;

    /// <summary>Standing at the Grand Creamery right now.</summary>
    public bool AtCreamery
    {
        get
        {
            var c = Creamery;
            var me = Plugin.ObjectTable.LocalPlayer;
            if (c == null || me == null || Plugin.ClientState.TerritoryType != c.Territory) return false;
            return Vector3.Distance(me.Position, new Vector3(c.X, c.Y, c.Z)) <= c.Radius;
        }
    }

    private bool wasAtCreamery;

    private void WatchCreamery()
    {
        var at = AtCreamery;
        if (at && !wasAtCreamery)
        {
            Net.Poke();   // let the server know right away
            if (CanCarry) Say($"You're at {Creamery!.Name}. Ship your jugs on Dairy > Contract.", 0);
        }

        wasAtCreamery = at;
    }

    public string? ShipProblem(int grade, int oz)
    {
        var c = Net.Progress?.Contract;
        if (Blocked() != null) return Blocked();
        if (c == null) return "Loading the contract...";
        if (c.Completed) return "This week's contract is done.";
        if (!CanCarry) return "Farmers and the owner ship milk to the contract.";
        if (Creamery is { } cr) { if (!AtCreamery) return $"Bring your jugs to {cr.Name} ({cr.Zone}) to ship them."; }
        else if (!Where.InDairy) return "Go to your dairy to ship milk.";
        if (grade < c.MinGrade) return $"This contract needs Grade {Grades.Name(c.MinGrade)} or better.";
        if (oz < 1) return "Nothing left to ship.";
        if (F.Tank![grade] < oz - 0.01f) return $"You carry {F.Tank[grade]:0.#} oz of Grade {Grades.Name(grade)}. Fill jugs at your dairy (Creamery > Jugs).";
        return null;
    }

    /// <summary>Developer: sets the Grand Creamery where you stand.</summary>
    public void SetCreameryHere(string name, float radius)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (!Dev || me == null) return;
        var t = Plugin.ClientState.TerritoryType;
        var zone = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(t)?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        Net.SetCreamery(new
        {
            name = string.IsNullOrWhiteSpace(name) ? "The Grand Creamery" : name.Trim(),
            territory = t,
            mapId = Plugin.ClientState.MapId,
            zone,
            x = me.Position.X, y = me.Position.Y, z = me.Position.Z,
            radius,
        }, () => Say($"The Grand Creamery is now here ({zone}).", 1), e => Fail(e));
    }

    public void Ship(int grade, int oz)
    {
        var p = ShipProblem(grade, oz);
        if (p != null)
        {
            Fail(p);
            return;
        }

        Touch();
        Net.ShipToContract(grade, oz, r =>
        {
            F.DayDelivered++;
            F.Tank![grade] = Math.Max(0, F.Tank[grade] - r.Shipped);
            AddFarmerXp(Math.Max(1, r.Shipped / 2));
            plugin.SaveConfig();
            Say(r.Finished ? $"Contract finished! The reward goes to {DairyName}." : $"Shipped {r.Shipped} oz to the contract ({r.OzDone:0} / {r.OzNeed} oz).", 1);
        }, e => Fail(e));
    }

    /// <summary>Milk people had stored before milk paid at milking time: paid out once, at today's prices.</summary>
    private void PayOutOldStock()
    {
        F.StockPaidOut = true;
        var oz = F.Tank!.Sum();
        if (oz <= 0.01f)
        {
            plugin.SaveConfig();
            return;
        }

        var coins = 0;
        for (var g = 0; g < Grades.Count; g++) coins += (int)Math.Round(F.Tank[g] * Grades.All[g].Price * PriceBonus);
        Array.Clear(F.Tank);
        Earn(coins);
        Say($"Milk now pays when you're milked. The {oz:0.#} oz you had stored was paid out: +{coins} coins.", 1);
        plugin.SaveConfig();
    }

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
        if (Cow.Hunger > 0 && Cow.FedBonus > 0) parts.Add(new RatePart("Fed", 1f + Cow.FedBonus));
        if (Cow.Thirst > 0 && Cow.WaterBonus > 0) parts.Add(new RatePart("Watered", 1f + Cow.WaterBonus));
        if (Cow.Comfort > 0 && Cow.ComfortBonus > 0) parts.Add(new RatePart("Comfy", 1f + Cow.ComfortBonus));
        if (Cow.Mood > 0 && Cow.HappyBonus > 0) parts.Add(new RatePart("Happy", 1f + Cow.HappyBonus));
        if (Level > 1) parts.Add(new RatePart(Level > Leveling.MilkBonusCap ? $"Level {Level} (max bonus)" : $"Level {Level}", 1f + Catalog.LevelBonus * (Math.Min(Level, Leveling.MilkBonusCap) - 1)));
        if (Contented) parts.Add(new RatePart("Contented (all four bonuses)", 1f + Catalog.ContentBonus));
        if (BoostLeft > TimeSpan.Zero) parts.Add(new RatePart("Treat boost", 1f + Catalog.TreatBonus));
        if (includeResting && Where.InDairy && Where.Seated && Wake == Wakefulness.Awake) parts.Add(new RatePart("Resting in your dairy", 1f + (Owned("deeprest") > 0 ? 0.75f : Catalog.RestBonus)));
        if (includeResting && Wake == Wakefulness.Dozing) parts.Add(new RatePart("Dozing (no activity for 15 min)", 0.5f));
        if (includeResting && Wake == Wakefulness.Asleep) parts.Add(new RatePart("Asleep (no activity for 30 min)", 0f));
        if (Tired) parts.Add(new RatePart($"Tired ({TiredHours} h milking today)", 0.5f));
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

    /// <summary>Resting on furniture in your dairy holds off dozing for 30 minutes (asleep at 45).</summary>
    public const double SeatedDozeAfter = 30 * 60;
    public const double SeatedSleepAfter = 45 * 60;

    private bool RestingInDairy => Where.InDairy && Where.Seated;

    public Wakefulness Wake =>
        Dev && !F.DevCanSleep ? Wakefulness.Awake
        : IdleSeconds >= (RestingInDairy ? SeatedSleepAfter : SleepAfter) ? Wakefulness.Asleep
        : IdleSeconds >= (RestingInDairy ? SeatedDozeAfter : DozeAfter) ? Wakefulness.Dozing
        : Wakefulness.Awake;

    /// <summary>After 12 hours of making milk in one day, milk comes in at half speed until tomorrow.</summary>
    public int TiredHours => Owned("steady") > 0 ? 14 : TiredAfterHours;
    public bool Tired => F.DayAwakeMinutes >= TiredHours * 60;

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
            if (w == Wakefulness.Dozing) Say("You're dozing off. Milk comes in at half speed until you do something (move, emote, chat or click).", 2);
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
        if (!F.StockPaidOut && Blocked() == null) PayOutOldStock();
        var wasInDairy = Where.InDairy;
        Where = Locate();
        // Tell the server right away when you arrive or leave, so jugs, chores and hand milking pay count at once
        if (Networked && Where.InDairy != wasInDairy) Net.Poke();
        WatchActivity();
        if (Networked) WatchCreamery();

        if (Blocked() != null || Paused)
        {
            HoldBoost(dt);
            Cow.LastTickUtc = DateTime.UtcNow;
            return;
        }

        if (!caughtUp)
        {
            caughtUp = true;
            CatchUp();
        }

        WatchFarmhands();
        WatchMyMilking();

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
        TickXp();
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
        var inPrivate = false;
        if (Networked)
        {
            var m = F.NetMember;
            var here = Estate.Current();
            if (m == null) why = "You aren't in a dairy yet. Mark out your own or accept an invite (Dairy tab).";
            else if (m.PrivateArea is { } priv && InArea(priv, me))
            {
                inDairy = true;
                inPrivate = true;
                why = $"In {m.Dairy}'s private place.";
            }
            else if (m.Area is { } area)
            {
                if (InArea(area, me))
                {
                    inDairy = true;
                    why = $"In {m.Dairy}.";
                }
                else why = Plugin.ClientState.TerritoryType == area.Territory && me.CurrentWorld.RowId == area.World ? $"Outside {m.Dairy}'s area." : $"You're away from {m.Dairy}.";
            }
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

        InPrivatePlace = inPrivate;
        return new Whereabouts(true, inHouse, inDairy, seated, at, why);
    }

    /// <summary>
    /// Time spent logged out: no milk is made. Full bottles still spoil on the real clock, so empty them before you log out.
    /// Need bonuses are frozen while you're away and pick up where they left off.
    /// </summary>
    private void CatchUp()
    {
        var away = DateTime.UtcNow - Cow.LastTickUtc;
        if (away.TotalMinutes < 1) return;

        Cow.LastTickUtc = DateTime.UtcNow;
        HoldBoost(away.TotalSeconds);   // a treat boost waits while you're away, like the need bars
        CheckSpoil();
    }

    /// <summary>A treat boost only runs while you're awake and playing: push its end back by time spent otherwise.</summary>
    private void HoldBoost(double seconds)
    {
        if (Cow.BoostUntilUtc > DateTime.UtcNow) Cow.BoostUntilUtc = Cow.BoostUntilUtc.AddSeconds(seconds);
    }

    private void Simulate(float seconds)
    {
        var m = seconds / 60f;
        if (Cow.Milk > Capacity) Cow.Milk = Capacity;
        var fullBefore = FullBottles;
        var cushion = Owned("cushion") > 0;
        WatchRest();
        if (Wake != Wakefulness.Awake) HoldBoost(seconds);

        // Need bars drain as the bonus wears off, only while you're awake; asleep, dozing or logged out they wait
        if (Wake == Wakefulness.Awake)
        {
            var step = 100f / Catalog.NeedMinutes * m;
            Cow.Hunger = Clamp(Cow.Hunger - step);
            Cow.Thirst = Clamp(Cow.Thirst - step / (Owned("trough") > 0 ? 1.5f : 1f));
            Cow.Mood = Clamp(Cow.Mood - step / ((Owned("lavender") > 0 ? 2f : 1f) * (Owned("bell") > 0 ? 1.2f : 1f)));
            Cow.Comfort = Clamp(Cow.Comfort - step / (cushion ? 2f : 1f));
        }

        if (!Overfull && Active == null && ProducesMilk)
        {
            var milkBefore = Cow.Milk;
            Cow.Milk = Math.Min(Capacity, Cow.Milk + ProductionPerMinute() * m);
            // XP ticks in as milk is made (dozing and tired already slow production; asleep and full make none)
            madeSinceTick += Cow.Milk - milkBefore;
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
        // The tonic works anywhere, so you can take it on the way out
        var p = item.HoldDays > 0 ? Blocked() : CareProblem();
        if (p != null) return Fail(p);
        if (Count(id) <= 0) return Fail($"You're out of {item.Name}. Buy more in the Shop.");

        if (item.Quality > 0 && QualityTreatWait > TimeSpan.Zero)
            return Fail($"You can have another quality treat in {Wait(QualityTreatWait)}.");
        var wait = FeedWait(item);
        if (wait > TimeSpan.Zero) return Fail($"You can have that in {Wait(wait)}.");
        F.Pantry[id] = Count(id) - 1;
        var cd = DateTime.UtcNow.AddMinutes(Catalog.NeedCooldownMinutes);
        if (item.Fed > 0) { Cow.Hunger = Fill(Cow.Hunger); Cow.FedBonus = item.Fed; Cow.FedReadyUtc = cd; }
        if (item.Water > 0) { Cow.Thirst = Fill(Cow.Thirst); Cow.WaterBonus = item.Water; Cow.WaterReadyUtc = cd; }
        if (item.Happy > 0) { Cow.Mood = Fill(Cow.Mood); Cow.HappyBonus = item.Happy; Cow.HappyReadyUtc = cd; }
        if (item.HoldDays > 0)
        {
            var from = Cow.FreshHoldUntilUtc > DateTime.UtcNow ? Cow.FreshHoldUntilUtc : DateTime.UtcNow;
            Cow.FreshHoldUntilUtc = from.AddDays(item.HoldDays);
            Touch();
            Say($"Dry-off tonic: your grade won't slip until {Cow.FreshHoldUntilUtc.ToLocalTime():MMM d, h:mm tt}.", 1);
            plugin.SaveConfig();
            return true;
        }
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

    private static TimeSpan Left(DateTime ready) => ready > DateTime.UtcNow ? ready - DateTime.UtcNow : TimeSpan.Zero;
    public TimeSpan EatWait => Left(Cow.FedReadyUtc);
    public TimeSpan DrinkWait => Left(Cow.WaterReadyUtc);
    public TimeSpan RestWait => Left(Cow.ComfyReadyUtc);
    public TimeSpan TreatWait => Left(Cow.HappyReadyUtc);

    /// <summary>How long until this food or treat can be had again (the longest of the cooldowns it uses).</summary>
    public TimeSpan FeedWait(FeedItem item)
    {
        var w = TimeSpan.Zero;
        if (item.Fed > 0 && EatWait > w) w = EatWait;
        if (item.Water > 0 && DrinkWait > w) w = DrinkWait;
        if (item.Happy > 0 && TreatWait > w) w = TreatWait;
        return w;
    }

    /// <summary>Each use adds half a bar (30 minutes), up to full.</summary>
    private static float Fill(float bar) => Math.Min(100f, bar + Catalog.NeedFill);

    public bool Drink()
    {
        var p = CareProblem();
        if (p != null) return Fail(p);
        if (DrinkWait > TimeSpan.Zero) return Fail($"You can drink again in {Wait(DrinkWait)}.");
        Cow.Thirst = Fill(Cow.Thirst);
        Cow.WaterBonus = Catalog.TroughBonus;
        Cow.WaterReadyUtc = DateTime.UtcNow.AddMinutes(Catalog.NeedCooldownMinutes);
        CareDone("You drink from the trough.");
        return true;
    }

    /// <summary>Settle in on furniture in your dairy for the Comfy bonus.</summary>
    public string? RestProblem()
    {
        var p = CareProblem();
        if (p != null) return p;
        if (!Where.Seated) return "Sit on a chair, bench or bed in your dairy first.";
        return null;
    }

    public bool Rest()
    {
        var p = RestProblem();
        if (p != null) return Fail(p);
        if (RestWait > TimeSpan.Zero) return Fail($"You can rest again in {Wait(RestWait)}.");
        var before = Cow.Comfort;
        restPrevBonus = Cow.ComfortBonus;
        Cow.Comfort = Fill(Cow.Comfort);
        restAdded = Cow.Comfort - before;
        restPendingSince = now;
        Cow.ComfortBonus = ComfyNow;
        Cow.ComfyReadyUtc = DateTime.UtcNow.AddMinutes(Catalog.NeedCooldownMinutes);
        CareDone($"You settle in and get comfy. Stay seated {Catalog.RestSettleMinutes:0} minutes and it stays with you when you get up.");
        return true;
    }

    // Comfy from furniture only sticks once you've stayed seated a while
    private double restPendingSince = -1;
    private float restAdded, restPrevBonus;

    /// <summary>Time left sitting before the last Rest's Comfy is yours to keep (zero when settled or no rest pending).</summary>
    public TimeSpan RestSettleLeft => restPendingSince < 0 ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Math.Max(0, Catalog.RestSettleMinutes * 60 - (now - restPendingSince)));

    private void WatchRest()
    {
        if (restPendingSince < 0) return;
        if (now - restPendingSince >= Catalog.RestSettleMinutes * 60)
        {
            restPendingSince = -1;
            Say("You're settled in. Your Comfy bonus stays when you get up.", 0);
            return;
        }

        if (Where.InDairy && Where.Seated) return;
        // Got up too soon: that rest doesn't count
        restPendingSince = -1;
        Cow.Comfort = Clamp(Cow.Comfort - restAdded);
        if (Cow.Comfort > 0) Cow.ComfortBonus = restPrevBonus;
        Say($"You got up too soon. Stay seated {Catalog.RestSettleMinutes:0} minutes after resting to keep the Comfy bonus.", 2);
        plugin.SaveConfig();
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

        var left = Cow.FullSinceUtc.Value.AddHours(SpoilHours) - DateTime.UtcNow;
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
        Say($"The {(Fresh(Where.AtStall!) ? "freshly cleaned " : "")}{Catalog.StallTierNames[Where.AtStall!.Tier].ToLowerInvariant()} starts milking you.", 0);
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
        // Tell the server now, so the farmer's plugin sees the milking has started
        if (Networked) Net.Poke();
        nextTestStroke = now + 1.5;
        Say(fh.IsTest
            ? "The test farmhand starts milking you."
            : $"{fh.Name} is milking you. Milk flows while they keep their Milk emote going.", 0);
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

            Draw(Catalog.StallSpeed[Math.Clamp(s.Stall!.Tier, 1, 4)] * (Owned("letdown") > 0 ? 1.1f : 1f) * (Fresh(s.Stall) ? 1f + Catalog.CleanBonus : 1f) * dt);
        }
        else
        {
            if (s.Hand!.IsTest)
            {
                handFlowing = true;
                s.LastActivity = now;
            }

            if (handFlowing)
            {
                Draw(Catalog.HandSpeed * (Owned("udderbalm") > 0 ? 1.15f : 1f) * dt);
                if (now >= nextSquirt)
                {
                    nextSquirt = now + 2.5;
                    s.Strokes++;
                    Stroked?.Invoke();
                }
            }
            else if (now - s.LastActivity > 20)
            {
                Finish("Your farmer stopped.");
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
        // Tell the server now: a farmer's plugin can end their milking emote, and the milk reaches the dairy tank before you walk off
        if (Networked) Net.Poke();

        if (s.Extracted < 0.05f)
        {
            Say(reason != null ? $"Milking stopped. {reason}" : "Milking stopped.", 2);
            return;
        }

        var collected = s.Extracted * s.Yield;
        EaseTaint(Catalog.TaintPerMilking);
        AddQuality(collected * (s.Kind == SessionKind.Hand ? Grades.QualityPerOzHand : Grades.QualityPerOz));
        // The dairy pays for the milk right away; the milk itself goes into the dairy's tank
        var pay = (int)Math.Round(collected * PayRate(s.Grade));
        Earn(pay);
        F.Stats.TotalOz += collected;
        F.DayOz += collected;
        F.Stats.BestGrade = Math.Max(F.Stats.BestGrade, s.Grade);
        EarnDairyXp(collected * (s.Kind == SessionKind.Hand ? 1.5f : 1f));
        if (Networked && F.NetMember != null && Where.InDairy) pendingPump[Math.Clamp(s.Grade, 0, 4)] += collected;
        F.Stats.BestSessionOz = Math.Max(F.Stats.BestSessionOz, collected);

        // Being milked keeps your milk fresh
        Cow.LastMilkedUtc = DateTime.UtcNow;

        int xp;
        if (s.Kind == SessionKind.Stall)
        {
            F.Stats.StallSessions++;
            F.DayStall++;
            s.Stall!.Sessions++;
            xp = (int)Math.Round(collected * Leveling.XpPerOzStall * Leveling.StallXpBonus[Math.Clamp(s.Stall.Tier, 1, 4)] * Leveling.GradeBonus(s.Grade) *
                                 (Fresh(s.Stall) ? 1f + Catalog.CleanBonus : 1f));
        }
        else
        {
            F.Stats.HandSessions++;
            F.DayHand++;
            s.Hand!.Sessions++;
            s.Hand.OzMilked += collected;
            if (Networked && !s.Hand.IsTest) Net.HandPay(s.Hand.Name, s.Hand.WorldName, collected, s.Grade);   // the farmer's share
            xp = (int)Math.Round(collected * Leveling.XpPerOzHand * Leveling.GradeBonus(s.Grade)) + Leveling.XpHandSession;
        }

        AddXp(xp);
        var by = s.Kind == SessionKind.Hand ? $" by {(s.Hand!.IsTest ? "the test farmhand" : s.Hand.Name)}" : "";
        Say($"Milked{by}: {collected:0.#} oz of Grade {Grades.Name(s.Grade)}. +{pay} coins, +{xp} xp" + (reason != null ? $" ({reason})" : ""), 1);
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
        return new FarmhandSight(true, Vector3.Distance(me.Position, o.Position), Emotes.Looping(o), o.TargetObjectId == me.GameObjectId);
    }

    /// <summary>
    /// The emotes a farmer's Milk button can use on you, best first: /beesknees whenever it counts
    /// (you allow any looping emote, or it's on your list), then your own list.
    /// </summary>
    public List<string> MilkEmotesForFarmers()
    {
        var list = F.HandEmotes!.Where(x => !x.Trim().Equals("/kneel", StringComparison.OrdinalIgnoreCase)).ToList();
        var bees = list.Any(x => x.Trim().Equals(MilkEmote, StringComparison.OrdinalIgnoreCase));
        if (F.AnyLoopEmote || bees)
        {
            list.RemoveAll(x => x.Trim().Equals(MilkEmote, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, MilkEmote);
        }

        return list;
    }

    public const string MilkEmote = "/beesknees";

    /// <summary>
    /// The emote your Milk button does on this cow: your own pick (Roles > Farmer) when they accept it,
    /// otherwise /beesknees or the first one on their list.
    /// </summary>
    public string MilkEmoteFor(MemberStatus? st)
    {
        var mine = F.MilkButtonEmote.Trim();
        if (mine.Length > 0 && !mine.StartsWith('/')) mine = "/" + mine;
        var theirs = st?.Emotes is { Count: > 0 } list ? list[0] : MilkEmote;
        if (mine.Length == 0 || Emotes.Find(mine) == 0) return theirs;
        if (st == null || st.AnyEmote || st.Emotes.Any(e => e.Trim().Equals(mine, StringComparison.OrdinalIgnoreCase))) return mine;
        return theirs;
    }

    // ----- Farmer side: stop the milking emote once the cow is empty -----
    private string? milkingCow;
    private ushort milkingEmote;
    private string? pendingEmote;
    private double pendingEmoteAt;
    private bool milkModOn;

    /// <summary>Switches the milking animation mod back off (if we turned it on).</summary>
    public void EndMilkMod()
    {
        if (!milkModOn) return;
        milkModOn = false;
        PenumbraLink.Disable(F.MilkModDir);
    }

    private void StopWatchingMilking()
    {
        if (milkingCow != null && Networked) Net.Poke();
        milkingCow = null;
        pendingEmote = null;
        EndMilkMod();
    }
    private double milkingSince, milkingEndsAt, nextMilkingCheck;
    private bool sawMilking;   // the cow's plugin has reported that you're milking them

    /// <summary>
    /// Milk button: does the emote for you, then ends it on its own once the cow should be empty
    /// (from their milk and the hand milking speed, or sooner when their plugin says they're done).
    /// </summary>
    public bool MilkCow(string cowName, string emote, float cowMilk)
    {
        if (Emotes.Find(emote.Split(' ')[0]) == 0) return false;
        // Your milking animation mod goes on first, then the emote a moment later so the new animation loads
        var delay = 0.0;
        if (F.MilkModDir.Length > 0 && !milkModOn)
        {
            var why = PenumbraLink.Enable(F.MilkModDir, F.MilkModOptions);
            if (why != null) Fail($"Milking animation: {why}");
            else
            {
                milkModOn = true;
                delay = 0.4;
                if (F.MilkModRedraw)
                {
                    PenumbraLink.RedrawMe();
                    delay = 1.5;
                }
            }
        }

        pendingEmote = emote;
        pendingEmoteAt = now + delay;
        milkingCow = cowName;
        // Tell the server right away: the cow's plugin only counts a farmer who pressed Milk on them
        if (Networked) Net.Poke();
        milkingEmote = 0;   // read back from your character once the loop starts (the game's id can differ from the command's)
        milkingSince = now + delay;
        // Only a safety net: the real end comes from the cow's plugin. Their milk keeps growing and the figure here
        // can be a minute old, so leave plenty of room (Udder balm only makes it faster)
        milkingEndsAt = now + delay + Math.Clamp(cowMilk / Catalog.HandSpeed * 1.5 + 60, 120, 3600);
        sawMilking = false;
        nextMilkingCheck = now + 5;
        return true;
    }

    public bool MilkingSomeone => milkingCow != null;
    public string? MilkingCowName => milkingCow;

    private void WatchMyMilking()
    {
        if (milkingCow == null) return;
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null)
        {
            StopWatchingMilking();
            return;
        }

        if (pendingEmote != null)
        {
            if (now < pendingEmoteAt) return;
            var e = pendingEmote;
            pendingEmote = null;
            if (!Emotes.Perform(e))
            {
                StopWatchingMilking();
                return;
            }
        }

        var loop = Emotes.Looping(me);
        if (milkingEmote == 0)
        {
            // Remember whichever loop you're actually in once it starts
            if (loop != 0) milkingEmote = loop;
            else if (now - milkingSince > 5) StopWatchingMilking();   // it never started
            return;
        }

        // Stopping or switching emotes yourself ends it here too
        if (loop != milkingEmote)
        {
            StopWatchingMilking();
            return;
        }

        var done = now >= milkingEndsAt;
        if (!done && now >= nextMilkingCheck)
        {
            nextMilkingCheck = now + 5;
            Net.RefreshMine();
            var st = Net.Mine?.Members.FirstOrDefault(m => m.Name.Equals(milkingCow, StringComparison.OrdinalIgnoreCase))?.Status;
            var myName = me.Name.TextValue;
            // Their plugin reports right away when a hand milking starts and ends. Only an end counts:
            // until they've reported you milking them, an old "nobody's milking" means nothing.
            var byMe = st != null && st.HandBy.Equals(myName, StringComparison.OrdinalIgnoreCase);
            if (byMe) sawMilking = true;
            else if (sawMilking && st != null) done = true;
        }

        if (!done) return;
        var cow = milkingCow;
        // There's no "stop emote" in the game; any other emote ends the loop, so give a quiet pat (no chat text)
        if (Emotes.Looping(me) != 0 && !Emotes.Perform("/pet motion")) Emotes.Perform("/pet");
        StopWatchingMilking();
        F.DayCowsMilked++;
        Say($"{cow} is empty.", 1);
    }

    // ----- Farmer chores: cleaning stalls and grooming cows -----

    public static long UnixNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Was this stall cleaned by a farmer recently?</summary>
    public static bool Fresh(Stall s) => s.CleanUntil > UnixNow;
    public static TimeSpan FreshLeft(Stall s) => TimeSpan.FromSeconds(Math.Max(0, s.CleanUntil - UnixNow));
    /// <summary>Time until it can be cleaned again (it can once half an hour or less of freshness is left).</summary>
    public static TimeSpan CleanWait(Stall s) => TimeSpan.FromSeconds(Math.Max(0, s.CleanUntil - UnixNow - 1800));

    /// <summary>Is anyone sitting on this stall right now?</summary>
    public bool StallTaken(Stall s)
    {
        var at = new Vector3(s.X, s.Y, s.Z);
        foreach (var o in Plugin.ObjectTable)
            if (o is IPlayerCharacter pc && Emotes.InPositionLoop(pc) && Vector3.Distance(pc.Position, at) <= Catalog.StallSitRange) return true;
        return false;
    }

    public float DistanceTo(Stall s)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        return me == null ? -1 : Vector3.Distance(me.Position, new Vector3(s.X, s.Y, s.Z));
    }

    public string? CleanProblem(Stall s)
    {
        var b = Blocked();
        if (b != null) return b;
        if (!Networked || F.NetMember == null) return "Cleaning stalls is for networked dairies.";
        if (!F.NetMember.IsOwner && !IsFarmer) return "Only farmers and the owner clean stalls.";
        if (!Where.InDairy) return "Go to your dairy first.";
        var d = DistanceTo(s);
        if (d < 0 || d > HandRange) return $"Walk up to {s.Name} first (within {HandRange:0} yalms).";
        if (StallTaken(s)) return "Someone is sitting on it. Clean it when it's free.";
        var w = CleanWait(s);
        if (w > TimeSpan.Zero) return $"It's still clean. You can freshen it up again in {Wait(w)}.";
        return null;
    }

    private bool cleaning;

    public void CleanStall(Stall s)
    {
        var p = CleanProblem(s);
        if (p != null)
        {
            Fail(p);
            return;
        }

        if (cleaning) return;
        cleaning = true;
        Touch();
        Net.CleanStall(s.Id, until =>
        {
            cleaning = false;
            s.CleanUntil = until;
            AddFarmerXp(Catalog.CleanXp);
            F.DayCleans++;
            EarnDairyXp(2);
            Say($"You clean {s.Name}. Cows milked there get +{Catalog.CleanBonus * 100:0}% for an hour. +{Catalog.CleanXp} farmer xp", 1);
            plugin.SaveConfig();
        }, e =>
        {
            cleaning = false;
            Fail(e);
        });
    }

    private readonly Dictionary<string, double> groomReady = new();

    public double GroomWait(string cowId) => groomReady.TryGetValue(cowId, out var t) ? Math.Max(0, t - now) : 0;

    public string? GroomProblem(string cowName, string cowId, float distance)
    {
        var b = Blocked();
        if (b != null) return b;
        if (!IsFarmer) return "Only farmers groom cows.";
        if (!Where.InDairy) return "Go to your dairy first.";
        if (distance < 0 || distance > HandRange) return $"Get within {HandRange:0} yalms of {cowName} first.";
        var w = GroomWait(cowId);
        if (w > 0) return $"You can groom {cowName} again in {Wait(TimeSpan.FromSeconds(w))}.";
        return null;
    }

    private bool grooming;

    public void Groom(string cowName, string cowId, float distance)
    {
        var p = GroomProblem(cowName, cowId, distance);
        if (p != null)
        {
            Fail(p);
            return;
        }

        if (grooming) return;
        grooming = true;
        Touch();
        Net.Groom(cowId, () =>
        {
            grooming = false;
            groomReady[cowId] = now + 15 * 60;
            AddFarmerXp(Catalog.GroomXp);
            F.DayGrooms++;
            EarnDairyXp(2);
            Say($"You groom {cowName}. Their Happy bar gets 30 more minutes. +{Catalog.GroomXp} farmer xp", 1);
        }, e =>
        {
            grooming = false;
            groomReady[cowId] = now + 60;   // the server knows the real wait; don't spam it
            Fail(e);
        });
    }

    public HashSet<ushort> HandEmoteIds() => F.HandEmotes!.Select(Emotes.Find).Where(id => id != 0).ToHashSet();

    private bool handFlowing;
    private double nextHandHint, nextSquirt;

    /// <summary>Is this farmer milking you right now: a looping emote (yours, or any if allowed), close, and targeting you?</summary>
    private bool Milking(FarmhandSight s) =>
        s.Present && s.Emote != 0 && s.Distance <= HandRange && (!HandNeedsTarget || s.TargetingYou) &&
        (F.AnyLoopEmote || HandEmoteIds().Contains(s.Emote));

    private double nextIntentCheck;

    /// <summary>
    /// Did this farmer press Milk on you? A looping emote alone never milks anyone: their plugin has to say they mean you.
    /// When someone is looping near you and we haven't heard yet, ask the server again (every few seconds).
    /// </summary>
    private bool PressedMilkOnMe(Farmhand f)
    {
        // Test farmhands, and farmhands in a dairy that isn't on the network (you start those yourself)
        if (f.IsTest || !Networked) return true;
        var myName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "";
        var st = Net.Mine?.Members.FirstOrDefault(m => m.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase))?.Status;
        if (st != null && myName.Length > 0 && st.Milking.Equals(myName, StringComparison.OrdinalIgnoreCase)) return true;
        if (now >= nextIntentCheck)
        {
            nextIntentCheck = now + 3;
            Net.RefreshMine();
        }

        return false;
    }

    /// <summary>A farmer milking you: holding the loop close by, and they pressed Milk on you.</summary>
    private bool MilkingMe(Farmhand f, FarmhandSight s) => Milking(s) && PressedMilkOnMe(f);

    /// <summary>Adds whatever emote this farmhand is doing right now to the milking emotes.</summary>
    public bool RecordEmote(Farmhand fh)
    {
        var sight = Sight(fh);
        if (!sight.Present || sight.Emote == 0) return Fail("They aren't doing an emote right now.");
        var cmd = Emotes.Command(sight.Emote);
        if (cmd.Length == 0) return Fail("That emote has no text command.");
        if (cmd.Equals("/kneel", StringComparison.OrdinalIgnoreCase)) return Fail("/kneel doesn't work for hand milking.");
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
                    // Match by name (a member's listed world can be where they registered, not their home world)
                    var fh = wanted.FirstOrDefault(f => f.Name.Equals(pc.Name.TextValue, StringComparison.OrdinalIgnoreCase) && (f.WorldId == 0 || f.WorldId == pc.HomeWorld.RowId))
                             ?? wanted.FirstOrDefault(f => f.Name.Equals(pc.Name.TextValue, StringComparison.OrdinalIgnoreCase));
                    if (fh != null) handObjects[Key(fh)] = pc;
                }
            }
        }

        // A farmer close by and targeting you may be grooming or milking you: check in with the server sooner
        if (Networked && Farmhands.Any(f => !f.IsTest && Sight(f) is { Present: true, TargetingYou: true } s && s.Distance <= HandRange))
            Net.BeatWithin(10);

        // A farmer of your dairy can start milking you just by targeting you and using one of your milking emotes
        if (Active == null && F.AutoHand && Networked && ProducesMilk)
        {
            foreach (var f in Farmhands.Where(f => !f.IsTest))
            {
                if (!MilkingMe(f, Sight(f))) continue;
                var why = HandProblem(f);
                if (why != null)
                {
                    if (now >= nextHandHint)
                    {
                        nextHandHint = now + 20;
                        Say($"{f.Name} wants to milk you, but: {why}", 2);
                    }

                    continue;
                }

                if (StartHand(f)) Say($"{f.Name} is milking you.", 1);
                return;
            }
        }

        var s = Active;
        if (s?.Kind != SessionKind.Hand || s.Hand!.IsTest) return;

        // Milk flows while the farmer keeps a milking emote going, close by and targeting you
        var fhand = s.Hand;
        handFlowing = MilkingMe(fhand, Sight(fhand));
        if (handFlowing) s.LastActivity = now;
    }

    // =================================================================
    // Dairy and stalls
    // =================================================================

    /// <summary>Null when you can register the house you're standing in; otherwise why not.</summary>
    public string? DairyHereProblem()
    {
        if (Blocked() != null) return Blocked();
        if (Networked)
        {
            if (F.NetMember != null && !F.NetMember.IsOwner) return $"You're a member of {F.NetMember.Dairy}. Leave it first to start your own dairy.";
            if (Draft == null) return "Mark out your dairy's area first.";
            return null;
        }

        if (Estate.Current() == null) return "Go inside the house you want to use as your dairy.";
        return null;
    }

    /// <summary>Registers your own estate as your dairy (on the network when it's up, otherwise locally).</summary>
    public bool RegisterDairyHere(string name)
    {
        var p = DairyHereProblem();
        if (p != null) return Fail(p);
        var me = Plugin.ObjectTable.LocalPlayer!;
        var clean = string.IsNullOrWhiteSpace(name) ? "My Dairy" : name.Trim();

        if (Networked)
        {
            PublishDairy(clean, Draft);
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

        Say($"{F.Dairy.Name} is registered.", 1);
        plugin.SaveConfig();
        return true;
    }

    /// <summary>Sends your dairy and its listing settings to the network. Also used to save listing changes.</summary>
    /// <summary>Sends your dairy to the network: a new or moved area, or just the name, listing and your role.</summary>
    public void PublishDairy(string name, DairyArea? area = null)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return;
        var world = me.CurrentWorld.ValueNullable;
        var body = new
        {
            name,
            description = F.ListDescription,
            isPublic = F.ListPublic,
            showOwner = F.ListShowName,
            ownerName = me.Name.TextValue,
            worldName = (area != null ? Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRowOrDefault(area.World)?.Name.ExtractText() : null)
                        ?? world?.Name.ExtractText() ?? "",
            dcName = world?.DataCenter.ValueNullable?.Name.ExtractText() ?? "",
            role = F.OwnerRoles!.FirstOrDefault() ?? "none",
            roles = F.OwnerRoles,
            area,
        };
        var isNew = F.NetMember?.IsOwner != true;
        Net.PutDairy(body, _ =>
        {
            if (area != null) Draft = null;
            Say(isNew ? $"{name} is registered on the network. You own it." : area != null ? $"{name}'s area is saved." : $"{name} is updated.", 1);
        }, e => Fail(e));
    }

    // ----- Marking out the dairy's area -----

    /// <summary>The box being set up (drawn on the ground while it's set).</summary>
    public DairyArea? Draft { get; private set; }
    /// <summary>The box being marked is the dairy's private place, not its public area.</summary>
    public bool DraftPrivate { get; private set; }
    /// <summary>You're in your dairy's private place (members only; visitors never see it).</summary>
    public bool InPrivatePlace { get; private set; }

    private static bool SameArea(DairyArea? a, DairyArea? b) =>
        a == null ? b == null : b != null && a.Territory == b.Territory && a.World == b.World && a.House == b.House &&
                                 a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.Width == b.Width && a.Length == b.Length && a.Rot == b.Rot && a.Height == b.Height && a.WholeHouse == b.WholeHouse;

    /// <summary>Where you are, for the server to tell which dairy (if any) you're standing in.</summary>
    public object? Position()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return null;
        return new
        {
            territory = Plugin.ClientState.TerritoryType,
            world = me.CurrentWorld.RowId,
            house = Estate.Current()?.HouseId.ToString() ?? "",
            x = me.Position.X, y = me.Position.Y, z = me.Position.Z,
        };
    }

    private static bool InArea(DairyArea a, Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter me)
    {
        if (Plugin.ClientState.TerritoryType != a.Territory || me.CurrentWorld.RowId != a.World) return false;
        var house = Estate.Current();
        if (a.House.Length > 0)
        {
            if (house == null || !ulong.TryParse(a.House, out var hid) || !Estate.Same(hid, house.Value.HouseId)) return false;
        }
        else if (house != null) return false;

        return a.Contains(me.Position);
    }

    /// <summary>Starts marking out the dairy: your current box if you're there, or a new one centered on you.</summary>
    public void StartDraft(bool privatePlace = false)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return;
        DraftPrivate = privatePlace;
        var cur = privatePlace ? F.NetMember?.PrivateArea : F.NetMember?.Area;
        if (cur is { WholeHouse: true }) cur = null;   // drawing borders replaces a whole-house place with a box
        if (cur != null && InArea(cur, me) || cur != null && Plugin.ClientState.TerritoryType == cur.Territory && me.CurrentWorld.RowId == cur.World)
        {
            Draft = cur.Copy();
            return;
        }

        var house = Estate.Current();
        Draft = new DairyArea
        {
            Territory = Plugin.ClientState.TerritoryType,
            World = me.CurrentWorld.RowId,
            House = house?.HouseId.ToString() ?? "",
            X = me.Position.X, Y = me.Position.Y, Z = me.Position.Z,
            Rot = me.Rotation,
            MapId = Plugin.ClientState.MapId,
            Zone = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(Plugin.ClientState.TerritoryType)?.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
            Width = house != null ? 10 : 16,
            Length = house != null ? 10 : 12,
        };
    }

    public void CenterDraftOnMe()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (Draft == null || me == null) return;
        if (Plugin.ClientState.TerritoryType != Draft.Territory)
        {
            Fail("You're in a different zone now. Cancel and start again here.");
            return;
        }

        Draft.X = me.Position.X;
        Draft.Y = me.Position.Y;
        Draft.Z = me.Position.Z;
    }

    public void FaceDraftMyWay()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (Draft != null && me != null) Draft.Rot = me.Rotation;
    }

    public void CancelDraft() => Draft = null;

    /// <summary>Saves a moved or resized area for the dairy you own.</summary>
    public void SaveDraft()
    {
        if (Draft == null || F.NetMember?.IsOwner != true) return;
        if (Plugin.ClientState.TerritoryType != Draft.Territory)
        {
            Fail("Go back to where you marked the area to save it.");
            return;
        }

        if (DraftPrivate)
        {
            var area = Draft;
            area.WholeHouse = false;
            Net.SetPrivatePlace(area, () =>
            {
                F.NetMember!.PrivateArea = area;
                Draft = null;
                Say("Your dairy's private place is saved. Members count as in the dairy there; visitors never see it.", 1);
                plugin.SaveConfig();
            }, e => Fail(e));
            return;
        }

        PublishDairy(F.NetMember.Dairy, Draft);
    }

    /// <summary>The whole house or apartment you're in becomes the dairy's private place (no box to draw).</summary>
    public string? WholeHouseProblem()
    {
        if (F.NetMember?.IsOwner != true) return "Only the owner sets the private place.";
        if (Estate.Current() == null) return "Go inside your house or apartment first.";
        return null;
    }

    public void UseWholeHouseAsPrivate()
    {
        var p = WholeHouseProblem();
        var me = Plugin.ObjectTable.LocalPlayer;
        if (p != null || me == null)
        {
            if (p != null) Fail(p);
            return;
        }

        var area = new DairyArea
        {
            Territory = Plugin.ClientState.TerritoryType,
            World = me.CurrentWorld.RowId,
            House = Estate.Current()!.Value.HouseId.ToString(),
            X = me.Position.X, Y = me.Position.Y, Z = me.Position.Z,
            Width = 10, Length = 10,
            MapId = Plugin.ClientState.MapId,
            Zone = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(Plugin.ClientState.TerritoryType)?.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
            WholeHouse = true,
        };
        Draft = null;
        Net.SetPrivatePlace(area, () =>
        {
            F.NetMember!.PrivateArea = area;
            Say("This whole house is now your dairy's private place. Members count as in the dairy anywhere inside it.", 1);
            plugin.SaveConfig();
        }, e => Fail(e));
    }

    public void RemovePrivatePlace()
    {
        if (F.NetMember?.IsOwner != true) return;
        Net.RemovePrivatePlace(() =>
        {
            F.NetMember!.PrivateArea = null;
            Say("The private place is removed. Stalls there stop working.", 1);
            plugin.SaveConfig();
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
            if (Stalls.Count >= MaxStalls) return $"A dairy can have up to {MaxStalls} stalls.";
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

    /// <summary>Null when you can move this stall to the seat you're sitting on (it keeps its tier).</summary>
    public string? MoveStallProblem(Stall s)
    {
        if (Blocked() != null) return Blocked();
        if (Networked && !IsOwner) return "Only the dairy's owner moves stalls.";
        if (!Where.InDairy) return "Stalls go inside your dairy. " + Where.Why;
        if (!Where.Seated) return "Sit on the seat you want this stall to move to.";
        if (Where.AtStall == s) return "It's already here.";
        if (Where.AtStall != null) return $"That seat is already {Where.AtStall.Name}.";
        return null;
    }

    public void MoveStall(Stall s)
    {
        var p = MoveStallProblem(s);
        if (p != null)
        {
            Fail(p);
            return;
        }

        var me = Plugin.ObjectTable.LocalPlayer!;
        var pos = me.Position;
        if (Active?.Stall == s) Stop("Stall moved.");
        if (Networked)
        {
            Net.MoveStall(s.Id, pos.X, pos.Y, pos.Z, () => Say($"{s.Name} moved here (still a {Catalog.StallTierNames[s.Tier].ToLowerInvariant()}).", 1), e => Fail(e));
            return;
        }

        s.X = pos.X;
        s.Y = pos.Y;
        s.Z = pos.Z;
        Say($"{s.Name} moved here.", 1);
        plugin.SaveConfig();
    }

    /// <summary>Is this stall inside the dairy's area? (After moving the dairy, stalls left outside stop working until moved.)</summary>
    /// <summary>The stalls in the place you're standing in (the public area or the private place), for drawing them.</summary>
    /// <remarks>
    /// Stalls only know their position, not their zone, so a stall counts as "here" when it's inside the box of the place
    /// you're in and close to you (an interior is small; the public area is at most 80 y across).
    /// </remarks>
    public IEnumerable<Stall> StallsHere()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (!Where.InDairy || me == null) return Enumerable.Empty<Stall>();
        var pos = me.Position;
        static Vector3 P(Stall s) => new(s.X, s.Y, s.Z);
        bool Near(Stall s, float range) => Vector3.Distance(P(s), pos) <= range;
        var m = F.NetMember;
        if (!Networked || m == null) return Stalls.Where(s => Near(s, 120));
        if (InPrivatePlace && m.PrivateArea is { } priv) return Stalls.Where(s => priv.Contains(P(s)) && Near(s, 60));
        return Stalls.Where(s => (m.Area?.Contains(P(s)) ?? true) && Near(s, 120));
    }

    public bool StallInArea(Stall s)
    {
        var p = new Vector3(s.X, s.Y, s.Z);
        if (F.NetMember?.PrivateArea is { } priv && priv.Contains(p)) return true;
        return F.NetMember?.Area is not { } a || a.Contains(p);
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
        if (u.Farmer && !CanCarry) return "For farmers and dairy owners.";
        var lvl = u.Farmer ? FarmerLevel : Level;
        if (lvl < u.Unlocks[have]) return $"Unlocks at {(u.Farmer ? "farmer " : "")}level {u.Unlocks[have]}.";
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
            Net.UpgradeStall(s.Id, next, () =>
            {
                F.Stats.BestStallTier = Math.Max(F.Stats.BestStallTier, next);
                Say($"{s.Name} is now a {Catalog.StallTierNames[next].ToLowerInvariant()} (paid from the dairy fund).", 1);
                plugin.SaveConfig();
            }, e => Fail(e));
            return true;
        }

        s.Tier = next;
        F.Stats.BestStallTier = Math.Max(F.Stats.BestStallTier, next);
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
        if (!CanCarry) return;   // farmers and owners deliver; cows are paid at milking time
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

    /// <summary>Delivering is farmer work, so orders grow with your farmer level (the owner's with their best level).</summary>
    private int DeliveryLevel => IsFarmer ? FarmerLevel : Math.Max(Level, FarmerLevel);

    private Order NewOrder()
    {
        var lvl = DeliveryLevel;
        var maxGrade = BestTankGrade();   // orders ask for grades your dairy has
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

    /// <summary>Best grade in your dairy's tank (or what you carry), so orders ask for milk you can get.</summary>
    private int BestTankGrade()
    {
        var best = 0;
        var t = Net.Progress?.Tank?.ByGrade;
        for (var g = 0; g < Grades.Count; g++)
            if ((t != null && g < t.Count && t[g] >= 8) || F.Tank![g] >= 8) best = g;
        return Math.Min(best, 2 + DeliveryLevel / 10);
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
        if (Networked) Net.UseCarried(o.Oz);
        Earn(o.Coins);
        if (CanCarry) AddFarmerXp(o.Xp);   // delivering is farmer work
        else AddXp(o.Xp);
        F.Stats.OrdersFilled++;
        F.DayOrders++;
        F.DayDelivered++;
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
    private readonly float[] pendingPump = new float[5];

    /// <summary>Milk pumped at the dairy's stalls since the last heartbeat, by grade, for the dairy's tank (a copy; yours is kept).</summary>
    public float[]? TakePumped()
    {
        if (pendingPump.All(v => v <= 0)) return null;
        var r = pendingPump.Select(v => (float)Math.Round(v, 2)).ToArray();
        Array.Clear(pendingPump);
        return r;
    }

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
    public string[] InvitableRoles => !CanInvite ? Array.Empty<string>() : new[] { "cow", "bull", "farmer" };

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
        return me.IsOwner || (IsFarmer && !m.Has("farmer"));
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

    // ----- Roles: several at once. Anyone adds cow or bull to themselves; farmer is given by a farmer or the owner;
    //       only the owner takes roles away. -----

    public string? AddRoleProblem(MemberInfo? m, string role)
    {
        var me = F.NetMember;
        if (!Networked || me == null) return "Join a dairy first.";
        var self = m == null || m.Id == Net.Mine?.You?.Id;
        var has = self ? MyRoles.Contains(role) : m!.Has(role);
        if (has) return null;
        if (me.IsOwner) return null;
        if (role == "farmer") return self ? "Farmer is given by a farmer or the owner." : IsFarmer ? null : "Only farmers and the owner give the farmer role.";
        return self || IsFarmer ? null : "Only the owner and farmers give others a role.";
    }

    public void AddRole(MemberInfo? m, string role)
    {
        var p = AddRoleProblem(m, role);
        if (p != null)
        {
            Fail(p);
            return;
        }

        var id = m?.Id ?? Net.Mine?.You?.Id;
        if (id == null) return;
        Net.ChangeRole(id, role, null, r => Say(m == null ? $"You're now a {Roles.Text(r)}." : $"{m.Name} is now a {Roles.Text(r)}.", 1), e => Fail(e));
    }

    public void RemoveRole(MemberInfo? m, string role)
    {
        if (F.NetMember?.IsOwner != true)
        {
            Fail("Only the owner can take a role away.");
            return;
        }

        var id = m?.Id ?? Net.Mine?.You?.Id;
        if (id == null) return;
        Net.ChangeRole(id, null, role, r => Say(m == null ? $"Your roles: {Roles.Text(r)}." : $"{m.Name}: {Roles.Text(r)}.", 1), e => Fail(e));
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
        if (!CanCarry) return "Only farmers and owners deliver milk.";
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
            F.DayDelivered++;
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
            var grade = Math.Min(BestTankGrade(), r.Next(0, 3));
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
        if (!CanCarry) return Fail("Only farmers and owners deliver milk.");
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

    public sealed record Goal(string Name, float Have, float Need, int Bit, bool Farmer);

    /// <summary>Your daily goals: a set for cows and a separate set for farmers (you get both if you're both).</summary>
    public IReadOnlyList<Goal> Goals()
    {
        var list = new List<Goal>();
        if (ProducesMilk)
        {
            list.Add(new Goal("Give 10 oz of milk", F.DayOz, 10, 0, false));
            list.Add(new Goal("Get milked at a stall", F.DayStall, 1, 1, false));
            list.Add(new Goal("Do 3 care tasks", F.DayCare, 3, 2, false));
            list.Add(new Goal("Get hand milked", F.DayHand, 1, 3, false));
        }

        if (IsFarmer)
        {
            list.Add(new Goal("Groom 2 cows", F.DayGrooms, 2, 8, true));
            list.Add(new Goal("Clean a stall", F.DayCleans, 1, 9, true));
            list.Add(new Goal("Fill an order or ship to the contract", F.DayDelivered, 1, 10, true));
            list.Add(new Goal("Hand milk a cow until they're empty", F.DayCowsMilked, 1, 11, true));
        }

        return list;
    }

    public bool Claimed(Goal g) => (F.DayClaimed & (1 << g.Bit)) != 0;
    public const int GoalCoins = 30;
    public const int GoalXp = 40;
    public const int AllGoalsCoins = 60;

    public bool Claim(Goal g)
    {
        if (Claimed(g) || g.Have < g.Need) return false;
        F.DayClaimed |= 1 << g.Bit;
        Earn(GoalCoins);
        if (g.Farmer) AddFarmerXp(GoalXp);
        else AddXp(GoalXp);
        F.Stats.DailiesDone++;
        // Each set has its own bonus for finishing all four
        var all = Goals().Where(x => x.Farmer == g.Farmer).All(Claimed);
        if (all) Earn(AllGoalsCoins);
        var set = g.Farmer ? "farmer" : "cow";
        Say(all ? $"All {set} goals done! +{GoalCoins + AllGoalsCoins} coins" : $"Daily goal done. +{GoalCoins} coins, +{GoalXp} {set} xp", 1);
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
        F.DayStall = 0;
        F.DayClaimed = 0;
        F.DayGrooms = 0;
        F.DayCleans = 0;
        F.DayDelivered = 0;
        F.DayCowsMilked = 0;
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
        if (amount > 0) amount *= 1f + (Owned("heifer") > 0 ? 0.1f : 0) + (Owned("champion") > 0 ? 0.2f : 0);
        F.Quality += amount;
        var after = Grades.Cap(F.Quality);
        for (var g = before + 1; g <= after; g++)
            Say($"Milk quality up! Your milk can now be Grade {Grades.Name(g)} ({Grades.All[g].Blurb}).", 1);
    }

    /// <summary>Quality needed for the next grade, or null at the top.</summary>
    public float? NextQuality => GradeCap + 1 < Grades.QualityNeed.Length ? Grades.QualityNeed[GradeCap + 1] : null;

    // ----- XP ticks while making milk -----
    private float madeSinceTick;      // milk made since the last +1 xp
    private float gradeExtra;         // the grade bonus's extra xp, paid out in whole points

    /// <summary>The last XP tick, for the HUD's little "+xp" (amount, when).</summary>
    public (int Amount, double At) LastXpTick { get; private set; }

    public double Now => now;

    /// <summary>+1 xp for every 0.1 oz made, right away. Better grades add a little extra on top.</summary>
    private void TickXp()
    {
        if (madeSinceTick < Leveling.OzPerXpTick) return;
        var n = (int)(madeSinceTick / Leveling.OzPerXpTick);
        madeSinceTick -= n * Leveling.OzPerXpTick;
        gradeExtra += n * (Leveling.GradeBonus(CurrentGrade) - 1f);
        var extra = (int)gradeExtra;
        gradeExtra -= extra;
        AddXp(n + extra);
        LastXpTick = (n + extra + (now - LastXpTick.At < 3 ? LastXpTick.Amount : 0), now);
    }

    private void AddXp(int amount)
    {
        if (!ProducesMilk) return;   // cow xp is only for cows; farmer work gives farmer xp
        var before = Level;
        F.Xp += amount;
        var after = Level;
        for (var l = before + 1; l <= after; l++)
        {
            var unlocks = Leveling.UnlocksAt(l, Networked).ToList();
            Say($"Cow level {l}! You're a {Leveling.Rank(l)}." + (unlocks.Count > 0 ? " Unlocked: " + string.Join(", ", unlocks) : ""), 1);
        }
    }

    /// <summary>Farmer xp: hand milking pay, filling jugs, deliveries and contracts.</summary>
    private void AddFarmerXp(int amount)
    {
        if (amount <= 0) return;
        var before = FarmerLevel;
        F.FarmerXp += amount;
        for (var l = before + 1; l <= FarmerLevel; l++)
        {
            var tank = Catalog.Upgrades.First(u => u.Id == "farmtank");
            var unlock = Array.IndexOf(tank.Unlocks, l);
            Say($"Farmer level {l}!" + (unlock >= 0 ? $" Unlocked: {Catalog.FarmerTankNames[unlock + 1]} ({Catalog.FarmerTankSizes[unlock + 1]} oz) in the Shop." : ""), 1);
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
        Cow.FullSinceUtc = DateTime.UtcNow.AddHours(-SpoilHours).AddSeconds(-1);
    }

    /// <summary>Testing: fill every bottle with only a minute left before it spoils.</summary>
    public void DevNearlySpoiled()
    {
        if (!Dev) return;
        Stop();
        Cow.Milk = Capacity;
        Cow.FullSinceUtc = DateTime.UtcNow.AddHours(-SpoilHours).AddMinutes(1);
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
        Cow.Hunger = Cow.Thirst = Cow.Comfort = Cow.Mood = 100;
        Cow.FedBonus = 0.07f;
        Cow.WaterBonus = Catalog.TroughBonus;
        Cow.ComfortBonus = ComfyNow;
        Cow.HappyBonus = 0.05f;
    }

    public void DevStroke()
    {
        if (!Dev) return;
        if (Active?.Kind == SessionKind.Hand) Stroke(Active.Hand!);
    }
}
