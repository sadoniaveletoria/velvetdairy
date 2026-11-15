using System.Collections.Generic;
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

    /// <summary>Your past births from Corruption (read with your pregnancy, every 30 seconds).</summary>
    public List<CorruptionLink.Birth> BirthsNow { get; private set; } = new();

    /// <summary>Cows of your dairy who are pregnant by you (confirmed), from their status: (name, stage).</summary>
    public List<(string Name, string Stage)> PregnantByMe()
    {
        var me = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "";
        if (me.Length == 0 || Net.Mine == null) return new();
        return Net.Mine.Members
            .Where(m => m.Status is { Preg.Length: > 0 } st && st.PregFather.Equals(me, StringComparison.OrdinalIgnoreCase))
            .Select(m => (m.Name, m.Status!.Preg)).ToList();
    }

    public CorruptionLink.Scene SceneNow { get; private set; } = CorruptionLink.NoScene;
    public CorruptionLink.Arousal ArousalNow { get; private set; } = CorruptionLink.NoArousal;
    private double nextSceneRead;
    private string sceneKey = "";
    // One breeding each way per scene: climaxing again in the same scene doesn't count again. Kept apart, so two players
    // who are both cow and bull can each breed the other in one scene.
    private bool wasBredThisScene, bredThemThisScene;

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
        (ArousalNow.Ready ? ArousalNow.RefractorySeconds > 0 ? " · catching your breath" : $" · arousal {ArousalNow.Value}%" : "") +
        (ConceiveChance > 0 ? $" · {ConceiveChance * 100:0}% chance" : "") +
        (ForcedCreampie ? " · creampie" : "");

    /// <summary>
    /// A cow in a scene with a bull of her dairy: Corruption's chance (0 to 1) that his finishing gets her pregnant now.
    /// 0 when there's nothing to show (no breeding scene, already pregnant, or Corruption would refuse). Only shown to her.
    /// </summary>
    public double ConceiveChance { get; private set; }

    // ----- Forced creampies while a bull of your dairy breeds you -----

    /// <summary>What we last asked Corruption for (forced creampies on or off).</summary>
    private bool forcedOn;
    private double nextForceTry;

    /// <summary>Corruption's last answer when turning forced creampies on ("ok", "not-allowed", ...).</summary>
    public string ForceCreampieAnswer { get; private set; } = "";

    /// <summary>Forced creampies are on right now (a breeding scene with a bull of your dairy).</summary>
    public bool ForcedCreampie => forcedOn && ForceCreampieAnswer == "ok";

    /// <summary>Corruption reloaded: it forgot, so set it again on the next check.</summary>
    public void CorruptionReloaded()
    {
        forcedOn = false;
        nextForceTry = 0;
    }

    /// <summary>Your scene partner has that role in your dairy.</summary>
    private bool PartnerIs(string role)
    {
        if (!InScene || SceneNow.Solo || SceneNow.PartnerName.Length == 0) return false;
        var p = DairyMember(SceneNow.PartnerName, SceneNow.PartnerWorld);
        return p != null && p.Has(role);
    }

    // Each way on its own: you could breed them, they could breed you (roles in your dairy, and what the bodies can do)
    private bool CouldBreedThem => IsBull && myBody.CanBreed && partnerBody.CanCarry && PartnerIs("cow");
    private bool CouldBeBred => IsCow && myBody.CanCarry && partnerBody.CanBreed && PartnerIs("bull");

    /// <summary>
    /// Both of you are cow and bull and could go either way: it's not decided by who finishes. One of you chooses to
    /// breed the other for this scene (a pop-up), and only that way counts. A new scene chooses again.
    /// </summary>
    public bool EitherWayScene => CouldBreedThem && CouldBeBred;

    /// <summary>You chose to breed your partner this scene.</summary>
    public bool IChoseToBreed => breedChoiceFor.Length > 0 && breedChoiceFor == sceneKey;

    /// <summary>Your partner chose to breed you this scene (their status says so).</summary>
    public bool PartnerChoseToBreed
    {
        get
        {
            if (!InScene || SceneNow.Solo) return false;
            var p = DairyMember(SceneNow.PartnerName, SceneNow.PartnerWorld);
            var me = Plugin.ObjectTable.LocalPlayer;
            if (p?.Status == null || me == null) return false;
            var mine = $"{me.Name.TextValue}@{me.HomeWorld.ValueNullable?.Name.ExtractText() ?? ""}";
            return p.Status.Breeding.Equals(mine, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>For your status: who you chose to breed (Name@World), so their plugin knows; empty otherwise.</summary>
    public string BreedingTarget => EitherWayScene && IChoseToBreed ? sceneKey : "";

    /// <summary>You both chose to breed the other: neither counts until one of you lets the other.</summary>
    public bool BothChoseToBreed => EitherWayScene && IChoseToBreed && PartnerChoseToBreed;

    /// <summary>Choose who breeds this scene (true: you breed them; false: you don't, so they can).</summary>
    public void ChooseToBreed(bool me)
    {
        breedChoiceFor = me ? sceneKey : "";
        breedChoiceAsked = sceneKey;
        Net.BeatWithin(1);
        Diag.Info("breed", me ? $"Chose to breed {SceneNow.PartnerName} this scene" : "Chose not to breed this scene");
    }

    /// <summary>The pop-up asking who breeds: an either-way scene where neither of you has chosen yet.</summary>
    public bool WantsBreedChoice => EitherWayScene && (BothChoseToBreed || (breedChoiceAsked != sceneKey && !PartnerChoseToBreed));

    public void DismissBreedChoice() => breedChoiceAsked = sceneKey;

    private string breedChoiceFor = "", breedChoiceAsked = "";
    private bool sceneLogged;
    private string lastSceneLine = "";

    /// <summary>
    /// Why only one way (or no way) breeds in this scene when your roles would allow more: what Corruption says a body
    /// can't do, or a missing role in your dairy. Empty when nothing is ruled out.
    /// </summary>
    public string BreedOneWayWhy
    {
        get
        {
            if (!InScene || SceneNow.Solo || SceneNow.PartnerName.Length == 0) return "";
            var name = SceneNow.PartnerName.Split(' ')[0];
            var why = new List<string>();
            if (IsCow && PartnerIs("bull") && !CouldBeBred)
                why.Add(!myBody.CanCarry ? $"your Corruption body is set so you can't carry, so {name} can't breed you"
                    : $"{name}'s Corruption body is set so they can't breed, so they can't breed you");
            if (IsBull && PartnerIs("cow") && !CouldBreedThem)
                why.Add(!myBody.CanBreed ? $"your Corruption body is set so you can't breed, so you can't breed {name}"
                    : $"{name}'s Corruption body is set so they can't carry, so you can't breed them");
            if (IsCow && !PartnerIs("bull") && PartnerIs("cow") && IsBull) why.Add($"{name} isn't a bull in your dairy, so they can't breed you");
            if (IsBull && !PartnerIs("cow") && PartnerIs("bull") && IsCow) why.Add($"{name} isn't a cow in your dairy, so you can't breed them");
            return why.Count == 0 ? "" : char.ToUpperInvariant(why[0][0]) + string.Join("; ", why)[1..] + ".";
        }
    }

    /// <summary>You're a bull in a scene with a cow of your dairy: you're the one breeding.</summary>
    public bool BreedingAsBull => CouldBreedThem && (!CouldBeBred || (IChoseToBreed && !PartnerChoseToBreed));

    /// <summary>You're a cow in a scene with a bull of your dairy: you're the one being bred.</summary>
    public bool BredAsCow => CouldBeBred && (!CouldBreedThem || (PartnerChoseToBreed && !IChoseToBreed));

    // What each body can do in this scene (from Corruption: who can breed, who can carry). Two players who are both
    // cows and bulls can go either way; their bodies decide which way(s) a finish breeds.
    private (bool CanCarry, bool CanBreed) myBody = (true, true), partnerBody = (true, true);

    /// <summary>
    /// A bull in a scene with a cow of their dairy: every finish creampies the cow's pussy (Corruption 0.3.5: set on the
    /// bull's side, and the cow's Corruption honours it for that bull only), so Corruption rolls for conception. Always on
    /// for breeding scenes, off again as soon as the scene isn't one. Corruption's own consent switch (the bull's) and the
    /// cow's Cummy blocks still apply; if Corruption says no, try again now and then (consent may be turned on).
    /// </summary>
    private void UpdateForcedCreampie()
    {
        var want = CorruptionOn && BreedingAsBull;   // an either-way scene: only when you chose to breed them
        if (want == forcedOn && (!want || ForceCreampieAnswer == "ok")) return;
        if (!want)
        {
            plugin.Corruption.SetForceCreampie(false);
            forcedOn = false;
            return;
        }

        if (now < nextForceTry) return;
        nextForceTry = now + 30;
        ForceCreampieAnswer = plugin.Corruption.SetForceCreampie(true);
        forcedOn = true;
        if (ForceCreampieAnswer != "ok") Diag.Info("breed", $"Forced creampies: Corruption answered {ForceCreampieAnswer}");
    }

    /// <summary>Turn forced creampies off (unloading).</summary>
    public void StopForcedCreampie()
    {
        if (forcedOn) plugin.Corruption.SetForceCreampie(false);
        forcedOn = false;
    }

    private void ReadScene(bool force = false)
    {
        if (!force && now < nextSceneRead) return;
        nextSceneRead = now + 1;
        if (!CorruptionOn)
        {
            SceneNow = CorruptionLink.NoScene;
            ArousalNow = CorruptionLink.NoArousal;
            ConceiveChance = 0;
            return;
        }

        SceneNow = plugin.Corruption.GetScene();
        ArousalNow = InScene ? plugin.Corruption.GetArousal() : CorruptionLink.NoArousal;
        if (InScene && !SceneNow.Solo)
        {
            var mine = plugin.Corruption.GetBody();
            var theirs = plugin.Corruption.GetBodyOf(SceneNow.PartnerName, SceneNow.PartnerWorld);
            if (mine != myBody || theirs != partnerBody)
                Diag.Info("breed", $"Bodies (from Corruption): you carry {mine.CanCarry}, breed {mine.CanBreed}; {SceneNow.PartnerName} carry {theirs.CanCarry}, breed {theirs.CanBreed}");
            myBody = mine;
            partnerBody = theirs;
        }
        else myBody = partnerBody = (true, true);
        ConceiveChance = BredAsCow && !Pregnant ? Math.Clamp(plugin.Corruption.BreedChance(SceneNow.PartnerName, SceneNow.PartnerWorld), 0, 1) : 0;

        // A new scene (or a new partner) allows one more breeding; the same scene going on doesn't
        var key = InScene ? $"{SceneNow.PartnerName}@{SceneNow.PartnerWorld}" : "";
        if (key != sceneKey)
        {
            sceneKey = key;
            wasBredThisScene = bredThemThisScene = false;
            if (key.Length > 0) sceneLogged = false;
            else Diag.Info("breed", "Scene ended");
        }

        // Once per scene (and again if it changes): who could breed whom, and why, so a scene that didn't breed can be explained
        if (InScene && !SceneNow.Solo)
        {
            var p = DairyMember(SceneNow.PartnerName, SceneNow.PartnerWorld);
            var line = $"Scene with {SceneNow.PartnerName}@{SceneNow.PartnerWorld} ({SceneNow.State}): your roles {string.Join("/", MyRoles)}, theirs " +
                       (p == null ? "not in your dairy" : string.Join("/", p.AllRoles)) +
                       $"; you could breed them {CouldBreedThem}, they could breed you {CouldBeBred}, either way {EitherWayScene}" +
                       (EitherWayScene ? $" (you chose to breed {IChoseToBreed}, they chose {PartnerChoseToBreed})" : "") +
                       (BreedOneWayWhy.Length > 0 ? $"; {BreedOneWayWhy}" : "");
            if (!sceneLogged || line != lastSceneLine)
            {
                Diag.Info("breed", line);
                sceneLogged = true;
                lastSceneLine = line;
            }
        }
        else if (InScene && SceneNow.Solo && !sceneLogged)
        {
            Diag.Info("breed", "Solo scene (no partner): nothing to breed");
            sceneLogged = true;
        }

        UpdateForcedCreampie();
    }

    /// <summary>A notice to your dairy: show it as a pop-up (with a little bell).</summary>
    public void OnNotice(DairyNotice n)
    {
        if (n.Kind == "breedReady")
            plugin.Notices.Show("Ready to be bred", $"{n.Name} can be bred again.");
    }

    private DateTime? readyToldFor;

    /// <summary>
    /// A cow's breeding bonus has run out (30 minutes after she was bred): tell her, and let the rest of her dairy know (a pop-up with a bell).
    /// Only right after it happens (not for an old breeding after a restart).
    /// </summary>
    private void AnnounceReadyAgain()
    {
        var at = F.BredAtUtc;
        if (!IsCow || at <= DateTime.MinValue || readyToldFor == at || BredLeft > TimeSpan.Zero) return;
        readyToldFor = at;
        if (DateTime.UtcNow - at > TimeSpan.FromMinutes(BredMinutes + 5)) return;
        plugin.Notices.Show("Ready to be bred", "You can be bred again.");
        if (Networked && F.NetMember != null) Net.PostNotice("breedReady");
    }

    private void BreedingUpdate()
    {
        ReadScene();
        AnnounceReadyAgain();
        if (now < nextPregRead || Plugin.ObjectTable.LocalPlayer == null) return;
        nextPregRead = now + 30;
        var p = CorruptionOn ? plugin.Corruption.GetPregnancy() : CorruptionLink.NoPregnancy;
        if (!p.Ready) p = CorruptionLink.NoPregnancy;
        Preg = p;
        BirthsNow = CorruptionOn ? plugin.Corruption.GetBirths() : new List<CorruptionLink.Birth>();
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
        if (!IsCow)
        {
            Diag.Info("breed", $"{name} finished: you aren't a cow, so it doesn't breed you");
            return;
        }

        if (Blocked() is { } blocked)
        {
            Diag.Info("breed", $"{name} finished, but breeding is blocked: {blocked}");
            return;
        }

        ReadScene(true);   // up to date, so a scene that only just started is the one this breeding belongs to
        var bull = DairyMember(name, world);
        if (bull == null || !bull.Has("bull"))
        {
            Diag.Info("breed", bull == null ? $"{name}@{world} finished, but they aren't in your dairy" : $"{name} finished, but they aren't a bull in your dairy");
            return;
        }
        if (EitherWayScene && !BredAsCow)
        {
            Diag.Info("breed", $"{name} finished, but they didn't choose to breed you this scene");
            return;
        }
        if (!myBody.CanCarry || !partnerBody.CanBreed)
        {
            Diag.Info("breed", $"{name} finished, but your bodies don't breed that way (you can carry: {myBody.CanCarry}, they can breed: {partnerBody.CanBreed})");
            return;
        }
        if (wasBredThisScene)
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
        wasBredThisScene = true;
        F.BredAtUtc = DateTime.UtcNow;
        F.BredUntilUtc = DateTime.UtcNow.AddMinutes(BredMinutes);
        F.BredStrength = Math.Clamp(seed / 100f, 0.2f, 1f);
        F.BredBy = bull.Name;
        Say($"{bull.Name} bred you: +{BredBonusNow * 100:0.#}% milk for {BredMinutes} minutes{(seed < 100 ? $" (their breeding output is at {seed}% today)" : "")}.", 1);
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
        if (!IsBull)
        {
            Diag.Info("breed", "You finished: you aren't a bull, so it doesn't breed anyone");
            return;
        }

        if (Blocked() is { } blocked)
        {
            Diag.Info("breed", $"You finished, but breeding is blocked: {blocked}");
            return;
        }

        ReadScene(true);
        var scene = SceneNow;
        if (!scene.Ready || scene.Solo || scene.PartnerName.Length == 0)
        {
            Diag.Info("breed", $"You finished, but not in a scene with a partner (ready {scene.Ready}, solo {scene.Solo})");
            return;
        }

        var cow = DairyMember(scene.PartnerName, scene.PartnerWorld);
        if (cow == null || !cow.Has("cow"))
        {
            Diag.Info("breed", cow == null ? $"You finished with {scene.PartnerName}@{scene.PartnerWorld}, but they aren't in your dairy" : $"You finished with {cow.Name}, but they aren't a cow in your dairy");
            return;
        }
        if (EitherWayScene && !BreedingAsBull)
        {
            Diag.Info("breed", "You finished, but you didn't choose to breed them this scene");
            return;
        }
        if (!myBody.CanBreed || !partnerBody.CanCarry)
        {
            Diag.Info("breed", $"You finished, but your bodies don't breed that way (you can breed: {myBody.CanBreed}, they can carry: {partnerBody.CanCarry})");
            return;
        }
        if (bredThemThisScene)
        {
            Diag.Info("breed", $"Finished again with {cow.Name} in the same scene; only the first counts");
            return;
        }

        bredThemThisScene = true;
        var output = SeedFor(F.DayBreeds);
        F.DayBreeds++;
        var xp = Math.Max(1, (int)Math.Round(BreedXp * output));
        AddBullXp(xp);
        var next = SeedNow;
        Say($"You bred {cow.Name}. +{xp} bull xp" + (next < 100 ? $". You're getting tired: seed output {next}% for the next one (resets at midnight UTC)." : "."), 1);
        plugin.SaveConfig();
    }

    /// <summary>For a scan: each thing speeding your milk (or a bull-only player's seed) up or down now, and the total.</summary>
    public List<Network.ScanBonus> ScanBonuses(out float total)
    {
        var parts = ProducesMilk ? RateParts() : ProducesSeed ? RateParts(true, true) : new List<RatePart>();
        total = 1f;
        var list = new List<Network.ScanBonus>();
        foreach (var p in parts)
        {
            total *= p.Multiplier;
            list.Add(new Network.ScanBonus { N = p.Name, P = (float)Math.Round((p.Multiplier - 1f) * 100f) });
        }

        total = (float)Math.Round(total, 2);
        return list;
    }

    private void AddBullXp(int amount)
    {
        var before = BullLevel;
        F.BullXp += amount;
        for (var l = before + 1; l <= BullLevel; l++)
            Say($"Bull level {l}!" + (Leveling.BullRank(l) is { } t && l % 25 == 0 ? $" You're a {t}." : ""), 1);
    }
}
