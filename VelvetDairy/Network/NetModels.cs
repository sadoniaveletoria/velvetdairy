using System;
using System.Collections.Generic;

namespace VelvetDairy.Network;

// Shapes of what the Eorzea Milkers Network server sends back (see server/src/index.js). JSON is camelCase.

/// <summary>A short notice to everyone in your dairy (like a cow ready to be bred again).</summary>
public sealed class DairyNotice
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class HeartbeatReply
{
    public List<DairyNotice> Notices { get; set; } = new();
    public int Online { get; set; }
    public HereInfo? Here { get; set; }
    public MembershipInfo? Membership { get; set; }
    public List<InviteInfo> Invites { get; set; } = new();
    public int Applications { get; set; }          // waiting for you to answer (owner, or farmers if allowed)
    public int Inbox { get; set; }
    public int DairyXp { get; set; }
    public CreameryInfo? Creamery { get; set; }
    public CreameryNow? CreameryHere { get; set; }   // standing at a Grand Creamery: who else is there (any world) and today's deliveries
    public List<CreameryInfo>? Creameries { get; set; }   // up to 3 (one per Grand Company); Creamery is the first
    public float Carried { get; set; }
    public List<MyDairyEntry> Dairies { get; set; } = new();   // every dairy you belong to (you can be in several, and own one)
}

/// <summary>One of the dairies you belong to.</summary>
public sealed class MyDairyEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsOwner { get; set; }
    public string Role { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public string RoleText => VelvetDairy.Game.Roles.Text(VelvetDairy.Game.Roles.From(Roles, Role));
}

/// <summary>Where the Grand Creamery is (set by the developer): weekly contracts are delivered there.</summary>
/// <summary>At a Grand Creamery right now: everyone else there on any world, and what's been shipped there today.</summary>
public sealed class CreameryNow
{
    public int Slot { get; set; }
    public List<CreameryPerson> Here { get; set; } = new();
    public int TodayOz { get; set; }
    public int TodayDairies { get; set; }
}

public sealed class CreameryPerson
{
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
}

public sealed class CreameryInfo
{
    public string Name { get; set; } = "";
    public uint Territory { get; set; }
    public uint MapId { get; set; }
    public string Zone { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 8;
    public int Slot { get; set; }
}

public sealed class HereInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPublic { get; set; }
    public bool Mine { get; set; }
    public bool Private { get; set; }   // you're in your dairy's private place

    // A guest in someone else's public dairy: what your plugin needs to be milked and cared for there
    public bool Guest { get; set; }
    public string HouseId { get; set; } = "";
    public VelvetDairy.Game.DairyArea? Area { get; set; }
    public List<StallInfo> Stalls { get; set; } = new();
    public List<GuestFarmer> Farmers { get; set; } = new();
}

/// <summary>A farmer of the public dairy you're visiting, and whom they pressed Milk on.</summary>
public sealed class GuestFarmer
{
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public string Milking { get; set; } = "";
    public string Refused { get; set; } = "";
}

/// <summary>Another player's roles and whom they've allowed to breed them (for breeding across dairies).</summary>
public sealed class PlayerLookup
{
    public bool Found { get; set; }
    public List<string> Roles { get; set; } = new();
    public string DairyId { get; set; } = "";
    public string Dairy { get; set; } = "";
    public string BreedOk { get; set; } = "";
}

