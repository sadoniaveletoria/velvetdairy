using System;
using System.Linq;
using VelvetDairy.Network;

namespace VelvetDairy.Game;

/// <summary>
/// Bulls and breeding, through the Corruption plugin (it does the scenes, climaxes and pregnancy; Eorzea Milkers only
/// reads them). A bull of your own dairy finishing in a scene with you "breeds" you: a small milk bonus for a while.
/// A pregnancy from Corruption shows here and adds milk by trimester, until the birth.
/// </summary>
public sealed partial class DairyGame
{
    /// <summary>Being bred: +5% milk for 30 minutes, at most once every 20 minutes.</summary>
    public const float BredBonus = 0.05f;
    public const int BredMinutes = 30, BredCooldownMinutes = 20;

    /// <summary>Bulls breed as often as they like, but after this many a day their seed output drops.</summary>
    public const int FreshBreeds = 20;

    /// <summary>Bull xp for one breeding at full seed output.</summary>
    public const int BreedXp = 40;

    public CorruptionLink.Pregnancy Preg { get; private set; } = CorruptionLink.NoPregnancy;
    private double nextPregRead;
    private double nextCorruptionCheck;
    private bool corruptionOn;

    /// <summary>Corruption is installed (checked every few seconds).</summary>
    public bool CorruptionOn
    {
        get
        {
            if (now >= nextCorruptionCheck)
            {
                nextCorruptionCheck = now + 5;
                corruptionOn = plugin.Corruption.Available;
            }

            return corruptionOn;
        }
    }

    public bool IsBull => Has("bull");

    // ----- Cows: bred and pregnant -----

    public TimeSpan BredLeft => F.BredUntilUtc > DateTime.UtcNow ? F.BredUntilUtc - DateTime.UtcNow : TimeSpan.Zero;

    /// <summary>The milk bonus from being bred right now (scaled by the bull's seed output).</summary>
    public float BredBonusNow => BredLeft > TimeSpan.Zero ? BredBonus * Math.Clamp(F.BredStrength, 0.2f, 1f) : 0f;

    /// <summary>When another breeding will count again.</summary>
    public TimeSpan BreedCooldownLeft
    {
        get
        {
            var at = F.BredAtUtc.AddMinutes(BredCooldownMinutes);
            return at > DateTime.UtcNow ? at - DateTime.UtcNow : TimeSpan.Zero;
        }
    }

    public bool Pregnant => Preg.Stage is "first" or "second" or "third";

    /// <summary>+5% / +10% / +15% milk in the first, second and third trimester. Nothing after the birth.</summary>
    public float PregnancyBonus => !IsCow ? 0f : Preg.Stage switch { "first" => 0.05f, "second" => 0.10f, "third" => 0.15f, _ => 0f };

    public static string StageName(string stage) => stage switch
    {
        "first" => "first trimester", "second" => "second trimester", "third" => "third trimester", "recovering" => "recovering after the birth", _ => "not pregnant",
    };

    /// <summary>How far along: 0 to 1 over Corruption's 14 days.</summary>
    public float PregnancyProgress
    {
        get
        {
            if (!Pregnant || Preg.DueUtc == default || Preg.ConceivedUtc == default) return 0f;
            var total = (Preg.DueUtc - Preg.ConceivedUtc).TotalSeconds;
            return total <= 0 ? 0f : (float)Math.Clamp((DateTime.UtcNow - Preg.ConceivedUtc).TotalSeconds / total, 0, 1);
        }
    }

    /// <summary>Corruption said something changed: read the pregnancy on the next frame.</summary>
    public void ReadPregnancySoon() => nextPregRead = 0;

    // ----- A Corruption scene going on right now (read once a second) -----

    public CorruptionLink.Scene SceneNow { get; private set; } = CorruptionLink.NoScene;
    public CorruptionLink.Arousal ArousalNow { get; private set; } = CorruptionLink.NoArousal;
    private double nextSceneRead;
    private string sceneKey = "";
    private bool bredThisScene;   // one breeding per scene: climaxing again in the same scene doesn't count again

    /// <summary>In a Corruption scene (solo or with a partner) right now.</summary>
    public bool InScene => SceneNow.Ready && SceneNow.State != "idle";

    /// <summary>Your scene partner is in your dairy and the roles fit, so finishing counts as a breeding.</summary>
    public bool SceneBreeds
    {
        get
        {
            if (!InScene || SceneNow.Solo || SceneNow.PartnerName.Length == 0) return false;
            var p = DairyMember(SceneNow.PartnerName, SceneNow.PartnerWorld);
            if (p == null) return false;
            return (IsCow && (p.Roles.Contains("bull") || p.Role == "bull")) || (IsBull && (p.Roles.Contains("cow") || p.Role == "cow"));
        }
    }

    /// <summary>One short line about the scene: who with and arousal.</summary>
    public string SceneLine =>
        (SceneNow.Solo || SceneNow.PartnerName.Length == 0 ? "Solo scene" : $"With {SceneNow.PartnerName.Split(' ')[0]}") +
        (ArousalNow.Ready ? ArousalNow.RefractorySeconds > 0 ? " · catching your breath" : $" · arousal {ArousalNow.Value}%" : "");

