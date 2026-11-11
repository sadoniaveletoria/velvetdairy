using System;
using System.Collections.Generic;
using System.Linq;

namespace VelvetDairy.Game;

/// <summary>
/// Talks to Penumbra (if installed) to switch an animation mod on for your character only while you're milking.
/// Uses Penumbra's temporary mod settings: nothing in your saved collections changes, and they're gone when Penumbra restarts.
/// </summary>
public static class PenumbraLink
{
    // Identifies our temporary settings (a negative key doesn't lock you out of editing them in Penumbra)
    private const int Key = -70220;
    private const string Source = "Eorzea Milkers (hand milking)";

    private static T? Try<T>(Func<T> f, T? fallback = default)
    {
        try { return f(); }
        catch { return fallback; }
    }

    public static bool Available => Try(() =>
        Plugin.PluginInterface.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion.V5").InvokeFunc().Item1 >= 5);

    /// <summary>Installed mods: folder name -> display name. Null when Penumbra isn't there.</summary>
    public static Dictionary<string, string>? Mods() => Try(() =>
        Plugin.PluginInterface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList").InvokeFunc());

    /// <summary>A mod's option groups: group name -> (its options, whether only one can be on at a time). Null if not found.</summary>
    public static List<(string Group, string[] Options, bool Single)>? Groups(string modDir) => Try(() =>
        Plugin.PluginInterface.GetIpcSubscriber<string, string, IReadOnlyDictionary<string, (string[], int)>?>("Penumbra.GetAvailableModSettings.V5")
            .InvokeFunc(modDir, "")?.Select(kv => (kv.Key, kv.Value.Item1, kv.Value.Item2 == 0)).ToList());

    /// <summary>
    /// Turns the mod on for your character. Groups you picked options for use those; the rest keep what you set in Penumbra.
    /// Null on success, or why not.
    /// </summary>
    public static string? Enable(string modDir, IReadOnlyDictionary<string, List<string>>? picked = null)
    {
        try
        {
            var options = new Dictionary<string, IReadOnlyList<string>>();
            var coll = Plugin.PluginInterface
                .GetIpcSubscriber<int, (bool, bool, (Guid, string))>("Penumbra.GetCollectionForObject.V5").InvokeFunc(0);
            if (coll.Item1)
            {
                var cur = Plugin.PluginInterface
                    .GetIpcSubscriber<Guid, string, string, bool, (int, (bool, int, Dictionary<string, List<string>>, bool)?)>("Penumbra.GetCurrentModSettings.V5")
                    .InvokeFunc(coll.Item3.Item1, modDir, "", false);
                if (cur.Item2 is { } s)
                    foreach (var (group, list) in s.Item3) options[group] = list;
            }

            if (picked != null)
                foreach (var (group, list) in picked) options[group] = list;

            var ec = Plugin.PluginInterface
                .GetIpcSubscriber<int, string, string, (bool, bool, int, IReadOnlyDictionary<string, IReadOnlyList<string>>), string, int, int>("Penumbra.SetTemporaryModSettingsPlayer.V5")
                .InvokeFunc(0, modDir, "", (false, true, 9999, options), Source, Key);
            return ec switch
            {
                0 or 1 => null,
                5 or 6 => "Penumbra can't find that mod anymore. Pick it again in Hand milk > Animation.",
                _ => $"Penumbra couldn't switch the mod on (code {ec}).",
            };
        }
        catch
        {
            return "Penumbra isn't running.";
        }
    }

    /// <summary>Takes our temporary setting off again (back to however you have it in Penumbra).</summary>
    public static void Disable(string modDir)
    {
        try
        {
            Plugin.PluginInterface.GetIpcSubscriber<int, string, string, int, int>("Penumbra.RemoveTemporaryModSettingsPlayer.V5")
                .InvokeFunc(0, modDir, "", Key);
        }
        catch
        {
            // Penumbra went away; its temporary settings went with it
        }
    }

    /// <summary>Redraws your character so new animation files load (a quick blink).</summary>
    public static void RedrawMe()
    {
        try { Plugin.PluginInterface.GetIpcSubscriber<int, int, object?>("Penumbra.RedrawObject.V5").InvokeAction(0, 0); }
        catch { }
    }

    public static string NameOf(string modDir) => Mods()?.GetValueOrDefault(modDir) ?? modDir;

    public static List<(string Dir, string Name)> Sorted() =>
        (Mods() ?? new()).Select(kv => (kv.Key, kv.Value.Length > 0 ? kv.Value : kv.Key)).OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).ToList();
}
