using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lumina.Excel.Sheets;

namespace VelvetDairy.Game;

/// <summary>
/// Works out which emote an animation mod (or one of its options) changes, by reading the mod's own files in Penumbra's
/// mod folder: each option lists the game animation files it replaces (".../bt_common/emote/b_dance.pap"), and the
/// game's emote data says which emote plays that animation. So picking a mod option is enough: the Milk button does
/// the emote it animates.
/// </summary>
public static class ModAnimations
{
    public sealed record Pick(string Command, string Name, int Pose);   // Pose: a sit pose's number (/cpose), -1 for none

    private static Dictionary<string, (string Command, string Name)>? byKey;

    /// <summary>Animation key ("emote/b_dance") -> the emote that plays it.</summary>
    private static Dictionary<string, (string Command, string Name)> Keys()
    {
        if (byKey != null) return byKey;
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Plugin.DataManager.GetExcelSheet<Emote>())
        {
            var cmd = e.TextCommand.ValueNullable?.Command.ExtractText().Trim() ?? "";
            var name = e.Name.ExtractText().Trim();
            if (e.RowId == 0 || cmd.Length < 2 || name.Length == 0) continue;
            foreach (var t in e.ActionTimeline)
            {
                var key = t.ValueNullable?.Key.ExtractText().Trim() ?? "";
                if (key.Length > 0) map.TryAdd(Base(key), (cmd, name));
            }
        }

