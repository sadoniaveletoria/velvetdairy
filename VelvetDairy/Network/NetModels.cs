using System;
using System.Collections.Generic;

namespace VelvetDairy.Network;

// Shapes of what the Velvet Dairy Network server sends back (see server/src/index.js). JSON is camelCase.

public sealed class HeartbeatReply
{
    public int Online { get; set; }
    public HereInfo? Here { get; set; }
    public MembershipInfo? Membership { get; set; }
    public List<InviteInfo> Invites { get; set; } = new();
    public int Inbox { get; set; }
    public int DairyXp { get; set; }
    public CreameryInfo? Creamery { get; set; }
    public float Carried { get; set; }
}

/// <summary>Where the Grand Creamery is (set by the developer): weekly contracts are delivered there.</summary>
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
}

public sealed class HereInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPublic { get; set; }
    public bool Mine { get; set; }
    public bool Private { get; set; }   // you're in your dairy's private place
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
    public int CowsHere { get; set; }
    public int OpenOrders { get; set; }
    public int Level { get; set; } = 1;
    public int WeekXp { get; set; }
    public VelvetDairy.Game.DairyArea? Area { get; set; }

    public string Address => Area != null ? Area.Zone : Ward is null ? "Private" : Room is > 0 && Plot is null ? $"{District}, Ward {Ward}, apartment {Room}" : $"{District}, Ward {Ward}, Plot {Plot}";
}

public sealed class MyDairyReply
{
    public MyDairyInfo? Dairy { get; set; }
    public YouInfo? You { get; set; }
    public List<MemberInfo> Members { get; set; } = new();
    public List<StallInfo> Stalls { get; set; } = new();
    public List<SentInvite> Invites { get; set; } = new();
    public DairyProgress? Progress { get; set; }
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
    public float PayBonus { get; set; } = 1f;
    public ContractInfo? Contract { get; set; }

    public float LevelProgress => NextXp is { } n && n > LevelXp ? Math.Clamp((Xp - LevelXp) / (float)(n - LevelXp), 0, 1) : 1;
    public int TierPrice(int tier) => tier >= 0 && tier < TierPrices.Count ? TierPrices[tier] : 0;
}

/// <summary>The dairy's milk tank: milk members pumped at its stalls, by grade.</summary>
public sealed class DairyTank
{
    public List<float> ByGrade { get; set; } = new();
    public float Total { get; set; }
    public List<Pumper> Pumpers { get; set; } = new();
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
    public bool Completed { get; set; }
    public List<Pumper> Shippers { get; set; } = new();

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
    public int Quality { get; set; }        // milk quality score (cows)
    public int TotalOz { get; set; }        // all the milk they've ever given
    public List<string> Emotes { get; set; } = new();   // the emotes this cow's farmers milk her with
    public string HandBy { get; set; } = "";            // the farmer milking her right now, if any
    public bool AnyEmote { get; set; } = true;          // any looping emote milks them (not only their list)
    public string Milking { get; set; } = "";           // a farmer: the cow they pressed Milk on right now
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