public sealed class MembershipInfo
{
    public string DairyId { get; set; } = "";
    public string Dairy { get; set; } = "";
    public string Role { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public bool IsOwner { get; set; }
    public int Level { get; set; } = 1;

    public List<string> AllRoles => VelvetDairy.Game.Roles.From(Roles, Role);
}

public sealed class InviteInfo
{
    public long Id { get; set; }
    public string Role { get; set; } = "";
    public string Dairy { get; set; } = "";
    public string World { get; set; } = "";
}

public sealed class DirectoryReply
{
    public int Online { get; set; }
    public List<DairyListing> Dairies { get; set; } = new();
    public List<MyApplication> Applications { get; set; } = new();
}

/// <summary>An application you sent: waiting, or denied lately (with when you can apply to that dairy again).</summary>
public sealed class MyApplication
{
    public long Id { get; set; }
    public string DairyId { get; set; } = "";
    public string Dairy { get; set; } = "";
    public string Status { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public long Sent { get; set; }
    public long RetryAt { get; set; }
}

/// <summary>An application to your dairy, waiting for an answer.</summary>
public sealed class ApplicationInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public string Note { get; set; } = "";
    public string Levels { get; set; } = "";
    public long Sent { get; set; }
}

public sealed class ApplicationsReply
{
    public List<ApplicationInfo> Applications { get; set; } = new();
}

/// <summary>What your dairy is looking for, and whether you can answer its applications.</summary>
public sealed class ApplyingInfo
{
    public List<string> LookingFor { get; set; } = new();
    public bool FarmersAnswer { get; set; }
    public bool CanAnswer { get; set; }
    public int Pending { get; set; }
}

public sealed class DairyListing
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsPublic { get; set; }
    public bool Mine { get; set; }
    public string Owner { get; set; } = "";
    public string WorldName { get; set; } = "";
    public string DcName { get; set; } = "";
    public string? HouseId { get; set; }
    public uint? World { get; set; }
    public string District { get; set; } = "";
    public int DistrictKind { get; set; }
    public int? Ward { get; set; }
    public int? Plot { get; set; }
    public int? Room { get; set; }
    public int Members { get; set; }
    public List<string> LookingFor { get; set; } = new();   // roles it takes applications for (empty: not taking any)
    public int CowsHere { get; set; }
    public int OpenOrders { get; set; }
    public int Level { get; set; } = 1;
    public int WeekXp { get; set; }
    public VelvetDairy.Game.DairyArea? Area { get; set; }

    /// <summary>
    /// Where it is, for the list: a box inside a house or apartment shows that home's district, ward and plot (from the
    /// house's id, which always says the right one); outdoors, the zone.
    /// </summary>
    public string Address => Area != null ? AreaAddress(Area, Ward ?? 0)
        : HouseId != null && ulong.TryParse(HouseId, out var hid) && VelvetDairy.Game.Estate.FromId(hid) is { Ward: > 0 } home ? home.Describe
        : Ward is null ? "Private" : Room is > 0 && Plot is null ? $"{District}, Ward {Ward}, apartment {Room}" : $"{District}, Ward {Ward}, Plot {Plot}";

    private string AreaAddress(VelvetDairy.Game.DairyArea a, int ward)
    {
        // Outdoors in a residential district: the district and ward
        if (a.House.Length == 0 && ward > 0 && VelvetDairy.Game.Estate.DistrictKind(a.Territory) > 0)
            return $"{VelvetDairy.Game.Estate.DistrictName(a.Territory)}, Ward {ward}" + (Plot is > 0 ? $", Plot {Plot}" : "") + " (outside)";
        if (a.House.Length == 0 || VelvetDairy.Game.Estate.SoloPlace(a.Territory) || VelvetDairy.Game.Estate.Island(a.Territory) || !ulong.TryParse(a.House, out var id)) return a.Zone;
        var addr = VelvetDairy.Game.Estate.FromId(id);
        return addr.Ward > 0 ? addr.Describe : a.Zone;
    }
}

public sealed class MyDairyReply
{
    public MyDairyInfo? Dairy { get; set; }
    public YouInfo? You { get; set; }
    public List<MemberInfo> Members { get; set; } = new();
    public List<StallInfo> Stalls { get; set; } = new();
    public List<MemberInfo> Guests { get; set; } = new();   // players from other dairies visiting this public dairy now
    public List<SentInvite> Invites { get; set; } = new();
    public DairyProgress? Progress { get; set; }
    public ApplyingInfo? Applying { get; set; }
}

/// <summary>A dairy's level, XP, shared fund and what its level allows.</summary>
public sealed class DairyProgress
{
    public int Xp { get; set; }
    public int Level { get; set; } = 1;
    public int LevelXp { get; set; }
    public int? NextXp { get; set; }
    public int Fund { get; set; }
    public int WeekXp { get; set; }
    public int StallSlots { get; set; } = 2;
    public int MaxTier { get; set; } = 2;
    public int NextStallPrice { get; set; }
    public List<int> TierPrices { get; set; } = new();
    public List<Donor> Donors { get; set; } = new();
    public DairyTank? Tank { get; set; }
    public BellInfo? Bell { get; set; }
    public float PayBonus { get; set; } = 1f;
    public ContractInfo? Contract { get; set; }
    public List<string> FeedBin { get; set; } = new() { "hay" };       // feed types farmers can give from the dairy's bin
    public List<FeedBinOffer> FeedBinShop { get; set; } = new();      // every feed type, its dairy level and fund price
    public long PumpUntil { get; set; }                                // the Velvet pump runs until this (unix seconds)
    public int PumpCost { get; set; } = 2500;
    public int PumpLevel { get; set; } = 5;
    public int PumpMinutes { get; set; } = 120;

