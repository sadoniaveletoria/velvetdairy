using FFXIVClientStructs.FFXIV.Client.Game;

namespace VelvetDairy.Game;

/// <summary>Where you are in housing. Ward and plot are 0-based; -1 = not inside a house.</summary>
public static class Housing
{
    public static unsafe (int Ward, int Plot) Current()
    {
        var hm = HousingManager.Instance();
        if (hm == null || !hm->IsInside()) return (-1, -1);
        return (hm->GetCurrentWard(), hm->GetCurrentPlot());
    }

    /// <summary>The ward you're in, 1-based, when you're outdoors in a residential district (0 elsewhere or indoors).</summary>
    public static unsafe int OutdoorWard()
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->IsInside() || hm->OutdoorTerritory == null) return 0;
        var w = hm->GetCurrentWard();
        return w >= 0 ? w + 1 : 0;
    }

    /// <summary>The plot whose yard you're standing in, 1-based, outdoors in a residential district (0 if you're on the street).</summary>
    public static unsafe int OutdoorPlot()
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->IsInside() || hm->OutdoorTerritory == null) return 0;
        var p = (int)hm->GetCurrentPlot();
        return p is >= 0 and < 60 ? p + 1 : 0;
    }

    public static string Describe(int ward, int plot)
    {
        if (ward < 0) return "";
        if (plot < 0) return $"Ward {ward + 1}, apartment";
        return $"Ward {ward + 1}, Plot {plot + 1}";
    }
}
