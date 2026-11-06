using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VelvetDairy.Game;

/// <summary>
/// Catches saves edited outside the game (for example, typing in more coins).
/// Every save is signed with an HMAC of the progress that matters; a backup of the last good save is kept next to it.
/// This stops file edits. It can't stop someone who changes the plugin itself; only a server can do that.
/// </summary>
public static partial class Integrity
{
    public enum Result { Fine, NewSave, Restored, Reset }

    /// <summary>
    /// Public key used by builds made before the real VELVET_INTEGRITY_KEY secret exists (early testing).
    /// Saves signed with it are accepted once and re-signed with the real key. Remove this once testers have updated.
    /// </summary>
    public const string TestingKeyHex = "77363D5FA05AB2AB759D4BBFE517696A4380D031387CAC5DC80787CB2553B00B";

    /// <summary>True when this build has no real key (CI fell back to the testing key).</summary>
    public static bool UsingTestingKey => KeyHex == TestingKeyHex;

    private static string BackupPath => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "save-backup.json");

    /// <summary>A stable text version of everything worth cheating on. Numbers are rounded so tiny float differences never matter.</summary>
    private static string Canonical(FarmSave f)
    {
        var c = CultureInfo.InvariantCulture;
        string N(float v) => Math.Round(v, 2).ToString("F2", c);
        // Raw ticks (no time zone conversion), so a daylight-saving change can't make a good save look edited
        string D(DateTime? d) => d.HasValue ? (d.Value.Ticks / TimeSpan.TicksPerSecond).ToString(c) : "-";
        var sb = new StringBuilder();
        void Add(string k, object v) => sb.Append(k).Append('=').Append(Convert.ToString(v, c)).Append(';');

        Add("coins", f.Coins);
        Add("xp", f.Xp);
        Add("quality", N(f.Quality));
        var cow = f.Cow;
        Add("milk", N(cow.Milk));
        Add("taint", N(cow.Taint));
        Add("spoiled", cow.Spoiled);
        Add("full", D(cow.FullSinceUtc));
        Add("treat", D(cow.QualityTreatReadyUtc));
        Add("boost", D(cow.BoostUntilUtc));
        Add("needs", $"{N(cow.Hunger)},{N(cow.Thirst)},{N(cow.Comfort)},{N(cow.Mood)},{N(cow.Richness)}");
        Add("tank", string.Join(",", (f.Tank ?? Array.Empty<float>()).Select(N)));
        Add("pantry", string.Join(",", f.Pantry.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value}")));
        Add("upgrades", string.Join(",", f.Upgrades.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value}")));
        Add("stalls", string.Join(",", (f.Stalls ?? new()).Select(s => $"{s.Id}:{s.Tier}:{s.Sessions}").OrderBy(x => x, StringComparer.Ordinal)));
        Add("awards", string.Join(",", f.Awards.OrderBy(a => a, StringComparer.Ordinal)));
        Add("orders", string.Join(",", (f.Orders ?? new()).Select(o => $"{o.Id}:{o.Oz}:{o.MinGrade}:{o.Coins}:{o.Xp}")));
        Add("serial", f.OrderSerial);
        Add("day", $"{f.DayKey},{N(f.DayOz)},{f.DayOrders},{f.DayCare},{f.DayHand},{f.DayClaimed}");
        var st = f.Stats;
        Add("stats", $"{N(st.TotalOz)},{st.StallSessions},{st.HandSessions},{st.OrdersFilled},{st.BottlesSold},{st.CoinsEarned},{st.CareTasks}," +
                     $"{st.BestGrade},{N(st.BestSessionOz)},{st.DailiesDone},{st.TimesDumped},{N(st.OzDumped)}");
        Add("starter", f.StarterGiven);
        return sb.ToString();
    }

    public static string Sign(FarmSave f) => Sign(f, KeyHex);

    private static string Sign(FarmSave f, string keyHex)
    {
        using var h = new HMACSHA256(Convert.FromHexString(keyHex));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(Canonical(f))));
    }

    private static bool Valid(FarmSave f, string? keyHex = null) =>
        !string.IsNullOrEmpty(f.Seal) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(f.Seal), Encoding.ASCII.GetBytes(Sign(f, keyHex ?? KeyHex)));

    /// <summary>Signs the save and keeps a copy of it as the last good backup.</summary>
    public static void Seal(FarmSave f)
    {
        f.Seal = Sign(f);
        try
        {
            var tmp = BackupPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(f));
            File.Move(tmp, BackupPath, true);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Velvet Dairy couldn't write the save backup");
        }
    }

    /// <summary>
    /// Checks the loaded save. Edited saves are replaced by the last good backup, or reset if there isn't one.
    /// Returns what happened; config.Farm may be replaced.
    /// </summary>
    public static Result Check(Configuration config)
    {
        var f = config.Farm;
        // Saves from before signing existed (and brand new saves) have no seal yet: accept them once
        if (string.IsNullOrEmpty(f.Seal)) return Result.NewSave;
        if (Valid(f)) return Result.Fine;
        // Signed by an early testing build: accept it and re-sign with this build's key
        if (!UsingTestingKey && Valid(f, TestingKeyHex)) return Result.NewSave;

        Plugin.Log.Warning("Velvet Dairy: the save doesn't match its signature (edited outside the game).");
        try
        {
            if (File.Exists(BackupPath))
            {
                var backup = JsonSerializer.Deserialize<FarmSave>(File.ReadAllText(BackupPath));
                if (backup != null && Valid(backup))
                {
                    backup.EditsCaught = f.EditsCaught + 1;
                    config.Farm = backup;
                    return Result.Restored;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Velvet Dairy couldn't read the save backup");
        }

        // No good backup: start progress over, but keep the dairy, stalls, farmhands and preferences
        var fresh = new FarmSave
        {
            Confirmed18 = f.Confirmed18, ShowHud = f.ShowHud, LockHud = f.LockHud, ChatEcho = f.ChatEcho,
            SoundOn = f.SoundOn, SoundVolume = f.SoundVolume, DevKey = f.DevKey,
            Dairy = f.Dairy, DairyRadius = f.DairyRadius, HandEmotes = f.HandEmotes,
            Stalls = f.Stalls?.Select(s => new Stall { Id = s.Id, Name = s.Name, X = s.X, Y = s.Y, Z = s.Z }).ToList(),
            Farmhands = f.Farmhands?.Select(h => new Farmhand { Name = h.Name, WorldId = h.WorldId, WorldName = h.WorldName, IsTest = h.IsTest }).ToList(),
            HiddenFeed = f.HiddenFeed,
            EditsCaught = f.EditsCaught + 1,
        };
        config.Farm = fresh;
        return Result.Reset;
    }
}
