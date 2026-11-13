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

    /// <summary>
    /// Just the number of an amount, in the same unit <see cref="Oz"/> uses for <paramref name="like"/>, for a big
    /// "82.1 / 2.84 L" readout: litres when the other amount is shown in litres, ml otherwise, oz with metric off.
    /// </summary>
    public static string NumLike(double oz, double like, string format = "0.#")
    {
        if (!Metric) return oz.ToString(format, CultureInfo.CurrentCulture);
        var ml = oz * MlPerOz;
        if (System.Math.Abs(like * MlPerOz) >= 1000) return (ml / 1000).ToString("0.00", CultureInfo.CurrentCulture);
        return ml.ToString(ml < 10 ? "0.#" : "N0", CultureInfo.CurrentCulture);
    }

    /// <summary>The unit that goes with <see cref="Num"/>.</summary>
    public static string Unit => Metric ? "ml" : "oz";

    private static readonly System.Text.RegularExpressions.Regex Pair = new(@"(\d[\d,]*(?:\.\d+)?) ?/ ?(\d[\d,]*(?:\.\d+)?) ?oz\b");
    private static readonly System.Text.RegularExpressions.Regex PerOzRx = new(@"(\d[\d,]*(?:\.\d+)?) (coins?) per (?:oz|ounce)\b");
    private static readonly System.Text.RegularExpressions.Regex Amount = new(@"(\d[\d,]*(?:\.\d+)?) ?(?:oz|fl oz|ounces?)\b");
    private static readonly System.Text.RegularExpressions.Regex Bare = new(@"\b(?:per oz|per ounce|an ounce|ounces|oz)\b");

    private static double Parse(string n) => double.Parse(n.Replace(",", ""), CultureInfo.InvariantCulture);

    /// <summary>
    /// Any text that mentions ounces (item descriptions, the guide, What's new, server messages), written in metric
    /// when the setting is on. Text without ounces, or with metric off, comes back as it is.
    /// </summary>
    public static string Text(string text)
    {
        if (!Metric || text.IndexOf("oz", System.StringComparison.Ordinal) < 0 && text.IndexOf("ounce", System.StringComparison.Ordinal) < 0) return text;
        text = Pair.Replace(text, m => $"{Num(Parse(m.Groups[1].Value))} / {Num(Parse(m.Groups[2].Value))} ml");
        text = PerOzRx.Replace(text, m => PerOz(Parse(m.Groups[1].Value)).Replace("coins per", m.Groups[2].Value + " per"));
        text = Amount.Replace(text, m => Oz(Parse(m.Groups[1].Value)));
        return Bare.Replace(text, m => m.Value switch
        {
            "per oz" or "per ounce" => "per 100 ml",
            "an ounce" => "a little milk",
            "ounces" => "millilitres",
            _ => "ml",
        });
    }

    /// <summary>A price per amount of milk: "12 coins per oz", or "41 coins per 100 ml".</summary>
    public static string PerOz(double coinsPerOz) => Metric
        ? $"{(coinsPerOz * 100 / MlPerOz).ToString("0.#", CultureInfo.CurrentCulture)} coins per 100 ml"
        : $"{coinsPerOz.ToString("0.#", CultureInfo.CurrentCulture)} coins per oz";
}
