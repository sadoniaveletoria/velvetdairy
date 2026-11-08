using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace VelvetDairy;

/// <summary>
/// Saved settings. Everything for this version lives under <see cref="Farm"/> so it can't clash with saves from the old prototype.
/// </summary>
[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public FarmSave Farm { get; set; } = new();

    public void EnsureDefaults() => Farm.EnsureDefaults();
}

[Serializable]
public class FarmSave
{
    // ----- Gate and preferences -----
    public bool Confirmed18 { get; set; }
    public bool ShowHud { get; set; } = true;
    public bool LockHud { get; set; }
    public bool ChatEcho { get; set; } = true;
    public bool AutoStall { get; set; } = true;       // sitting in a stall starts milking by itself
    public bool Paused { get; set; }                 // developer only
    public string DevKey { get; set; } = "";         // developer phrase, checked against a hash in the code
    public float MilkSpeed { get; set; } = 1f;       // multiplies how fast milk builds up
    public bool SoundOn { get; set; } = true;
    public float SoundVolume { get; set; } = 0.6f;

    // ----- Your dairy -----
    public DairyPlace? Dairy { get; set; }
    public float DairyRadius { get; set; }            // 0 = anywhere inside the house counts
    public List<Stall>? Stalls { get; set; }
    public List<Farmhand>? Farmhands { get; set; }
    public List<string>? HandEmotes { get; set; }     // emote commands a farmhand uses to milk you
    public bool AutoHand { get; set; } = true;
    public bool AnyLoopEmote { get; set; } = true;    // any looping emote from a farmer counts, not just the ones listed        // a farmer of your dairy starts hand milking you just by using one of your emotes
    public float HandRange { get; set; } = 3f;
    public float StallRange { get; set; } = 0.9f;
    public bool DevCanSleep { get; set; }          // developer: let the anti-AFK rules apply to you for testing
    public bool HandNeedsTarget { get; set; } = true; // farmhand must be targeting you

    // ----- You (the cow) -----
    public CowState Cow { get; set; } = new();
    public int Coins { get; set; } = 60;
    public Dictionary<string, int> Pantry { get; set; } = new();      // feed item id -> count
    public List<string>? HiddenFeed { get; set; }                     // feed you hid from the lists
    public bool CareShowEmpty { get; set; }                           // Care list also shows feed you're out of
    public Dictionary<string, int> Upgrades { get; set; } = new();    // upgrade id -> level owned
    public float[]? Tank { get; set; }                                // stored milk (oz) by grade index
    public bool StarterGiven { get; set; }

    // ----- Creamery -----
    public List<Order>? Orders { get; set; }
    public DateTime NextOrderUtc { get; set; } = DateTime.MinValue;
    public int OrderSerial { get; set; }

    // ----- Daily goals -----
    public string DayKey { get; set; } = "";
    public float DayOz { get; set; }
    public int DayOrders { get; set; }
    public int DayCare { get; set; }
    public int DayHand { get; set; }
    public int DayClaimed { get; set; }               // bitmask of claimed goals

    // ----- Progress -----
    public int Xp { get; set; }
    public float Quality { get; set; }                // milk quality score: unlocks better grades
    public HashSet<string> Awards { get; set; } = new();
    public FarmStats Stats { get; set; } = new();
    public List<string>? Log { get; set; }

    // ----- Velvet Dairy Network -----
    public string NetId { get; set; } = "";          // random id for this install (not your character)
    public string NetKey { get; set; } = "";         // secret that proves it's this install
    public bool NetEnabled { get; set; } = true;
    public string NetServerOverride { get; set; } = "";   // developer only: test server address
    public NetMember? NetMember { get; set; }         // the dairy you belong to (cached from the server)
    public bool ListPublic { get; set; } = true;      // your dairy shows its address in the directory
    public bool ListShowName { get; set; }            // your character name shows as the owner
    public string ListDescription { get; set; } = "";
    public string OwnerRole { get; set; } = "cow";    // older single role (kept for old saves)
    public List<string>? OwnerRoles { get; set; }      // what you play in your own dairy: any of cow, farmer, bull
    public bool ShowOwnerHud { get; set; } = true;
    public string VisitDay { get; set; } = "";
    public float DayAwakeMinutes { get; set; }
    public int DayStall { get; set; }                 // stall milkings today (daily goal for cows)
    public bool StockPaidOut { get; set; }
    public bool TutorialDone { get; set; }
    public int FarmerXp { get; set; }                  // each role levels on its own (Xp is the cow's)
    public int BullXp { get; set; }             // the first-run walkthrough was shown            // old sellable milk was paid out when milk started paying at milking time        // minutes of milk-making today (tired after 12 h)        // bonus orders at other dairies, per day
    public List<string>? VisitOrdersDone { get; set; }

    // ----- Save protection -----
    public string Seal { get; set; } = "";            // signature of the progress above (see Game/Integrity.cs)
    public int EditsCaught { get; set; }              // times an edited save was caught and undone

    public static readonly string[] DefaultHandEmotes = { "/beesknees" };

