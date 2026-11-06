using System;
using Dalamud.Plugin.Ipc;

namespace VelvetDairy.Game;

/// <summary>
/// Travel to a housing address through the Lifestream plugin, if it's installed.
/// Lifestream's address-book entry: (Name, World, City, Ward, PropertyType, Plot, Apartment, ApartmentSubdivision, AliasEnabled, Alias),
/// where City is the residential aetheryte id and Ward/Plot are the numbers players use (from 1).
/// </summary>
public static class Lifestream
{
    private static ICallGateSubscriber<(string, int, int, int, int, int, int, bool, bool, string), object>? goTo;
    private static ICallGateSubscriber<bool>? isBusy;

    public static bool Installed
    {
        get
        {
            foreach (var p in Plugin.PluginInterface.InstalledPlugins)
                if (p.InternalName == "Lifestream" && p.IsLoaded) return true;
            return false;
        }
    }

    public static bool Busy
    {
        get
        {
            try
            {
                isBusy ??= Plugin.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
                return isBusy.InvokeFunc();
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Starts travel to a house or apartment. Returns null on success, or why it couldn't.</summary>
    public static string? GoTo(string name, uint world, int districtKind, int ward, int plot, int room, bool apartment)
    {
        if (!Installed) return "Install the Lifestream plugin to travel there automatically.";
        if (districtKind == 0) return "Lifestream doesn't know that district.";
        try
        {
            goTo ??= Plugin.PluginInterface.GetIpcSubscriber<(string, int, int, int, int, int, int, bool, bool, string), object>("Lifestream.GoToHousingAddress");
            // PropertyType: 0 = house, 1 = apartment (Lifestream's address book)
            goTo.InvokeAction((name, (int)world, districtKind, ward, apartment ? 1 : 0, plot, apartment ? room : 0, false, false, ""));
            return null;
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Velvet Dairy couldn't ask Lifestream to travel");
            return "Lifestream couldn't start the trip (it may need an update).";
        }
    }
}
