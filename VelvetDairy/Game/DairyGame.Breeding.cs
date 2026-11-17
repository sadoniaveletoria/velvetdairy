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


    /// <summary>Bulls breed as often as they like, but after this many a day their seed output drops.</summary>
    public const int FreshBreeds = 20;

    /// <summary>Bull xp for one breeding at full seed output.</summary>
    public const int BreedXp = 40;

    /// <summary>Dairy xp for each breeding with the Breeding pen.</summary>
    public const int BreedPenDairyXp = 15;

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
    public float BredBonusNow => BredLeft > TimeSpan.Zero ? BredBonus * Math.Clamp(F.BredStrength, 0.2f, 1.5f) : 0f;

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

    // Each way on its own: you could breed them, they could breed you
    // Your dairy roles decide who can breed whom in Eorzea Milkers (the bred bonus, bull xp, the "who breeds" pop-up).
    // What Corruption says each body can do only decides whether Corruption can make it a pregnancy.
    // ...or a partner from another dairy who said yes (the cow pressed Allow for this bull)
    private bool CouldBreedThem => IsBull && (PartnerIs("cow") || OutsideCowSaidYes);
    private bool CouldBeBred => IsCow && (PartnerIs("bull") || OutsideBullAllowed(SceneNow.PartnerName));

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

    /// <summary>
    /// You can press Breed on this player: you're both cow and bull, and so are they (that's when it decides who breeds;
    /// a bull and a cow who aren't both breed the one way without it).
    /// </summary>
    public bool CanBreedPlayer(string name, string world) =>
        IsBull && IsCow && Networked && DairyMember(name, world) is { } m && m.Has("cow") && m.Has("bull");

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

        UpdateOutsidePartner();
        UpdateForcedCreampie();
    }

    /// <summary>A notice to your dairy: show it as a pop-up (with a little bell).</summary>
    public void OnNotice(DairyNotice n)
    {
        if (n.Kind == "breedReady")
            plugin.Notices.Show("Ready to be bred", $"{n.Name}'s bred bonus has run out.");
        else if (n.Kind == "feedingBell")
            HeardFeedingBell(n.Name);
    }

    private DateTime? readyToldFor;

    /// <summary>
    /// A cow's breeding bonus has run out (30 minutes after she was bred): tell her, and let the rest of her dairy know (a pop-up with a bell).
    /// Only right after it happens (not for an old breeding after a restart).
    /// </summary>
    private bool wardFilled;

    /// <summary>
    /// An owner whose dairy is an outdoor box in a residential district, saved before the ward was kept: the next time
    /// they stand in it, the ward is added (so the Dairies list shows it and Visit can take people there).
    /// </summary>
    private void FillOutdoorWard()
    {
        if (wardFilled || !Networked || F.NetMember is not { IsOwner: true } m || !Where.InDairy || Where.Guest) return;
        if (Net.Mine?.Dairy is not { } d || d.Area is not { House.Length: 0 } area || Estate.DistrictKind(area.Territory) == 0) return;
        if (Plugin.ClientState.TerritoryType != area.Territory || Housing.OutdoorWard() == 0) return;
        // Missing its ward, or its yard (plot) when the owner is standing in one
        if (d.Ward > 0 && (d.Plot > 0 || Housing.OutdoorPlot() == 0 || !area.Contains(Plugin.ObjectTable.LocalPlayer?.Position ?? default))) return;
        wardFilled = true;
        Diag.Info("net", $"Adding ward {Housing.OutdoorWard()} to {d.Name}'s outdoor area");
        PublishDairy(d.Name, area);
    }

    private void AnnounceReadyAgain()
    {
        var at = F.BredAtUtc;
        if (!IsCow || at <= DateTime.MinValue || readyToldFor == at || BredLeft > TimeSpan.Zero) return;
        readyToldFor = at;
        if (DateTime.UtcNow - at > TimeSpan.FromMinutes(BredMinutesNow + 5)) return;
        plugin.Notices.Show("Ready to be bred", "Your bred bonus has run out.");
        if (Networked && F.NetMember != null) Net.PostNotice("breedReady");
    }

    private void BreedingUpdate()
    {
        ReadScene();
        FillOutdoorWard();
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
        // A bull of your dairy, or a bull from another dairy you pressed Allow for in this scene
        var bull = DairyMember(name, world);
        if (bull != null && !bull.Has("bull"))
        {
            Diag.Info("breed", $"{name} finished, but they aren't a bull in your dairy");
            return;
        }

        if (bull == null && !OutsideBullAllowed(name))
        {
            Diag.Info("breed", $"{name}@{world} finished, but they aren't in your dairy and you didn't allow them to breed you");
            return;
        }

        var bullName = bull?.Name ?? name;
        if (EitherWayScene && !BredAsCow)
        {
            Diag.Info("breed", $"{name} finished, but they didn't choose to breed you this scene");
            return;
        }
        if (!CorruptionCanBreedMe)
            Diag.Info("breed", $"{name} breeds you in the dairy; Corruption won't roll a pregnancy (you can carry: {myBody.CanCarry}, they can breed: {partnerBody.CanBreed})");
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

        var seed = bull?.Status?.Seed is > 0 and var s ? s : 100;
        var stud = bull?.Status?.StudTonic == true;   // the bull had a Stud draught: half again as strong, 15 minutes longer
        wasBredThisScene = true;
        F.BredAtUtc = DateTime.UtcNow;
        F.BredUntilUtc = DateTime.UtcNow.AddMinutes(BredMinutesNow + (stud ? 15 : 0));
        F.BredStrength = Math.Clamp(seed / 100f, 0.2f, 1f) * (stud ? 1.5f : 1f);
        F.BredBy = bullName;
        Say($"{bullName} bred you: +{BredBonusNow * 100:0.#}% milk for {BredMinutesNow + (stud ? 15 : 0)} minutes{(stud ? " (they had a Stud draught)" : "")}{(seed < 100 ? $" (their breeding output is at {seed}% today)" : "")}.", 1);
        plugin.SaveConfig();
    }

    // ----- Bulls: breeding and seed output -----

    /// <summary>Breedings a day at full seed output: 20, 25 with the Stud book, 30 with Legendary stud.</summary>
    public int FreshBreedsNow => Owned("legendstud") > 0 ? 30 : Owned("studbook") > 0 ? 25 : FreshBreeds;

    /// <summary>The lowest your seed output drops to: 20%, or 60% with the Champion bull sash.</summary>
    public float SeedFloor => Owned("bullsash") > 0 ? 0.6f : 0.2f;

    /// <summary>Seed output for a bull's next breeding today: full for the first few, then 20% less each, down to the floor.</summary>
    public float SeedFor(int breedsBefore) =>
        breedsBefore < FreshBreedsNow ? 1f : Math.Max(SeedFloor, 1f - 0.2f * (breedsBefore - FreshBreedsNow + 1));

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

        // A cow of your dairy, or a cow from another dairy who pressed Allow for you
        var cow = DairyMember(scene.PartnerName, scene.PartnerWorld);
        if (cow != null && !cow.Has("cow"))
        {
            Diag.Info("breed", $"You finished with {cow.Name}, but they aren't a cow in your dairy");
            return;
        }

        if (cow == null && !OutsideCowSaidYes)
        {
            Diag.Info("breed", $"You finished with {scene.PartnerName}@{scene.PartnerWorld}, but they aren't in your dairy and didn't allow you to breed them");
            return;
        }

        var cowName = cow?.Name ?? scene.PartnerName;
        if (EitherWayScene && !BreedingAsBull)
        {
            Diag.Info("breed", "You finished, but you didn't choose to breed them this scene");
            return;
        }
        if (!CorruptionCanBreedThem)
            Diag.Info("breed", $"You breed them in the dairy; Corruption won't roll a pregnancy (you can breed: {myBody.CanBreed}, they can carry: {partnerBody.CanCarry})");
        if (bredThemThisScene)
        {
            Diag.Info("breed", $"Finished again with {cowName} in the same scene; only the first counts");
            return;
        }

        bredThemThisScene = true;
        F.StudTonic = false;
        var output = SeedFor(F.DayBreeds);
        F.DayBreeds++;
        var xp = Math.Max(1, (int)Math.Round(BreedXp * output * (Owned("prizestud") > 0 ? 1.25f : 1f)));
        if (Owned("breedpen") > 0) EarnDairyXp(BreedPenDairyXp);   // the dairy only counts it while you're in it
        AddBullXp(xp);
        var next = SeedNow;
        Say($"You bred {cowName}. +{xp} bull xp" + (next < 100 ? $". You're getting tired: seed output {next}% for the next one (resets at midnight UTC)." : "."), 1);
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
        F.BullXp += Boosted(amount);
        for (var l = before + 1; l <= BullLevel; l++)
            Say($"Bull level {l}!" + (Leveling.BullRank(l) is { } t && l % 25 == 0 ? $" You're a {t}." : ""), 1);
    }

    // ----- Breeding stalls -----
    // A stall the owner switched to breeding. A cow sits in it for 5 minutes and is bred: the same bred bonus as a
    // scene, but no pregnancy (that only comes from Corruption). It uses 3 oz from the dairy's seed tank.

    public const int BreedStallMinutes = 5;
    public const float BreedStallSeedOz = 3f;
    public const int BreedStallMaxLoaded = 5;
    public const int BreedStallLoadXp = 8;
    private Stall? breedSitAt;
    private DateTime breedSitStart;
    private bool breedAsking;
    private string breedWarnedFor = "";

    /// <summary>Sitting in a breeding stall, waiting for it to finish.</summary>
    public bool OnBreedStall => breedSitAt != null;

    public TimeSpan BreedStallLeft
    {
        get
        {
            if (breedSitAt == null) return TimeSpan.Zero;
            var left = TimeSpan.FromMinutes(BreedStallMinutes) - (DateTime.UtcNow - breedSitStart);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public float BreedStallProgress => breedSitAt == null ? 0f : Math.Clamp((float)((DateTime.UtcNow - breedSitStart).TotalMinutes / BreedStallMinutes), 0f, 1f);

    /// <summary>Why this cow can't use a breeding stall right now, or null.</summary>
    public string? BreedStallProblem()
    {
        if (!Networked) return "Breeding stalls need the Eorzea Milkers Network.";
        if (!IsCow) return "Breeding stalls are for cows.";
        if (Blocked() is { } blocked) return blocked;
        if (BreedCooldownLeft > TimeSpan.Zero) return $"You were bred recently: you can be bred again in {Wait(BreedCooldownLeft)}.";
        if (Where.AtStall is { Breed: true, Loaded: <= 0 }) return "This breeding stall has no breedings loaded. Ask a farmer or the owner to load it.";
        return null;
    }

    /// <summary>Why a farmer or the owner can't load this breeding stall now, or null.</summary>
    public string? LoadStallProblem(Stall s)
    {
        if (!Networked || !s.Breed) return "Only breeding stalls are loaded.";
        if (!CanCarry) return "Farmers and the owner load breeding stalls.";
        if (!Where.InDairy || Where.Guest) return "Be in your dairy to load it.";
        if (s.Loaded >= BreedStallMaxLoaded) return $"It's full: {BreedStallMaxLoaded} breedings loaded.";
        var seed = Net.Progress?.Tank?.Seed ?? 0;
        if (seed < BreedStallSeedOz - 0.05f) return $"The seed tank has {Units.Oz(seed, "0.#")}; a breeding needs {Units.Oz(BreedStallSeedOz)}. It needs a bull, or seed from the Seed market.";
        return null;
    }

    /// <summary>A farmer or the owner loads one breeding (3 oz from the seed tank) into a breeding stall.</summary>
    public void LoadBreedingStall(Stall s)
    {
        if (LoadStallProblem(s) is { } why)
        {
            Fail(why);
            return;
        }

        Net.LoadBreedingStall(s.Id, r =>
        {
            s.Loaded = r.Loaded;
            AddFarmerXp(BreedStallLoadXp);
            Say($"Loaded {s.Name}: {r.Loaded} breeding{(r.Loaded == 1 ? "" : "s")} ready. The seed tank has {Units.Oz(r.Tank, "0.#")} left. +{BreedStallLoadXp} farmer xp.", 1);
        }, e => Fail(e));
    }

    public void UnloadBreedingStall(Stall s) =>
        Net.UnloadBreedingStall(s.Id, r =>
        {
            s.Loaded = 0;
            Say($"Unloaded {s.Name}: {Units.Oz(r.Returned, "0.#")} of seed went back into the tank.", 1);
        }, e => Fail(e));

    private void BreedStallUpdate()
    {
        var st = Where.AtStall;
        if (st == null || !st.Breed || !Where.Seated)
        {
            if (breedSitAt != null && !breedAsking)
            {
                Say("You got up before the breeding stall finished.", 2);
                breedSitAt = null;
            }

            if (st == null) breedWarnedFor = "";
            return;
        }

        if (breedAsking) return;
        if (breedSitAt != st)
        {
            if (BreedStallProblem() is { } why)
            {
                // Said once per sit, not every frame
                if (breedWarnedFor != st.Id) Fail(why);
                breedWarnedFor = st.Id;
                breedSitAt = null;
                return;
            }

            breedSitAt = st;
            breedSitStart = DateTime.UtcNow;
            breedWarnedFor = st.Id;
            Say($"You settle into {st.Name}. Stay seated for {BreedStallMinutes} minutes to be bred.", 0);
            return;
        }

        if (DateTime.UtcNow - breedSitStart < TimeSpan.FromMinutes(BreedStallMinutes)) return;
        breedAsking = true;
        Net.BreedAtStall(st.Id, r =>
        {
            breedAsking = false;
            breedSitAt = null;
            F.BredAtUtc = DateTime.UtcNow;
            F.BredUntilUtc = DateTime.UtcNow.AddMinutes(BredMinutesNow);
            F.BredStrength = 1f;
            F.BredBy = r.Bull.Length > 0 ? $"{r.Bull} (breeding stall)" : "the breeding stall";
            st.Loaded = r.Loaded;
            Diag.Info("breed", $"Bred at breeding stall {st.Name} ({r.Oz} oz seed, {r.Loaded} breedings left in it)");
            Say($"The breeding stall bred you{(r.Bull.Length > 0 ? $" with {r.Bull}'s seed" : "")}: +{BredBonusNow * 100:0.#}% milk for {BredMinutesNow} minutes. " +
                "(A stall doesn't get you pregnant: that's only from a scene.)", 1);
            plugin.SaveConfig();
        }, e =>
        {
            breedAsking = false;
            breedSitAt = null;
            Fail(e);
        });
    }
}