    public float LevelProgress => NextXp is { } n && n > LevelXp ? Math.Clamp((Xp - LevelXp) / (float)(n - LevelXp), 0, 1) : 1;
    public int TierPrice(int tier) => tier >= 0 && tier < TierPrices.Count ? TierPrices[tier] : 0;
}

/// <summary>The dairy's milk tank: milk members pumped at its stalls, by grade.</summary>
public sealed class DairyTank
{
    public List<float> ByGrade { get; set; } = new();
    public float Total { get; set; }
    public List<Pumper> Pumpers { get; set; } = new();
    public float Seed { get; set; }              // the dairy's seed tank (bulls' seed, in oz)
    public List<Pumper> SeedPumpers { get; set; } = new();
}

/// <summary>The weekend milk contest (Saturday and Sunday, UTC): pumping into the tank also fills the contract.</summary>
public sealed class ContestInfo
{
    public bool On { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public List<Pumper> Top { get; set; } = new();
    public Pumper? LastChampion { get; set; }
}

public sealed class Pumper
{
    public string Name { get; set; } = "";
    public float Oz { get; set; }
}

public sealed class Donor
{
    public string Name { get; set; } = "";
    public int Coins { get; set; }
}

public sealed class LeaderboardReply
{
    public string Week { get; set; } = "";
    public List<LeaderRow> Dairies { get; set; } = new();
}

public sealed class LeaderRow
{
    public int Rank { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPublic { get; set; }
    public string WorldName { get; set; } = "";
    public int Level { get; set; }
    public int WeekXp { get; set; }
    public bool Mine { get; set; }
}

/// <summary>This week's creamery contract for the dairy.</summary>
public sealed class ContractInfo
{
    public string Week { get; set; } = "";
    public int OzNeed { get; set; }
    public float OzDone { get; set; }
    public int MinGrade { get; set; }
    public int RewardXp { get; set; }
    public int RewardFund { get; set; }
    public int EarnedXp { get; set; }     // what it pays if the week ends now (the share delivered)
    public int EarnedFund { get; set; }
    public bool Completed { get; set; }
    public List<Pumper> Shippers { get; set; } = new();
    public ContestInfo? Contest { get; set; }

    public float Left => Math.Max(0, OzNeed - OzDone);
    public float Progress => OzNeed > 0 ? Math.Clamp(OzDone / OzNeed, 0, 1) : 0;
}

public sealed class ShipReply
{
    public int Shipped { get; set; }
    public float OzDone { get; set; }
    public int OzNeed { get; set; }
    public bool Finished { get; set; }
}

public sealed class TakeReply
{
    public int Oz { get; set; }
    public int Grade { get; set; }
    public float Left { get; set; }
}

public sealed class HandPayReply
{
    public int Coins { get; set; }
}

public sealed class RolesReply
{
    public List<string> Roles { get; set; } = new();
}

public sealed class FundReply
{
    public int Fund { get; set; }
}

public sealed class MyDairyInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsPublic { get; set; }
    public bool ShowOwner { get; set; }
    public string HouseId { get; set; } = "";
    public uint World { get; set; }
    public string WorldName { get; set; } = "";
    public string District { get; set; } = "";
    public int DistrictKind { get; set; }
    public int Ward { get; set; }
    public int Plot { get; set; }
    public int Room { get; set; }
    public VelvetDairy.Game.DairyArea? Area { get; set; }
    public VelvetDairy.Game.DairyArea? PrivateArea { get; set; }   // members only: the dairy's private place (a home or apartment)
}

public sealed class FeedBinOffer
{
    public string Item { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Price { get; set; }
}

public sealed class YouInfo
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public bool IsOwner { get; set; }
}

public sealed class MemberInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public string Role { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public bool IsOwner { get; set; }
    public bool Online { get; set; }
    public long LastSeen { get; set; }      // unix seconds their plugin last checked in (0 = never)
    public string Version { get; set; } = "";   // their plugin version, as of their last check-in
    public bool Guest { get; set; }             // visiting from another dairy (not a member)
    public bool Here { get; set; }              // in this dairy right now (the server's view of where they are)
    public int Quota { get; set; } = 1;         // daily quota size a farmer or the owner set (0 Light .. 3 Devoted)

