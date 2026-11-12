using System;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace VelvetDairy.Game;

/// <summary>A housing address read from the game.</summary>
public readonly record struct Address(ulong HouseId, uint World, uint DistrictTerritory, int Ward, int Plot, int Room, bool Apartment)
{
    /// <summary>Lifestream's residential aetheryte ids for each district.</summary>
    public int DistrictKind => Estate.DistrictKind(DistrictTerritory);
    public string District => Estate.DistrictName(DistrictTerritory);
    public string Describe => Apartment ? $"{District}, Ward {Ward}, apartment {Room}" : Room > 0 ? $"{District}, Ward {Ward}, Plot {Plot}, room {Room}" : $"{District}, Ward {Ward}, Plot {Plot}";
}

/// <summary>Where you are in housing, and whether it's your own estate.</summary>
public static class Estate
{
    private static readonly (uint Territory, int Kind, string Name)[] Districts =
    {
        (339, 8, "Mist"), (340, 2, "The Lavender Beds"), (341, 9, "The Goblet"), (641, 111, "Shirogane"), (979, 70, "Empyreum"),
    };

    public static int DistrictKind(uint territory)
    {
        foreach (var d in Districts) if (d.Territory == territory) return d.Kind;
        return 0;
    }

    public static string DistrictName(uint territory)
    {
        foreach (var d in Districts) if (d.Territory == territory) return d.Name;
        return "Residential district";
    }

    public static uint TerritoryForKind(int kind)
    {
        foreach (var d in Districts) if (d.Kind == kind) return d.Territory;
        return 0;
    }

    private static uint IntendedUse(uint territory) =>
        Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(territory)?.TerritoryIntendedUse.RowId ?? 0;

    /// <summary>
    /// Inn rooms: every player gets their own copy at the same positions and nobody else can come in, so a box there
    /// is told apart by whose it is.
    /// </summary>
    public static bool SoloPlace(uint territory) => IntendedUse(territory) == 2;

    /// <summary>
    /// The Island Sanctuary: everyone's island is the same zone at the same positions, but friends can visit, so
    /// there's no telling whose island you're on. Boxes there may overlap other players' (they're on other islands),
    /// and you count as in a box on an island only for your own dairy.
    /// </summary>
    public static bool Island(uint territory) => IntendedUse(territory) == 49;

    /// <summary>
    /// Which copy of the zone you're in, for dairy boxes: the house's id inside a house or apartment, your own
    /// character's id in an inn room, and "" anywhere else (the open world, and islands).
    /// </summary>
    public static string PlaceKey()
    {
        if (Current() is { } h) return h.HouseId.ToString();
        if (SoloPlace(Plugin.ClientState.TerritoryType) && Plugin.ContentId() is var cid and > 0) return cid.ToString();
        return "";
    }

    /// <summary>The house you're standing in, or null outside housing.</summary>
    public static unsafe Address? Current()
    {
        var hm = HousingManager.Instance();
        if (hm == null || !hm->IsInside()) return null;
        var id = hm->GetCurrentIndoorHouseId();
        if (id.Id == 0) id = hm->GetCurrentHouseId();
        return id.Id == 0 ? null : From(id);
    }

    /// <summary>Turns a stored house id back into an address.</summary>
    public static Address FromId(ulong id)
    {
        HouseId h = id;
        return From(h);
    }

    private static Address From(HouseId id) => new(
        id.Id,
        id.WorldId,
        id.TerritoryTypeId,
        (id.Data2 & 0x3F) + 1,
        id.Unit.PlotIndex + 1,
        (id.Data2 >> 6) & 0x3FF,
        id.Unit.IsApartment);

    /// <summary>Same estate, ignoring which room of a house you're in.</summary>
    public static bool Same(ulong a, ulong b)
    {
        if (a == b) return true;
        HouseId x = a, y = b;
        return x.WorldId == y.WorldId && x.TerritoryTypeId == y.TerritoryTypeId && (x.Data2 & 0x3F) == (y.Data2 & 0x3F)
               && x.Unit.Value == y.Unit.Value && !x.Unit.IsApartment;
    }

    /// <summary>
    /// True when the house you're standing in is yours: your private house, your Free Company's house,
    /// your apartment, your private chambers, or a shared estate you're part of.
    /// </summary>
    public static unsafe bool IsOwnEstate(Address here)
    {
        try
        {
            foreach (var type in new[] { EstateType.PersonalEstate, EstateType.FreeCompanyEstate, EstateType.ApartmentRoom, EstateType.PersonalChambers })
            {
                var owned = HousingManager.GetOwnedHouseId(type);
                if (owned.Id != 0 && (owned.Id == here.HouseId || (type != EstateType.PersonalChambers && Same(owned.Id, here.HouseId)))) return true;
            }

            for (var i = 0; i < 2; i++)
            {
                var shared = HousingManager.GetOwnedHouseId(EstateType.SharedEstate, i);
                if (shared.Id != 0 && Same(shared.Id, here.HouseId)) return true;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Eorzea Milkers couldn't read which houses you own");
        }

        return false;
    }
}