        return byKey = map;
    }

    /// <summary>"emote/b_dance_loop" and "emote/b_dance_start" are the same emote as "emote/b_dance".</summary>
    private static string Base(string key)
    {
        key = key.ToLowerInvariant();
        foreach (var end in new[] { "_loop", "_start", "_st", "_end", "_ed", "_lp" })
            if (key.EndsWith(end)) return key[..^end.Length];
        return key;
    }

    // Sitting: s_pose (on a chair, /sit), j_pose (on the ground, /groundsit), l_pose (lying down, /doze); the number is the /cpose pose
    private static readonly Regex PoseFile = new(@"(?:^|/)(s|j|l)_pose(\d+)", RegexOptions.IgnoreCase);

    /// <summary>The emote one animation file belongs to, if it's an emote at all.</summary>
    public static Pick? ForFile(string gamePath)
    {
        var p = gamePath.Replace('\\', '/').ToLowerInvariant();
        if (!p.EndsWith(".pap")) return null;
        var i = p.IndexOf("/bt_common/", StringComparison.Ordinal);
        var key = (i >= 0 ? p[(i + "/bt_common/".Length)..] : p[(p.LastIndexOf('/') + 1)..])[..^4];

        var pose = PoseFile.Match(key);
        if (pose.Success)
        {
            var n = int.Parse(pose.Groups[2].Value);
            return pose.Groups[1].Value switch
            {
                "s" => new Pick("/sit", "Sit", n),
                "j" => new Pick("/groundsit", "Sit on ground", n),
                _ => new Pick("/doze", "Doze", n),
            };
        }

        return Keys().TryGetValue(Base(key), out var e) ? new Pick(e.Command, e.Name, -1) : null;
    }

    private static readonly Dictionary<string, (DateTime Read, DateTime Checked, Dictionary<string, List<string>> Files)> cache = new();

    /// <summary>
    /// The game files each option of a mod replaces: "" for the mod's always-on files, "group\u0001option" for each option.
    /// Read from the mod's folder (cached until the folder changes). Empty when it can't be read.
    /// </summary>
    private static Dictionary<string, List<string>> Files(string modDir)
    {
        // The picker asks every frame: look at the folder again at most every few seconds
        if (cache.TryGetValue(modDir, out var recent) && DateTime.UtcNow - recent.Checked < TimeSpan.FromSeconds(5)) return recent.Files;
        var root = PenumbraLink.ModRoot();
        if (string.IsNullOrEmpty(root)) return new();
        var dir = Path.Combine(root, modDir);
        if (!Directory.Exists(dir)) return new();
        var stamp = Directory.GetLastWriteTimeUtc(dir);
        foreach (var f in Directory.EnumerateFiles(dir, "*.json")) { var t = File.GetLastWriteTimeUtc(f); if (t > stamp) stamp = t; }
        if (cache.TryGetValue(modDir, out var c) && c.Read == stamp)
        {
            cache[modDir] = (c.Read, DateTime.UtcNow, c.Files);
            return c.Files;
        }

        var files = new Dictionary<string, List<string>>();
        try
        {
            var def = Path.Combine(dir, "default_mod.json");
            if (File.Exists(def))
                using (var doc = JsonDocument.Parse(File.ReadAllText(def)))
                    files[""] = Paths(doc.RootElement);

            foreach (var g in Directory.EnumerateFiles(dir, "group_*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(g));
                var group = doc.RootElement.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                if (!doc.RootElement.TryGetProperty("Options", out var opts) || opts.ValueKind != JsonValueKind.Array) continue;
                foreach (var o in opts.EnumerateArray())
                {
                    var name = o.TryGetProperty("Name", out var on) ? on.GetString() ?? "" : "";
                    files[group + "\u0001" + name] = Paths(o);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, $"Eorzea Milkers couldn't read the mod {modDir}");
        }

        cache[modDir] = (stamp, DateTime.UtcNow, files);
        return files;
    }

    /// <summary>Game paths an option replaces: its Files and its FileSwaps.</summary>
    private static List<string> Paths(JsonElement option)
    {
        var list = new List<string>();
        foreach (var prop in new[] { "Files", "FileSwaps" })
            if (option.TryGetProperty(prop, out var d) && d.ValueKind == JsonValueKind.Object)
                foreach (var kv in d.EnumerateObject()) list.Add(kv.Name);
        return list;
    }

    // Animation mods name the emote each option replaces: "(Dom-Bdance/Sub-Hdance)" for a pair (the farmer is the Dom),
    // "(Gsit0, 1)" or "[Bdance]" for one. The names are the game's emote commands (/bdance is Ball Dance) or a sit pose.
    private static readonly Regex DomSlot = new(@"\bDom[\s:_-]+([A-Za-z]+)(\d*)", RegexOptions.IgnoreCase);
    private static readonly Regex AnySlot = new(@"[\(\[]\s*([A-Za-z]+)(\d*)\s*[,\)\]/]", RegexOptions.IgnoreCase);

    /// <summary>The emote an option's name says it replaces (for a Dom/Sub pair, the Dom one: that's you, the farmer).</summary>
    public static Pick? FromName(string option)
    {
        var m = DomSlot.Match(option);
        if (!m.Success) m = AnySlot.Match(option);
        while (m.Success)
        {
            if (Slot(m.Groups[1].Value, m.Groups[2].Value) is { } p) return p;
            m = m.NextMatch();
        }

        return null;
    }

    private static Pick? Slot(string word, string digits)
    {
        var n = digits.Length > 0 && int.TryParse(digits, out var d) ? d : 0;
        switch (word.ToLowerInvariant())
        {
            case "gsit" or "groundsit" or "gpose" or "floor": return new Pick("/groundsit", "Sit on ground", n);
            case "chair" or "sit" or "spose": return new Pick("/sit", "Sit", n);
            case "bed" or "doze" or "lay" or "lie" or "sleep": return new Pick("/doze", "Doze", n);
        }

        foreach (var cmd in new[] { "/" + word + digits, "/" + word })
        {
            var id = Emotes.Find(cmd);
            if (id != 0)
            {
                var main = Emotes.Command(id);
                return new Pick(main.Length > 0 ? main : cmd, Emotes.Name(id), -1);
            }
        }

        return null;
    }

    /// <summary>What an option animates: its name first (mods name the emote), then the files it replaces.</summary>
    private static Pick? Of(string modDir, string group, string option) =>
        FromName(option) ?? (Files(modDir).TryGetValue(group + "\u0001" + option, out var f) ? First(f) : null);

    private static Pick? First(IEnumerable<string> paths) => paths.Select(ForFile).FirstOrDefault(p => p != null);

    /// <summary>The emote one option of a mod animates (null if it doesn't change an emote).</summary>
    public static Pick? ForOption(string modDir, string group, string option) => Of(modDir, group, option);

    /// <summary>
    /// The emote the mod animates with the options picked for milking: those options first, then the mod's
    /// always-on files, then (if nothing was picked) any option that changes exactly one emote.
    /// </summary>
    public static Pick? ForMilking(string modDir, IReadOnlyDictionary<string, List<string>> picked)
    {
        if (modDir.Length == 0) return null;
        var files = Files(modDir);
        foreach (var (group, options) in picked)
            foreach (var o in options)
                if (Of(modDir, group, o) is { } p) return p;

        if (files.TryGetValue("", out var always) && First(always) is { } d) return d;
        var all = files.Where(kv => kv.Key.Length > 0).Select(kv => First(kv.Value)).Where(p => p != null).Select(p => p!).ToList();
        return all.Select(p => p.Command).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? all[0] : null;
    }
}