    public List<string> AllRoles => VelvetDairy.Game.Roles.From(Roles, Role);
    public bool Has(string role) => AllRoles.Contains(role);
    public string RoleText => VelvetDairy.Game.Roles.Text(AllRoles);
    public MemberStatus? Status { get; set; }
}

/// <summary>What a member's plugin shares with their own dairy.</summary>
public sealed class MemberStatus
{
    public float Milk { get; set; }
    public float Cap { get; set; }
    public int Grade { get; set; }
    public bool Spoiled { get; set; }
    public bool InDairy { get; set; }
    public int Level { get; set; }          // cow level
    public int FarmerLevel { get; set; }
    public int BullLevel { get; set; }
    public bool Crown { get; set; }         // a crown next to their name in the member list
    public int Seed { get; set; }           // a bull: seed output for his next breeding (20 to 100; 0 = not a bull)
    public string Preg { get; set; } = "";  // a cow: pregnancy stage from Corruption (first, second, third), or empty
    public string PregFather { get; set; } = "";
    public string BreedOk { get; set; } = "";      // a cow: the bull from another dairy she allowed to breed her this scene
    public string GuestAt { get; set; } = "";      // a guest: the public dairy they're visiting   // a pregnant cow: the father's name (from Corruption), so he can see it
    public float SeedOz { get; set; }       // a bull: seed waiting to be milked
    public float SeedCap { get; set; }      // a bull: what his vials hold (0 = not a bull)
    public int SeedGrade { get; set; }
    public bool SeedSpoiled { get; set; }
    // For a scan: what speeds them up now, their bred bonus, when a cow can be bred again, and who may scan them
    public List<ScanBonus> Bonuses { get; set; } = new();
    public float Speed { get; set; }
    public string BredBy { get; set; } = "";
    public float BredPct { get; set; }
    public long BredUntil { get; set; }
    public long BreedWait { get; set; }
    public string Scan { get; set; } = "anyone";
    public long W { get; set; }                         // when the server last stored this (unix seconds)
    public List<int> Needs { get; set; } = new();       // minutes left on Fed, Watered, Comfy, Happy
    public bool StudTonic { get; set; }          // a bull who had a Stud draught: his next breeding is stronger
    public string Breeding { get; set; } = "";   // both cow and bull: the scene partner (Name@World) they chose to breed this scene
    public int Quality { get; set; }        // milk quality score (cows)
    public int TotalOz { get; set; }        // all the milk they've ever given
    public List<string> Emotes { get; set; } = new();   // the emotes this cow's farmers milk her with
    public string HandBy { get; set; } = "";            // the farmer milking her right now, if any
    public bool AnyEmote { get; set; } = true;          // any looping emote milks them (not only their list)
    public string Milking { get; set; } = "";           // a farmer: the cow they pressed Milk on right now
    public bool Ask { get; set; }                       // a cow who presses Allow before each hand milking
    public string Refused { get; set; } = "";           // a cow: the farmer they just told "Not now"
    public long TreatAt { get; set; }                   // a cow: when she can have her next treat (unix seconds)
}

public sealed class ScanBonus
{
    public string N { get; set; } = "";
    public float P { get; set; }   // percent: +15 is 15% faster, -50 half speed
}

/// <summary>Who's at a dairy right now (Dairies list > Who's here).</summary>
public sealed class HereReply
{
    public string Dairy { get; set; } = "";
    public List<HerePerson> People { get; set; } = new();
    public int Hidden { get; set; }   // there, but they don't share (scan setting) or aren't in a dairy
    public long Now { get; set; }
    public float TankMilk { get; set; }
    public float TankSeed { get; set; }
    public int Stalls { get; set; }
}

public sealed class HerePerson
{
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public bool Member { get; set; }      // a member of this dairy (else visiting, from From)
    public string From { get; set; } = "";
    public int CowLevel { get; set; }
    public int BullLevel { get; set; }
    public int FarmerLevel { get; set; }
    public string Preg { get; set; } = "";
    public long BredUntil { get; set; }
    public long BreedWait { get; set; }
}

/// <summary>A scan of another player: their dairy and status, or why it can't be seen.</summary>
public sealed class ScanReply
{
    public bool Found { get; set; }
    public bool Hidden { get; set; }
    public string Why { get; set; } = "";
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public string Dairy { get; set; } = "";
    public string Role { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public bool Online { get; set; }
    public string Version { get; set; } = "";
    public long Now { get; set; }
    public MemberStatus? Status { get; set; }
}

public sealed class StallInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int Tier { get; set; }
    public long CleanUntil { get; set; }   // unix seconds; a farmer cleaned it and it's fresh until then (0 = not fresh)
    public bool Breed { get; set; }        // a breeding stall: it breeds cows instead of milking
    public int Loaded { get; set; }        // breedings a farmer or the owner loaded into it (3 oz of seed each)
}

public sealed class ShelfItem
{
    public string Item { get; set; } = "";
    public string Name { get; set; } = "";
    public int Qty { get; set; }
    public int Price { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
}

public sealed class ShelfReply
{
    public List<ShelfItem> Items { get; set; } = new();
}

public sealed class ShelfBuyReply
{
    public int Price { get; set; }
    public string Baker { get; set; } = "";
}

public sealed class CleanReply
{
    public long Until { get; set; }
}

public sealed class SentInvite
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class OrdersReply
{
    public List<BuyOrder> Orders { get; set; } = new();
}

public sealed class BuyOrder
{
    public long Id { get; set; }
    public string DairyId { get; set; } = "";
    public int Oz { get; set; }
    public int MinGrade { get; set; }
    public int Coins { get; set; }
    public long Expires { get; set; }
    public string DairyName { get; set; } = "";   // board only
    public bool DairyPublic { get; set; }          // board only: can also be delivered in person for a bonus
}

public sealed class InboxReply
{
    public List<InboxItem> Items { get; set; } = new();
}

public sealed class InboxItem
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public int Coins { get; set; }
    public float Oz { get; set; }
    public int Grade { get; set; }
    public string Note { get; set; } = "";
}

public sealed class IdReply
{
    // Dairies and stalls have text ids, orders and invites have number ids
    public System.Text.Json.JsonElement Id { get; set; }

