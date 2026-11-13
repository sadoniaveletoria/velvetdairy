using System.Globalization;

namespace VelvetDairy.Game;

/// <summary>
/// How milk amounts are shown: US fluid ounces (the default) or metric (ml, and litres from 1,000 ml), per the
/// Settings choice. The game itself always counts ounces; this is only how they're written.
/// </summary>
public static class Units
{
    /// <summary>Show milk in ml and litres. Set from the character's settings every frame.</summary>
    public static bool Metric;

    private const double MlPerOz = 29.5735;

    /// <summary>An amount with its unit: "12.5 oz", or "370 ml" / "1.48 L".</summary>
    public static string Oz(double oz, string format = "0.#")
    {
        if (!Metric) return $"{oz.ToString(format, CultureInfo.CurrentCulture)} oz";
        var ml = oz * MlPerOz;
        return System.Math.Abs(ml) >= 1000 ? $"{(ml / 1000).ToString("0.##", CultureInfo.CurrentCulture)} L" : $"{ml.ToString(ml < 10 ? "0.#" : "N0", CultureInfo.CurrentCulture)} ml";
    }

    /// <summary>Just the number, for "5.8 / 96 oz" style pairs that share one unit (always ml in metric, never litres).</summary>
    public static string Num(double oz, string format = "0.#")
    {
        if (!Metric) return oz.ToString(format, CultureInfo.CurrentCulture);
        var ml = oz * MlPerOz;
        return ml.ToString(ml < 10 ? "0.#" : "N0", CultureInfo.CurrentCulture);
    }

    /// <summary>The unit that goes with <see cref="Num"/>.</summary>
    public static string Unit => Metric ? "ml" : "oz";

    /// <summary>A price per amount of milk: "12 coins per oz", or "41 coins per 100 ml".</summary>
    public static string PerOz(double coinsPerOz) => Metric
        ? $"{(coinsPerOz * 100 / MlPerOz).ToString("0.#", CultureInfo.CurrentCulture)} coins per 100 ml"
        : $"{coinsPerOz.ToString("0.#", CultureInfo.CurrentCulture)} coins per oz";
}
