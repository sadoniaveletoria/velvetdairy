using System.Linq;
using System.Collections.Generic;
using System;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace VelvetDairy.Game;

/// <summary>
/// Reads the Corruption plugin (github.com/RaylaPetal/corruption) through its public plugin API (version 1), for bulls
/// and breeding: who your scene partner is, when a partner or you climax, and your pregnancy. Eorzea Milkers never asks
/// Corruption to change anything (no Breed, arousal or climax calls): pregnancy only happens Corruption's own way.
/// Everything is called on the framework thread; while Corruption isn't installed every read returns its empty value.
/// </summary>
public sealed class CorruptionLink : IDisposable
{
    public sealed record Scene(bool Ready, string State, bool Solo, string PartnerName, string PartnerWorld);

    /// <summary>Arousal as Corruption shows it (0 to 100), refractory seconds left, and whether it's held at 100.</summary>
    public sealed record Arousal(bool Ready, int Value, double RefractorySeconds, bool Edging);

    public static readonly Scene NoScene = new(false, "idle", false, "", "");
    public static readonly Arousal NoArousal = new(false, 0, 0, false);

    /// <summary>Stage is none, first, second, third or recovering.</summary>
    /// <summary>
    /// Your pregnancy as Corruption shows it (API 2: it reads "none" until it's confirmed by a test or shows at day 5),
    /// with today's craving and your totals: times conceived, births, and children you've fathered as a breeder.
    /// </summary>
    public sealed record Pregnancy(bool Ready, string Stage, double DaysPregnant, DateTime ConceivedUtc, DateTime DueUtc, string FatherName, string FatherWorld,
        DateTime RecoveryUntilUtc, string Craving = "", bool CravingMet = false, int Conceptions = 0, int Births = 0, int Fathered = 0);

    public static readonly Pregnancy NoPregnancy = new(false, "none", 0, default, default, "", "", default);

    /// <summary>One past birth: when, and who the father was.</summary>
    public sealed record Birth(DateTime BornUtc, DateTime ConceivedUtc, string FatherName, string FatherWorld);

    private readonly ICallGateSubscriber<int> apiVersion;
    private readonly ICallGateSubscriber<(bool, string, bool, string, string, int, double)> getScene;
    private readonly ICallGateSubscriber<(bool, int, double, bool, string)> getArousal;
    private readonly ICallGateSubscriber<string, string, double> breedChanceV1;          // Corruption API 1
    private readonly ICallGateSubscriber<string, string, double, double> breedChance;    // API 2 adds a chance factor
    private readonly ICallGateSubscriber<(bool, string, double, DateTime, DateTime, string, string, DateTime, string, bool, bool, int, int, int)> getPregnancy;
    private readonly ICallGateSubscriber<List<(DateTime, DateTime, string, string)>> getBirths;
    private readonly ICallGateSubscriber<object> onClimax;
    private readonly ICallGateSubscriber<string, string, object> onPartnerClimax;
    private readonly ICallGateSubscriber<object> onStateChanged;
    private readonly ICallGateSubscriber<object> onReady;
    private readonly ICallGateSubscriber<string, bool, string> setForceCreampie;

    /// <summary>Corruption (re)loaded: anything we set there (forced creampies) has to be set again.</summary>
    public event Action? Reloaded;

    /// <summary>You climaxed (Corruption).</summary>
    public event Action? Climax;

    /// <summary>Your scene partner's climax was credited (Corruption): name, world.</summary>
    public event Action<string, string>? PartnerClimax;

    /// <summary>Something Corruption reports changed (pregnancy stage and the like), or Corruption (re)loaded.</summary>
    public event Action? Changed;