    public string IdText => Id.ValueKind == System.Text.Json.JsonValueKind.Number ? Id.GetRawText() : Id.GetString() ?? "";
}

public sealed class AcceptReply
{
    public string Dairy { get; set; } = "";
    public string Role { get; set; } = "";
    public string? Left { get; set; }
}

public sealed class FillReply
{
    public int Coins { get; set; }
    public int Bonus { get; set; }      // extra for delivering in person
    public bool InPerson { get; set; }
    public int Oz { get; set; }
    public string Dairy { get; set; } = "";
}

public sealed class NetworkFile
{
    public string Api { get; set; } = "";
}

public sealed class ErrorReply
{
    public string Error { get; set; } = "";
}

public sealed class PantryReply
{
    public Dictionary<string, int>? Items { get; set; }
}

// ----- Cloud backup and restore -----

public sealed class BackupPutReply
{
    public bool Ok { get; set; }
    public long Saved { get; set; }
    public bool Skipped { get; set; }
    public bool Kept { get; set; }     // the server kept a backup with more progress (this save was reset)
}

public sealed class BackupReply
{
    public bool Found { get; set; }
    public string Save { get; set; } = "";
    public long Saved { get; set; }
    public string Prev { get; set; } = "";
    public long PrevSaved { get; set; }
    public int Memberships { get; set; }
}

public sealed class RestoreAvailableReply
{
    public bool Available { get; set; }
    public long Saved { get; set; }
    public bool HasSave { get; set; }
}

public sealed class RestoreStartReply
{
    public string Code { get; set; } = "";
    public long Expires { get; set; }
}

public sealed class RestoreCheckReply
{
    public bool Ok { get; set; }
    public string Why { get; set; } = "";
    public string NetId { get; set; } = "";
    public string Save { get; set; } = "";
    public long Saved { get; set; }
    public string Prev { get; set; } = "";
    public long PrevSaved { get; set; }
}

public sealed class OkReply
{
    public bool Ok { get; set; }
}

/// <summary>The dairy's feeding bell: who rang it last and when it can ring again (unix seconds).</summary>
public sealed class BellInfo
{
    public string By { get; set; } = "";
    public long At { get; set; }
    public long ReadyAt { get; set; }
}

public sealed class BreedStallReply
{
    public bool Ok { get; set; }
    public float Oz { get; set; }
    public int Loaded { get; set; }     // breedings left in the stall
    public string Bull { get; set; } = "";
}

public sealed class SeedSale
{
    public long Id { get; set; }
    public string Dairy { get; set; } = "";
    public float Oz { get; set; }
    public float Sold { get; set; }
    public int Price { get; set; }
}

public sealed class SeedMarketReply
{
    public List<SeedSale> Sales { get; set; } = new();
    public List<SeedSale> Mine { get; set; } = new();
    public float Tank { get; set; }
    public int Fund { get; set; }
    public bool IsOwner { get; set; }
}

public sealed class SeedBuyReply
{
    public bool Ok { get; set; }
    public int Oz { get; set; }
    public int Cost { get; set; }
    public float Tank { get; set; }
}

public sealed class StallLoadReply
{
    public bool Ok { get; set; }
    public int Loaded { get; set; }
    public float Tank { get; set; }
    public float Returned { get; set; }
}
