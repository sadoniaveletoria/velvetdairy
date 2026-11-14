using System;
using System.Collections.Generic;
using System.Linq;
using VelvetDairy.Network;

namespace VelvetDairy.Game;

/// <summary>
/// Visiting someone else's public dairy as a guest: you're milked on its stalls and cared for there like at home,
/// your milk goes into its tank, and its farmers can hand milk you (you always get an Allow first).
/// And breeding across dairies: a bull from another dairy only breeds you once you press Allow.
/// </summary>
public sealed partial class DairyGame
{
    /// <summary>In your own dairy (not visiting another as a guest): where farmer and owner work happens.</summary>
    public bool AtHome => Where.InDairy && !Where.Guest;

    /// <summary>The public dairy the server says you're visiting (not your own), with its area, stalls and farmers.</summary>
    public HereInfo? GuestHere => Networked && Net.Last?.Here is { Guest: true, Mine: false } h ? h : null;

    private List<Stall> guestStalls = new();
    private List<Farmhand> guestFarmers = new();
    private HereInfo? guestFrom;

    /// <summary>The visited dairy's stalls (the same objects from one heartbeat to the next).</summary>
    public List<Stall> GuestStalls
    {
        get
        {
            SyncGuest();
            return guestStalls;
        }
    }

    /// <summary>The visited dairy's farmers who are online (they can hand milk you, with your Allow).</summary>
    public List<Farmhand> GuestFarmhands
    {
        get
        {
            SyncGuest();
            return guestFarmers;
        }
    }

    private void SyncGuest()
    {
        var h = GuestHere;
        if (ReferenceEquals(h, guestFrom)) return;
        guestFrom = h;
        if (h == null)
        {
            guestStalls = new List<Stall>();
            guestFarmers = new List<Farmhand>();
            return;
        }

        var stalls = new List<Stall>();
        foreach (var si in h.Stalls)
        {
            var s = guestStalls.FirstOrDefault(x => x.Id == si.Id) ?? new Stall { Id = si.Id };
            s.Name = si.Name;
            s.X = si.X;
            s.Y = si.Y;
            s.Z = si.Z;
            s.Tier = Math.Clamp(si.Tier, 1, 4);
            s.CleanUntil = si.CleanUntil;
            stalls.Add(s);
        }

        guestStalls = stalls;
        var myName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "";
        guestFarmers = h.Farmers.Where(f => !f.Name.Equals(myName, StringComparison.OrdinalIgnoreCase))
            .Select(f => guestFarmers.FirstOrDefault(x => x.Name == f.Name && x.WorldName == f.World)
                         ?? new Farmhand { Name = f.Name, WorldName = f.World, WorldId = LookupWorld(f.World) })
            .ToList();
    }

    /// <summary>Inside the visited dairy's public area (or its house, for a dairy without a box).</summary>
    private bool GuestInside(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter me)
    {
        if (GuestHere is not { } h) return false;
        if (h.Area is { } area) return InArea(area, me);
        var here = Estate.Current();
        return here != null && ulong.TryParse(h.HouseId, out var hid) && Estate.Same(hid, here.Value.HouseId);
    }

    /// <summary>A farmer's view: a member of your dairy, or a guest visiting it, by name.</summary>
    public MemberInfo? HerdMember(string name) =>
        Net.Mine?.Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ??
        Net.Mine?.Guests.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // ----- Breeding with someone from another dairy -----

    private string outsideKey = "";
    private PlayerLookup? outsidePartner;
    private bool outsideAsked, lookPending;
    private double nextOutsideLook;

    /// <summary>A cow: the bull from another dairy you allowed to breed you in this scene (shared with them through the server).</summary>
    public string BreedOkFor { get; private set; } = "";

    /// <summary>A cow: a bull from another dairy is in a scene with you; asks whether they may breed you (a pop-up).</summary>
    public string? BreedAsk { get; private set; }

    /// <summary>Your partner is a bull from another dairy whom you allowed to breed you this scene.</summary>
    private bool OutsideBullAllowed(string name) =>
        BreedOkFor.Length > 0 && BreedOkFor.Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>A bull: your partner is a cow from another dairy who pressed Allow for you.</summary>
    private bool OutsideCowSaidYes
    {
        get
        {
            var me = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "";
            return IsBull && me.Length > 0 && outsidePartner is { Found: true } p && p.Roles.Contains("cow") &&
                   p.BreedOk.Equals(me, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A scene with someone who isn't in your dairy: find out (from the server) whether they're a cow or bull.
    /// A cow asks once whether a bull may breed them; a bull keeps checking for the cow's yes.
    /// </summary>
    private void UpdateOutsidePartner()
    {
        var outside = Networked && InScene && !SceneNow.Solo && SceneNow.PartnerName.Length > 0 &&
                      DairyMember(SceneNow.PartnerName, SceneNow.PartnerWorld) == null;
        var key = outside ? sceneKey : "";
        if (key != outsideKey)
        {
            outsideKey = key;
            outsidePartner = null;
            outsideAsked = false;
            BreedOkFor = "";
            BreedAsk = null;
            nextOutsideLook = 0;
        }

        if (!outside) return;

        // A cow looks once; a bull looks again every few seconds, waiting for the cow's Allow
        var look = outsidePartner == null || (IsBull && outsidePartner.Found && outsidePartner.Roles.Contains("cow") && !OutsideCowSaidYes);
        if (look && !lookPending && now >= nextOutsideLook)
        {
            lookPending = true;
            nextOutsideLook = now + 5;
            var asked = key;
            Net.LookupPlayer(SceneNow.PartnerName, SceneNow.PartnerWorld, r =>
            {
                lookPending = false;
                if (asked == outsideKey) outsidePartner = r;
            });
        }

        if (IsCow && !outsideAsked && BreedOkFor.Length == 0 && outsidePartner is { Found: true } p && p.Roles.Contains("bull"))
        {
            outsideAsked = true;
            BreedAsk = SceneNow.PartnerName;
            Say($"{SceneNow.PartnerName} (a bull{(p.Dairy.Length > 0 ? $" of {p.Dairy}" : "")}) wants to breed you.", 1);
        }
    }

    /// <summary>The cow's answer to a bull from another dairy.</summary>
    public void AnswerBreedAsk(bool allow)
    {
        if (BreedAsk is not { } name) return;
        BreedAsk = null;
        Touch();
        if (allow)
        {
            BreedOkFor = name;
            Say($"You let {name} breed you.", 1);
        }
        else Say($"You told {name} no.", 0);

        if (Networked) Net.Poke();   // so the bull's plugin hears it right away
    }
}