    public CorruptionLink()
    {
        var pi = Plugin.PluginInterface;
        apiVersion = pi.GetIpcSubscriber<int>("Corruption.ApiVersion");
        getScene = pi.GetIpcSubscriber<(bool, string, bool, string, string, int, double)>("Corruption.GetScene");
        getArousal = pi.GetIpcSubscriber<(bool, int, double, bool, string)>("Corruption.GetArousal");
        breedChanceV1 = pi.GetIpcSubscriber<string, string, double>("Corruption.BreedChance");
        breedChance = pi.GetIpcSubscriber<string, string, double, double>("Corruption.BreedChance");
        getPregnancy = pi.GetIpcSubscriber<(bool, string, double, DateTime, DateTime, string, string, DateTime, string, bool, bool, int, int, int)>("Corruption.GetPregnancy");
        getBirths = pi.GetIpcSubscriber<List<(DateTime, DateTime, string, string)>>("Corruption.GetBirths");
        onClimax = pi.GetIpcSubscriber<object>("Corruption.OnClimax");
        onPartnerClimax = pi.GetIpcSubscriber<string, string, object>("Corruption.OnPartnerClimax");
        onStateChanged = pi.GetIpcSubscriber<object>("Corruption.OnStateChanged");
        onReady = pi.GetIpcSubscriber<object>("Corruption.Ready");
        setForceCreampie = pi.GetIpcSubscriber<string, bool, string>("Corruption.SetForceCreampie");
        onClimax.Subscribe(OnClimax);
        onPartnerClimax.Subscribe(OnPartnerClimax);
        onStateChanged.Subscribe(OnChanged);
        onReady.Subscribe(OnChanged);
        onReady.Subscribe(OnReloaded);
    }

    private void OnReloaded() => Reloaded?.Invoke();

    /// <summary>
    /// Turns Corruption's forced creampies on or off for this character (a partner's climax creampies your pussy, so a
    /// breeder's climax rolls for conception). Corruption only allows it with the player's consent in its own settings
    /// ("not-allowed" otherwise), and keeps Cummy's blocks. Returns Corruption's answer, or "" if it couldn't be reached.
    /// </summary>
    public string SetForceCreampie(bool on) => Try(() => setForceCreampie.InvokeFunc("Eorzea Milkers", on) ?? "", "");

    public void Dispose()
    {
        onClimax.Unsubscribe(OnClimax);
        onPartnerClimax.Unsubscribe(OnPartnerClimax);
        onStateChanged.Unsubscribe(OnChanged);
        onReady.Unsubscribe(OnChanged);
        onReady.Unsubscribe(OnReloaded);
    }

    private void OnClimax() => Climax?.Invoke();
    private void OnPartnerClimax(string name, string world) => PartnerClimax?.Invoke(name, world);
    private void OnChanged() => Changed?.Invoke();

    private static T Try<T>(Func<T> call, T empty)
    {
        try { return call(); }
        catch (IpcNotReadyError) { return empty; }
        catch (IpcTypeMismatchError) { return empty; }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, "Eorzea Milkers: Corruption call failed");
            return empty;
        }
    }

    /// <summary>Corruption is installed with API version 1 or later.</summary>
    public bool Available => Try(() => apiVersion.InvokeFunc(), 0) >= 1;

    public Scene GetScene() => Try(() =>
    {
        var s = getScene.InvokeFunc();
        return new Scene(s.Item1, s.Item2, s.Item3, s.Item4, s.Item5);
    }, NoScene);

    /// <summary>The chance (0 to 1) a breeding by that player would have right now, or 0 when it would be refused. Read only.</summary>
    public int ApiVersion => Try(() => apiVersion.InvokeFunc(), 0);

    /// <summary>The chance (0 to 1) a breeding by that player would have now (factor 1), or 0 when it would be refused. Works with Corruption API 1 and 2.</summary>
    public double BreedChance(string name, string world) => ApiVersion >= 2
        ? Try(() => breedChance.InvokeFunc(name, world, 1.0), 0)
        : Try(() => breedChanceV1.InvokeFunc(name, world), 0);

    public Arousal GetArousal() => Try(() =>
    {
        var a = getArousal.InvokeFunc();
        return new Arousal(a.Item1, a.Item2, a.Item3, a.Item4);
    }, NoArousal);

    public Pregnancy GetPregnancy() => Try(() =>
    {
        var p = getPregnancy.InvokeFunc();
        return new Pregnancy(p.Item1, string.IsNullOrEmpty(p.Item2) ? "none" : p.Item2, p.Item3, p.Item4, p.Item5, p.Item6 ?? "", p.Item7 ?? "", p.Item8,
            p.Item9 ?? "", p.Item10, p.Item12, p.Item13, p.Item14);
    }, NoPregnancy);

    /// <summary>Your past births, oldest first, each with its father.</summary>
    public List<Birth> GetBirths() => Try(() => getBirths.InvokeFunc().Select(b => new Birth(b.Item1, b.Item2, b.Item3 ?? "", b.Item4 ?? "")).ToList(), new List<Birth>());
}
