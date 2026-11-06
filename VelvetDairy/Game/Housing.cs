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

    public static string Describe(int ward, int plot)
    {
        if (ward < 0) return "";
        if (plot < 0) return $"Ward {ward + 1}, apartment";
        return $"Ward {ward + 1}, Plot {plot + 1}";
    }
}