    private void ReadScene(bool force = false)
    {
        if (!force && now < nextSceneRead) return;
        nextSceneRead = now + 1;
        if (!CorruptionOn)
        {
            SceneNow = CorruptionLink.NoScene;
            ArousalNow = CorruptionLink.NoArousal;
            return;
        }

        SceneNow = plugin.Corruption.GetScene();
        ArousalNow = InScene ? plugin.Corruption.GetArousal() : CorruptionLink.NoArousal;

        // A new scene (or a new partner) allows one more breeding; the same scene going on doesn't
        var key = InScene ? $"{SceneNow.PartnerName}@{SceneNow.PartnerWorld}" : "";
        if (key != sceneKey)
        {
            sceneKey = key;
            bredThisScene = false;
        }
    }

    private void BreedingUpdate()
    {
        ReadScene();
        if (now < nextPregRead || Plugin.ObjectTable.LocalPlayer == null) return;
        nextPregRead = now + 30;
        var p = CorruptionOn ? plugin.Corruption.GetPregnancy() : CorruptionLink.NoPregnancy;
        if (!p.Ready) p = CorruptionLink.NoPregnancy;
        Preg = p;
        var was = F.PregStage;
        if (was == p.Stage) return;
        F.PregStage = p.Stage;
        plugin.SaveConfig();
        if (!IsCow) return;
        // Tell the cow what changed (not when it's first read after loading)
        if (was == "none" && Pregnant)
            Say($"You're pregnant{(p.FatherName.Length > 0 ? $" by {p.FatherName}" : "")}! Your milk goes up {PregnancyBonus * 100:0}% (more each trimester).", 1);
        else if (Pregnant && was is "first" or "second")
            Say($"Your pregnancy is in its {StageName(p.Stage)}: milk +{PregnancyBonus * 100:0}%.", 1);
        else if (p.Stage == "recovering" && was is "first" or "second" or "third")
            Say("You gave birth! The pregnancy milk bonus has ended.", 1);
        Diag.Info("breed", $"Pregnancy stage {was} -> {p.Stage}");
    }

    /// <summary>A member of your dairy with this name and world, if any.</summary>
    private MemberInfo? DairyMember(string name, string world) =>
        Net.Mine?.Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                              (world.Length == 0 || m.World.Equals(world, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Corruption: your scene partner climaxed. If you're a cow and they're a bull of your dairy, you've been bred.</summary>
    public void OnPartnerClimax(string name, string world)
    {
        if (!IsCow || Blocked() != null) return;
        ReadScene(true);   // up to date, so a scene that only just started is the one this breeding belongs to
        var bull = DairyMember(name, world);
        if (bull == null || !(bull.Roles.Contains("bull") || bull.Role == "bull")) return;
        if (bredThisScene)
        {
            Diag.Info("breed", $"{name} finished again in the same scene; only the first counts");
            return;
        }

        if (BreedCooldownLeft > TimeSpan.Zero)
        {
            Diag.Info("breed", $"Bred by {name} again, but the last breeding was under {BredCooldownMinutes} min ago");
            return;
        }

        var seed = bull.Status?.Seed is > 0 and var s ? s : 100;
        bredThisScene = true;
        F.BredAtUtc = DateTime.UtcNow;
        F.BredUntilUtc = DateTime.UtcNow.AddMinutes(BredMinutes);
        F.BredStrength = Math.Clamp(seed / 100f, 0.2f, 1f);
        F.BredBy = bull.Name;
        Say($"{bull.Name} bred you: +{BredBonusNow * 100:0.#}% milk for {BredMinutes} minutes{(seed < 100 ? $" (his seed is at {seed}% today)" : "")}.", 1);
        plugin.SaveConfig();
    }

    // ----- Bulls: breeding and seed output -----

    /// <summary>Seed output for a bull's next breeding today: full for the first few, then 20% less each, down to 20%.</summary>
    public static float SeedFor(int breedsBefore) => breedsBefore < FreshBreeds ? 1f : Math.Max(0.2f, 1f - 0.2f * (breedsBefore - FreshBreeds + 1));

    /// <summary>Your seed output now (for your next breeding), 20 to 100.</summary>
    public int SeedNow => (int)Math.Round(SeedFor(F.DayBreeds) * 100);

    /// <summary>Corruption: you climaxed. If you're a bull in a scene with a cow of your dairy, you bred her.</summary>
    public void OnClimax()
    {
        if (!IsBull || Blocked() != null) return;
        ReadScene(true);
        var scene = SceneNow;
        if (!scene.Ready || scene.Solo || scene.PartnerName.Length == 0) return;
        var cow = DairyMember(scene.PartnerName, scene.PartnerWorld);
        if (cow == null || !(cow.Roles.Contains("cow") || cow.Role == "cow")) return;
        if (bredThisScene)
        {
            Diag.Info("breed", $"Finished again with {cow.Name} in the same scene; only the first counts");
            return;
        }

        bredThisScene = true;
        var output = SeedFor(F.DayBreeds);
        F.DayBreeds++;
        var xp = Math.Max(1, (int)Math.Round(BreedXp * output));
        AddBullXp(xp);
        var next = SeedNow;
        Say($"You bred {cow.Name}. +{xp} bull xp" + (next < 100 ? $". You're getting tired: seed output {next}% for the next one (resets at midnight UTC)." : "."), 1);
        plugin.SaveConfig();
    }

    private void AddBullXp(int amount)
    {
        var before = BullLevel;
        F.BullXp += amount;
        for (var l = before + 1; l <= BullLevel; l++)
            Say($"Bull level {l}!" + (Leveling.BullRank(l) is { } t && l % 25 == 0 ? $" You're a {t}." : ""), 1);
    }
}
