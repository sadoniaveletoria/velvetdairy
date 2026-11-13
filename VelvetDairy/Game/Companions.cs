using System;
using System.Collections.Generic;

namespace VelvetDairy.Game;

/// <summary>
/// The other plugins Eorzea Milkers works with, and whether each is installed and loaded. None are needed to play;
/// each turns on features. Checked every few seconds (not every frame), for the HUD's dots and their tooltip.
/// </summary>
public sealed class Companions
{
    public sealed record Companion(string Name, string For, bool On);

    private readonly Plugin plugin;
    private double nextCheck = -1;
    private List<Companion> list = new();

    public Companions(Plugin plugin) => this.plugin = plugin;

    public IReadOnlyList<Companion> All(double now)
    {
        if (now < nextCheck) return list;
        nextCheck = now + 5;
        list = new List<Companion>
        {
            new("Penumbra", "animation mods while milking", PenumbraLink.Available),
            new("Corruption", "breeding, arousal and pregnancy", plugin.Corruption.Available),
            new("Cummy", "creampies, so Corruption can get a cow pregnant", CummyCanCreampie()),
            new("Lifestream", "Visit travels straight to a dairy", Lifestream.Installed),
        };
        return list;
    }

    /// <summary>Cummy with plugin API 6 or later: what Corruption needs for conception.</summary>
    private static bool CummyCanCreampie()
    {
        try
        {
            return Plugin.PluginInterface.GetIpcSubscriber<int>("Cummy.ApiVersion").InvokeFunc() >= 6;
        }
        catch
        {
            return false;
        }
    }
}
