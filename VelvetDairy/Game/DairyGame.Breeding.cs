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

    /// <summary>The Champion's ribbon: a prized cow (farmers who hand milk her earn 10% more; bred 45 minutes; pregnancy +5%).</summary>
    public bool Prized => Owned("champion") > 0;

    /// <summary>How long being bred lasts for you: 30 minutes, 45 with the Champion's ribbon.</summary>
    public int BredMinutesNow => Prized ? 45 : BredMinutes;

    /// <summary>A finish only breeds after this long in the scene (so a scene doesn't end in a breeding a minute in).</summary>
    public const int BreedSceneMinutes = 10;

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
    public float PregnancyBonus
    {
        get
        {
            if (!IsCow) return 0f;
            var b = Preg.Stage switch { "first" => 0.05f, "second" => 0.10f, "third" => 0.15f, _ => 0f };
            return b > 0 && Prized ? b + 0.05f : b;   // the Champion's ribbon: +5% more
        }
    }

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
        (BreedScene && BreedTimerLeft > TimeSpan.Zero ? $" · breeding in {Math.Max(1, (int)Math.Ceiling(BreedTimerLeft.TotalMinutes))}m"
            : ArousalNow.Ready ? ArousalNow.RefractorySeconds > 0 ? " · catching your breath" : $" · arousal {ArousalNow.Value}%" : "") +
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

    // Each way on its own: you could breed them, they could breed you
    // Your dairy roles decide who can breed whom in Eorzea Milkers (the bred bonus, bull xp, the "who breeds" pop-up).
    // What Corruption says each body can do only decides whether Corruption can make it a pregnancy.
    private bool CouldBreedThem => IsBull && PartnerIs("cow");
    private bool CouldBeBred => IsCow && PartnerIs("bull");

    /// <summary>Corruption can make a pregnancy of the way this scene breeds (both bodies allow it).</summary>
    private bool CorruptionCanBreedThem => myBody.CanBreed && partnerBody.CanCarry;
    private bool CorruptionCanBreedMe => myBody.CanCarry && partnerBody.CanBreed;

    /// <summary>
    /// Both of you are cow and bull and could go either way: it's not decided by who finishes. Whoever is breeding
    /// targets the other and presses Breed (on the HUD, or right-click > Eorzea Milkers), and only that way counts.
    /// </summary>
    public bool EitherWayScene => CouldBreedThem && CouldBeBred;

    /// <summary>You pressed Breed on your scene partner.</summary>
    public bool IChoseToBreed => breedChoiceFor.Length > 0 && breedChoiceFor.Equals(sceneKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>Your partner pressed Breed on you (their status says so).</summary>
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

    /// <summary>For your status: who you pressed Breed on (Name@World), so their plugin knows; empty otherwise.</summary>
    public string BreedingTarget => breedChoiceFor;

    /// <summary>You both pressed Breed on the other: neither counts until one of you cancels.</summary>
    public bool BothChoseToBreed => EitherWayScene && IChoseToBreed && PartnerChoseToBreed;

    /// <summary>You can press Breed on this player: you're a bull and they're a cow of your dairy.</summary>
    public bool CanBreedPlayer(string name, string world) =>
        IsBull && Networked && DairyMember(name, world) is { } m && m.Has("cow");

    /// <summary>You've pressed Breed on this player (until it's done, or you press it again).</summary>
    public bool BreedingPlayer(string name, string world) =>
        breedChoiceFor.Equals($"{name}@{world}", StringComparison.OrdinalIgnoreCase);

    /// <summary>Breed button: you're the one breeding this player (press again to cancel).</summary>
    public void ToggleBreed(string name, string world)
    {
        var key = $"{name}@{world}";
        if (breedChoiceFor.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            breedChoiceFor = "";
            Say($"You won't breed {name} now.", 0);
        }
        else
        {
            breedChoiceFor = key;
            Say($"You're breeding {name}: when you finish in a scene with them, you breed them" + (IsCow ? " (and their finish doesn't breed you)." : "."), 0);
        }

        Net.BeatWithin(1);
        Diag.Info("breed", breedChoiceFor.Length > 0 ? $"Pressed Breed on {key}" : $"Cancelled Breed on {key}");
    }

    private string breedChoiceFor = "";

    // ----- Breeding scenes: a 10 minute minimum, and the breeder starts from 0 arousal -----

    private double sceneStartedAt = -1;
    private string arousalResetFor = "";
    private double nextArousalStep;
    private bool arousalResetting;

    /// <summary>This scene breeds one way or the other (you breed them, or they breed you).</summary>
    public bool BreedScene => BreedingAsBull || BredAsCow;

    /// <summary>Time in this scene so far (zero when not in one).</summary>
    public TimeSpan SceneTime => sceneStartedAt < 0 || !InScene ? TimeSpan.Zero : TimeSpan.FromSeconds(now - sceneStartedAt);

    /// <summary>How long until a finish breeds in this scene (zero once the minimum is up).</summary>
    public TimeSpan BreedTimerLeft => sceneStartedAt < 0 ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Math.Max(0, BreedSceneMinutes * 60 - (now - sceneStartedAt)));

    /// <summary>The breed timer filling up, 0 to 1 (for the HUD's bar).</summary>
    public float BreedTimerFraction => (float)Math.Clamp((now - sceneStartedAt) / (BreedSceneMinutes * 60.0), 0, 1);

    /// <summary>
    /// When a scene becomes one where you're breeding them, your arousal is brought down to 0, as fast as Corruption
    /// lets another plugin (60 a minute), so you don't start the breeding already about to finish. Needs Corruption's
    /// "Let other plugins control Corruption"; without it nothing changes.
    /// </summary>
    private void ResetBreederArousal()
    {
        if (!InScene || !BreedingAsBull)
        {
            arousalResetting = false;
            return;
        }

        if (arousalResetFor != sceneKey)
        {
            arousalResetFor = sceneKey;
            arousalResetting = true;
            nextArousalStep = 0;
            Diag.Info("breed", $"Breeding {SceneNow.PartnerName}: bringing your arousal down to 0 (it's {ArousalNow.Value}%)");
        }

        if (!arousalResetting || now < nextArousalStep) return;
        if (!ArousalNow.Ready || ArousalNow.Value <= 0)
        {
            arousalResetting = false;
            return;
        }

        nextArousalStep = now + 25;   // 25 at a time: well inside Corruption's 60 a minute
        var answer = plugin.Corruption.AddArousal(-Math.Min(25, ArousalNow.Value));
        if (answer is "ok" or "limited") return;
        arousalResetting = false;
        Diag.Info("breed", $"Couldn't lower your arousal: Corruption answered \"{answer}\"" +
                           (answer == "not-allowed" ? " (turn on \"Let other plugins control Corruption\" in Corruption's settings)" : ""));
    }
    private bool sceneLogged;
    private string lastSceneLine = "";

    /// <summary>
    /// For the scene note: a missing role in your dairy that rules a way out, and when Corruption can't make a breeding
    /// a pregnancy (a body set to carry only or breed only). Empty when there's nothing to say.
    /// </summary>
    public string BreedOneWayWhy
    {
        get
        {
            if (!InScene || SceneNow.Solo || SceneNow.PartnerName.Length == 0) return "";
            var name = SceneNow.PartnerName.Split(' ')[0];
            var why = new List<string>();
            if (IsCow && IsBull && PartnerIs("cow") && !PartnerIs("bull")) why.Add($"{name} isn't a bull in your dairy, so they can't breed you");
            if (IsCow && IsBull && PartnerIs("bull") && !PartnerIs("cow")) why.Add($"{name} isn't a cow in your dairy, so you can't breed them");
            if (BredAsCow && !CorruptionCanBreedMe)
                why.Add(!myBody.CanCarry ? "your Corruption body is set so you can't carry, so Corruption won't roll a pregnancy"
                    : $"{name}'s Corruption body is set so they can't breed, so Corruption won't roll a pregnancy from them (the bred bonus still counts)");
            if (BreedingAsBull && !CorruptionCanBreedThem)
                why.Add(!myBody.CanBreed ? $"your Corruption body is set so you can't breed, so Corruption won't roll a pregnancy for {name} (the bred bonus still counts)"
                    : $"{name}'s Corruption body is set so they can't carry, so Corruption won't roll a pregnancy");
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
        // Breeding them (an either-way scene: only when you chose to), and Corruption can make it a pregnancy
        var want = CorruptionOn && BreedingAsBull && CorruptionCanBreedThem;
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
        ConceiveChance = BredAsCow && CorruptionCanBreedMe && !Pregnant ? Math.Clamp(plugin.Corruption.BreedChance(SceneNow.PartnerName, SceneNow.PartnerWorld), 0, 1) : 0;

        // A new scene (or a new partner) allows one more breeding; the same scene going on doesn't
        var key = InScene ? $"{SceneNow.PartnerName}@{SceneNow.PartnerWorld}" : "";
        if (key != sceneKey)
        {
            if (bredThemThisScene && breedChoiceFor.Equals(sceneKey, StringComparison.OrdinalIgnoreCase)) breedChoiceFor = "";
            sceneKey = key;
            wasBredThisScene = bredThemThisScene = false;
            if (key.Length > 0)
            {
                sceneLogged = false;
                sceneStartedAt = now;
            }
            else
            {
                sceneStartedAt = -1;
                Diag.Info("breed", "Scene ended");
            }
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
        ResetBreederArousal();
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
        if (DateTime.UtcNow - at > TimeSpan.FromMinutes(BredMinutesNow + 5)) return;
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
        if (!CorruptionCanBreedMe)
            Diag.Info("breed", $"{name} breeds you in the dairy; Corruption won't roll a pregnancy (you can carry: {myBody.CanCarry}, they can breed: {partnerBody.CanBreed})");
        if (BreedTimerLeft > TimeSpan.Zero)
        {
            Diag.Info("breed", $"{name} finished {SceneTime.TotalMinutes:0.#} min into the scene; breeding counts after {BreedSceneMinutes} min");
            Say($"Too soon to breed: a finish breeds after {BreedSceneMinutes} minutes in the scene ({DairyGame.Wait(BreedTimerLeft)} to go).", 0);
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
        F.BredUntilUtc = DateTime.UtcNow.AddMinutes(BredMinutesNow);
        F.BredStrength = Math.Clamp(seed / 100f, 0.2f, 1f);
        F.BredBy = bull.Name;
        Say($"{bull.Name} bred you: +{BredBonusNow * 100:0.#}% milk for {BredMinutesNow} minutes{(seed < 100 ? $" (their breeding output is at {seed}% today)" : "")}.", 1);
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
        if (!CorruptionCanBreedThem)
            Diag.Info("breed", $"You breed them in the dairy; Corruption won't roll a pregnancy (you can breed: {myBody.CanBreed}, they can carry: {partnerBody.CanCarry})");
        if (BreedTimerLeft > TimeSpan.Zero)
        {
            Diag.Info("breed", $"You finished {SceneTime.TotalMinutes:0.#} min into the scene; breeding counts after {BreedSceneMinutes} min");
            Say($"Too soon to breed: a finish breeds after {BreedSceneMinutes} minutes in the scene ({DairyGame.Wait(BreedTimerLeft)} to go).", 0);
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