    public void EnsureDefaults()
    {
        Stalls ??= new List<Stall>();
        HiddenFeed ??= new List<string>();
        Farmhands ??= new List<Farmhand>();
        if (HandEmotes == null || HandEmotes.Count == 0) HandEmotes = new List<string>(DefaultHandEmotes);
        // The old defaults were one-shot gestures the game doesn't report for other players; swap them for a looping one
        if (HandEmotes.Count == 3 && HandEmotes.Contains("/pet") && HandEmotes.Contains("/comfort") && HandEmotes.Contains("/hug")) HandEmotes = new List<string>(DefaultHandEmotes);
        // /kneel doesn't work for hand milking at all, so it's never a milking emote
        HandEmotes.RemoveAll(e => e.Trim().Equals("/kneel", StringComparison.OrdinalIgnoreCase) || e.Trim().Equals("kneel", StringComparison.OrdinalIgnoreCase));
        if (HandEmotes.Count == 0) HandEmotes = new List<string>(DefaultHandEmotes);
        if (Tank == null || Tank.Length != Game.Grades.Count) Tank = new float[Game.Grades.Count];
        Orders ??= new List<Order>();
        Log ??= new List<string>();
        VisitOrdersDone ??= new List<string>();
        OwnerRoles ??= OwnerRole is "cow" or "farmer" or "bull" ? new List<string> { OwnerRole } : new List<string>();
        if (Math.Abs(StallRange - 1.6f) < 0.01f) StallRange = 0.9f;   // old default was loose enough to count sitting beside a stall
        if (!StarterGiven)
        {
            Pantry["hay"] = 6;
            Pantry["clover"] = 1;
            StarterGiven = true;
        }
    }
}

[Serializable]
public class NetMember
{
    public string DairyId { get; set; } = "";
    public string Dairy { get; set; } = "";
    public string Role { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public bool IsOwner { get; set; }
    public string HouseId { get; set; } = "";
    public int Level { get; set; } = 1;
    public VelvetDairy.Game.DairyArea? Area { get; set; }   // the dairy's box (null for older house-only dairies)
}

[Serializable]
public class DairyPlace
{
    public string Name { get; set; } = "My Dairy";
    public uint Territory { get; set; }
    public uint World { get; set; }
    public string WorldName { get; set; } = "";
    public int Ward { get; set; } = -1;
    public int Plot { get; set; } = -1;
    public float X { get; set; }   // where you stood when you registered (used by the range limit)
    public float Y { get; set; }
    public float Z { get; set; }
}

[Serializable]
public class Stall
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Stall";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int Tier { get; set; } = 1;   // 1..4
    public int Sessions { get; set; }
}

[Serializable]
public class Farmhand
{
    public string Name { get; set; } = "";
    public uint WorldId { get; set; }
    public string WorldName { get; set; } = "";
    public bool IsTest { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public int Sessions { get; set; }
    public float OzMilked { get; set; }

    public string Display => IsTest ? "Test farmhand" : WorldName.Length > 0 ? $"{Name} ({WorldName})" : Name;
}

[Serializable]
public class CowState
{
    // Need bars: 100 = an hour of bonus left, 0 = plain (no bonus, never a penalty). Each use adds half a bar.
    // They only tick down while you're awake and logged in.
    public float Hunger { get; set; }    // Fed
    public float Thirst { get; set; }    // Watered
    public float Comfort { get; set; }   // Comfy
    public float Mood { get; set; }      // Happy
    public float Richness { get; set; }  // no longer used (kept so old saves load)
    public float FedBonus { get; set; }       // milk bonus from the last food, as a fraction
    public float WaterBonus { get; set; }
    public float ComfortBonus { get; set; }
    public float HappyBonus { get; set; }
    public DateTime FedReadyUtc { get; set; } = DateTime.MinValue;     // 15-minute cooldowns: eat, drink, rest, treat
    public DateTime WaterReadyUtc { get; set; } = DateTime.MinValue;
    public DateTime ComfyReadyUtc { get; set; } = DateTime.MinValue;
    public DateTime HappyReadyUtc { get; set; } = DateTime.MinValue;
    public DateTime? LastMilkedUtc { get; set; }        // freshness: grade slips if you go days without being milked
    public DateTime FreshHoldUntilUtc { get; set; } = DateTime.MinValue;   // Dry-off tonic: no slipping until then
    public float Milk { get; set; } = 3;       // oz waiting to be milked (across all your bottles)
    public DateTime BoostUntilUtc { get; set; } = DateTime.MinValue;   // treat boost
    public DateTime? FullSinceUtc { get; set; }   // when every bottle became full (spoils 72 h later)
    public bool Spoiled { get; set; }             // bottles have gone bad: dump them before anything else
    public float Taint { get; set; }              // off taste left from dumping (0-100); one grade lower while it lasts
    public DateTime QualityTreatReadyUtc { get; set; } = DateTime.MinValue;   // next quality treat allowed
    public DateTime LastTickUtc { get; set; } = DateTime.UtcNow;
}

[Serializable]
public class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public int Oz { get; set; }
    public int MinGrade { get; set; }
    public int Coins { get; set; }
    public int Xp { get; set; }
}

[Serializable]
public class FarmStats
{
    public float TotalOz { get; set; }
    public int StallSessions { get; set; }
    public int HandSessions { get; set; }
    public int OrdersFilled { get; set; }
    public int BottlesSold { get; set; }
    public int CoinsEarned { get; set; }
    public int CareTasks { get; set; }
    public int BestGrade { get; set; } = -1;
    public float BestSessionOz { get; set; }
    public int DailiesDone { get; set; }
    public int TimesDumped { get; set; }
    public float OzDumped { get; set; }
}
