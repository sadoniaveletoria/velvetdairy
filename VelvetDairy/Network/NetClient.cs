using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VelvetDairy.Game;

namespace VelvetDairy.Network;

/// <summary>
/// Talks to the Eorzea Milkers Network server: presence, the dairy directory, your dairy's members and stalls,
/// invites, buy orders, deliveries and the inbox. Requests run in the background; their results are applied
/// on the game's thread in <see cref="Update"/>.
///
/// The server's address comes from network.json in the GitHub repo (written when the server deploys), so the
/// server can move without a plugin update. Until that file exists the network is simply off and the game runs locally.
/// </summary>
public sealed class NetClient : IDisposable
{
    private const string NetworkFileUrl = Channel.NetworkFileUrl;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Plugin plugin;
    // Connections the server (or something on the way) has quietly closed are dropped after a short idle time
    // instead of being reused and failing with "forcibly closed by the remote host"
    private readonly HttpClient http = new(new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    }) { Timeout = TimeSpan.FromSeconds(12) };
    private readonly ConcurrentQueue<Action> mainThread = new();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    private double nextResolve, nextBeat, nextMine, nextDirectory;
    private bool beating, claiming;
    private readonly HashSet<long> announcedInvites = new();

    private FarmSave F => plugin.Config.Farm;
    private double Now => clock.Elapsed.TotalSeconds;

    // ----- What the rest of the plugin reads -----
    public string? Api { get; private set; }
    public bool Connected { get; private set; }          // last request reached the server
    public string Status { get; private set; } = "Not connected.";
    public HeartbeatReply? Last { get; private set; }
    public MyDairyReply? Mine { get; private set; }
    public DirectoryReply? Directory { get; private set; }
    public List<BuyOrder> MyOrders { get; private set; } = new();
    public Dictionary<string, List<BuyOrder>> OrdersByDairy { get; } = new();
    public bool WantDirectory { get; set; }              // the Dairies tab is open

    /// <summary>The network is in use: we know the server and it has answered at least once.</summary>
    public bool Active => Api != null && Last != null;
    public MembershipInfo? Membership => Last?.Membership;

    public NetClient(Plugin plugin)
    {
        this.plugin = plugin;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("VelvetDairy-Plugin");
        if (string.IsNullOrEmpty(F.NetId) || string.IsNullOrEmpty(F.NetKey))
        {
            F.NetId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            F.NetKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            plugin.SaveConfig();
        }
    }

    public void Dispose() => http.Dispose();

    /// <summary>SHA-256 of "name@homeWorldId": how invites find a character without the server storing who you are.</summary>
    public static string CharHash(string name, uint homeWorld) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{name.Trim().ToLowerInvariant()}@{homeWorld}"))).ToLowerInvariant();

    public string? MyCharHash()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        return me == null ? null : CharHash(me.Name.TextValue, me.HomeWorld.RowId);
    }

    // =================================================================
    // Frame update
    // =================================================================

    public void Update()
    {
        Pump();

        if (Now >= nextUpdateCheck) CheckForUpdate();
        if (!F.NetEnabled || !F.Confirmed18 || Plugin.ObjectTable.LocalPlayer == null) return;
        var t = Now;

        if (Api == null)
        {
            if (t >= nextResolve) Resolve();
            return;
        }

        // A new recovery code was being made when the game closed: find out which key the server has before anything else
        if (F.PendingNetKey.Length > 0)
        {
            if (!checkingPending) CheckPendingKey();
            return;
        }

        if (t >= nextBeat && !beating) Heartbeat();
        if (Last != null && t >= nextBackup) Backup();
        if (Membership != null && t >= nextMine) RefreshMine();
        if (WantDirectory && t >= nextDirectory) RefreshDirectory();
        WantDirectory = false;   // the Dairies tab sets it again each frame it's open

        // Standing in someone else's dairy: keep its buy orders fresh
        if (Last?.Here is { Mine: false } here && t >= nextVisit)
        {
            nextVisit = t + 45;
            RefreshOrders(here.Id);
        }
    }

    /// <summary>Runs the replies that came back, on the game's thread.</summary>
    public void Pump()
    {
        while (mainThread.TryDequeue(out var a))
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "Eorzea Milkers network callback failed");
            }
        }
    }

    /// <summary>
    /// Asks the server which character a save from before per-character saves last played as (a hash of name and
    /// home world, or null if it never connected, the network is off, or the server can't be reached in time).
    /// </summary>
    public void LastCharacterOf(FarmSave old, Action<string?> done)
    {
        if (!old.NetEnabled || string.IsNullOrEmpty(old.NetId) || string.IsNullOrEmpty(old.NetKey))
        {
            done(null);
            return;
        }

        var api = Api;
        if (api == null && plugin.Dev && !string.IsNullOrWhiteSpace(old.NetServerOverride)) api = old.NetServerOverride.Trim().TrimEnd('/');
        var id = old.NetId;
        var key = old.NetKey;
        Task.Run(async () =>
        {
            string? hash = null;
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                if (api == null)
                {
                    var text = await http.GetStringAsync(NetworkFileUrl + "?t=" + DateTime.UtcNow.Ticks, cts.Token);
                    api = JsonSerializer.Deserialize<NetworkFile>(text, Json)?.Api?.Trim().TrimEnd('/');
                    if (api == null || !api.StartsWith("https://")) api = null;
                }

                if (api != null)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, api + "/v1/whoami");
                    req.Headers.Add("X-VD-Id", id);
                    req.Headers.Add("X-VD-Key", key);
                    using var res = await http.SendAsync(req, cts.Token);
                    if (res.IsSuccessStatusCode)
                        hash = JsonSerializer.Deserialize<WhoAmIReply>(await res.Content.ReadAsStringAsync(cts.Token), Json)?.CharHash;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Debug(e, "Eorzea Milkers couldn't ask whose the old save is");
            }

            mainThread.Enqueue(() => done(hash));
        });
    }

    private sealed class WhoAmIReply
    {
        public string? CharHash { get; set; }
    }

    private double nextVisit;

    private void Resolve()
    {
        nextResolve = Now + 30 * 60;
        if (plugin.Dev && !string.IsNullOrWhiteSpace(F.NetServerOverride))
        {
            Api = F.NetServerOverride.Trim().TrimEnd('/');
            nextBeat = 0;
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var text = await http.GetStringAsync(NetworkFileUrl + "?t=" + DateTime.UtcNow.Ticks);
                var file = JsonSerializer.Deserialize<NetworkFile>(text, Json);
                var api = file?.Api?.Trim().TrimEnd('/');
                Diag.Info("net", string.IsNullOrEmpty(api) ? "network.json has no server" : $"Server found: {api}");
                if (!string.IsNullOrEmpty(api) && api.StartsWith("https://"))
                    mainThread.Enqueue(() =>
                    {
                        Api = api;
                        nextBeat = 0;
                        Status = "Connecting...";
                    });
            }
            catch (Exception e)
            {
                Diag.Warn("net", "Couldn't read network.json", e);
                mainThread.Enqueue(() => Status = "The Eorzea Milkers network isn't set up yet. Playing locally.");
            }
        });
    }

    // ----- Update check: the repo's repo.json says which version is released -----

    private const string RepoFileUrl = Channel.RepoFileUrl;
    private double nextUpdateCheck;

    /// <summary>The newest released version, when it's newer than this one.</summary>
    public string? UpdateVersion { get; private set; }

    private void CheckForUpdate()
    {
        nextUpdateCheck = Now + 30 * 60;
        Task.Run(async () =>
        {
            try
            {
                var text = await http.GetStringAsync(RepoFileUrl + "?t=" + DateTime.UtcNow.Ticks);
                using var doc = JsonDocument.Parse(text);
                var latest = doc.RootElement[0].GetProperty("AssemblyVersion").GetString();
                if (System.Version.TryParse(latest, out var lv) && System.Version.TryParse(Plugin.Version, out var mine) && lv > mine)
                    mainThread.Enqueue(() => UpdateVersion = latest);
            }
            catch
            {
                // No update info right now; try again later
            }
        });
    }

    /// <summary>Forces the next heartbeat and dairy refresh to happen right away.</summary>
    /// <summary>Makes the next heartbeat (and inbox check) happen within this many seconds.</summary>
    public void BeatWithin(double seconds)
    {
        if (nextBeat > Now + seconds) nextBeat = Now + seconds;
    }

    public void Poke()
    {
        nextBeat = 0;
        nextMine = 0;
        nextDirectory = 0;
    }

    // =================================================================
    // Calls
    // =================================================================

    private void Call<T>(string method, string path, object? body, Action<T> ok, Action<string>? failed = null, bool dev = false,
                         string? asId = null, string? asKey = null) where T : class
    {
        var devPhrase = dev ? F.DevKey : "";
        var api = Api;
        if (api == null)
        {
            failed?.Invoke("The Eorzea Milkers network isn't available.");
            return;
        }

        // A recovery code signs in as the old install for one request
        var id = asId ?? F.NetId;
        var key = asKey ?? F.NetKey;
        Task.Run(async () =>
        {
            try
            {
                HttpRequestMessage Make()
                {
                    var r = new HttpRequestMessage(new HttpMethod(method), api + path);
                    r.Headers.Add("X-VD-Id", id);
                    r.Headers.Add("X-VD-Key", key);
                    if (devPhrase.Length > 0) r.Headers.Add("X-VD-Dev", devPhrase);
                    if (body != null) r.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
                    return r;
                }

                HttpResponseMessage res;
                try
                {
                    using var req = Make();
                    res = await http.SendAsync(req);
                }
                catch (HttpRequestException e) when (method == "GET" || path == "/v1/heartbeat")
                {
                    // A dropped connection: try once more on a fresh one (only for calls that are safe to repeat)
                    Diag.Info("net", $"{method} {path}: connection dropped ({Why(e)}), trying again");
                    using var again = Make();
                    res = await http.SendAsync(again);
                }

                using var _ = res;
                var text = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    var value = JsonSerializer.Deserialize<T>(text, Json)!;
                    mainThread.Enqueue(() =>
                    {
                        Connected = true;
                        ok(value);
                    });
                }
                else
                {
                    string msg;
                    try
                    {
                        msg = JsonSerializer.Deserialize<ErrorReply>(text, Json)?.Error ?? res.ReasonPhrase ?? "Error";
                    }
                    catch
                    {
                        msg = $"Server error ({(int)res.StatusCode}).";
                    }

                    Diag.Warn("net", $"{method} {path} -> {(int)res.StatusCode} {msg}");
                    mainThread.Enqueue(() =>
                    {
                        Connected = true;
                        failed?.Invoke(msg);
                    });
                }
            }
            catch (Exception e)
            {
                Diag.Warn("net", $"{method} {path} failed: {Why(e)}");
                mainThread.Enqueue(() =>
                {
                    Connected = false;
                    Status = "Can't reach the Eorzea Milkers network right now.";
                    failed?.Invoke("Can't reach the Eorzea Milkers network right now.");
                });
            }
        });
    }

    private void Toast(string text, int kind) => plugin.Game.Notify(text, kind);

    // ----- Cloud backup and restore -----
    // Every few minutes the save goes to the server (without the network key or developer phrase), so a rebuilt PC or a
    // lost settings file doesn't lose anything. A new install gets it back with the recovery code, or by putting a code
    // in the character's Lodestone profile. Dev builds never upload.

    private double nextBackup = 90;
    public DateTime? LastBackupUtc { get; private set; }
    public string BackupNote { get; private set; } = "";

    /// <summary>Back up now (after a restore, or the Back up now button).</summary>
    public void BackupSoon(double seconds = 3) => nextBackup = Math.Min(nextBackup, Now + seconds);

    private void Backup(bool now = false)
    {
        nextBackup = Now + 300;
        if (!plugin.CloudBackupAllowed) return;
        var hash = MyCharHash();
        if (hash == null) return;
        plugin.SaveConfig();   // sealed with the newest progress first
        string text;
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(F))!.AsObject();
            node.Remove("NetKey");
            node.Remove("DevKey");
            text = node.ToJsonString();
        }
        catch (Exception e)
        {
            Diag.Warn("backup", "Couldn't prepare the cloud backup", e);
            return;
        }

        Call<BackupPutReply>("POST", "/v1/backup", new { charHash = hash, save = text, now }, r =>
        {
            if (r.Skipped) return;
            LastBackupUtc = DateTime.UtcNow;
            BackupNote = r.Kept ? "The server kept your older backup: it has more progress than this save." : "";
            if (r.Kept) Diag.Info("backup", "Server kept the backup with more progress");
        }, e => BackupNote = e);
    }

    public void BackupNow() => Backup(true);

    private bool checkingPending;

    /// <summary>
    /// A new recovery code: a new key for this install, so the old code stops working. The new key is saved as pending
    /// first, so if the game closes before the server answers, the next start finds out which key the server kept.
    /// </summary>
    public void NewRecoveryCode(Action ok, Action<string> failed)
    {
        var next = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        F.PendingNetKey = next;
        plugin.SaveConfig();
        Call<OkReply>("POST", "/v1/rekey", new { newKey = next }, _ =>
        {
            F.NetKey = next;
            F.PendingNetKey = "";
            plugin.SaveConfig();
            Diag.Info("backup", "Made a new recovery code");
            ok();
        }, e =>
        {
            // It may still have gone through: check which key works
            CheckPendingKey();
            failed(e);
        });
    }

    private void CheckPendingKey()
    {
        if (F.PendingNetKey.Length == 0 || checkingPending) return;
        checkingPending = true;
        var pending = F.PendingNetKey;
        Call<BackupReply>("GET", "/v1/backup", null, _ =>
        {
            checkingPending = false;
            F.NetKey = pending;
            F.PendingNetKey = "";
            plugin.SaveConfig();
            Diag.Info("backup", "The new recovery code was kept by the server");
        }, e =>
        {
            checkingPending = false;
            if (!e.Contains("key", StringComparison.OrdinalIgnoreCase)) return;   // offline: try again later
            F.PendingNetKey = "";
            plugin.SaveConfig();
            Diag.Info("backup", "The new recovery code didn't reach the server: kept the old one");
        }, asKey: pending);
    }

    /// <summary>A recovery code: this install's id and key in one string, to sign in as it again on a new install.</summary>
    public string RecoveryCode()
    {
        try
        {
            var bytes = Convert.FromHexString(F.NetId).Concat(Convert.FromHexString(F.NetKey)).ToArray();
            var b32 = Base32(bytes);
            return "EMR1-" + string.Join("-", Enumerable.Range(0, (b32.Length + 4) / 5).Select(i => b32.Substring(i * 5, Math.Min(5, b32.Length - i * 5))));
        }
        catch
        {
            return "";
        }
    }

    public static bool TryReadRecoveryCode(string code, out string id, out string key)
    {
        id = key = "";
        var s = new string(code.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (!s.StartsWith("EMR1")) return false;
        var bytes = FromBase32(s[4..].Replace('O', '0').Replace('I', '1').Replace('L', '1'));
        if (bytes == null || bytes.Length != 48) return false;
        id = Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
        key = Convert.ToHexString(bytes, 16, 32).ToLowerInvariant();
        return true;
    }

    private const string B32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";   // Crockford: no I, L, O or U

    private static string Base32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(B32[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0) sb.Append(B32[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[]? FromBase32(string s)
    {
        var out_ = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            var v = B32.IndexOf(c);
            if (v < 0) return null;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                out_.Add((byte)((buffer >> (bits - 8)) & 255));
                bits -= 8;
            }
        }

        return out_.ToArray();
    }

    public void GetBackupAs(string id, string key, Action<BackupReply> ok, Action<string> failed) =>
        Call("GET", "/v1/backup", null, ok, failed, asId: id, asKey: key);

    public void GetMyBackup(Action<BackupReply> ok, Action<string> failed) => Call("GET", "/v1/backup", null, ok, failed);

    // ----- Breeding stalls and the seed market -----

    public void SetBreedingStall(string stallId, bool on, Action ok, Action<string> failed) =>
        Call<OkReply>("POST", $"/v1/stalls/{stallId}/breeding", new { on }, _ =>
        {
            nextMine = 0;
            ok();
        }, failed);

    public void BreedAtStall(string stallId, Action<BreedStallReply> ok, Action<string> failed) =>
        Call<BreedStallReply>("POST", $"/v1/stalls/{stallId}/breed", null, r =>
        {
            nextMine = 0;
            ok(r);
        }, failed);

    public void LoadBreedingStall(string stallId, Action<StallLoadReply> ok, Action<string> failed) =>
        Call<StallLoadReply>("POST", $"/v1/stalls/{stallId}/load", null, r => { nextMine = 0; ok(r); }, failed);

    public void UnloadBreedingStall(string stallId, Action<StallLoadReply> ok, Action<string> failed) =>
        Call<StallLoadReply>("POST", $"/v1/stalls/{stallId}/unload", null, r => { nextMine = 0; ok(r); }, failed);

    public SeedMarketReply? SeedMarket { get; private set; }
    private double seedMarketAt = -100;

    /// <summary>The seed for sale (at most every 20 seconds unless forced).</summary>
    public void RefreshSeedMarket(bool force = false)
    {
        if (!force && Now - seedMarketAt < 20) return;
        seedMarketAt = Now;
        Call<SeedMarketReply>("GET", "/v1/seed/market", null, r => SeedMarket = r, _ => { });
    }

    public void SellSeed(int oz, int price, Action ok, Action<string> failed) =>
        Call<OkReply>("POST", "/v1/seed/sell", new { oz, price }, _ => { RefreshSeedMarket(true); nextMine = 0; ok(); }, failed);

    public void BuySeed(long id, int oz, Action<SeedBuyReply> ok, Action<string> failed) =>
        Call<SeedBuyReply>("POST", $"/v1/seed/sales/{id}/buy", new { oz }, r => { RefreshSeedMarket(true); nextMine = 0; ok(r); }, failed);

    public void CloseSeedSale(long id, Action ok, Action<string> failed) =>
        Call<OkReply>("POST", $"/v1/seed/sales/{id}/close", null, _ => { RefreshSeedMarket(true); nextMine = 0; ok(); }, failed);

    public void RestoreAvailable(Action<RestoreAvailableReply> ok) => Call<RestoreAvailableReply>("GET", "/v1/restore/available", null, ok, _ => { });

    public void RestoreStart(string name, string world, Action<RestoreStartReply> ok, Action<string> failed) =>
        Call("POST", "/v1/restore/start", new { name, world }, ok, failed);

    /// <summary>A restore key from the Discord walkthrough (MILKR-...).</summary>
    public static bool LooksLikeRestoreKey(string code)
    {
        var s = new string(code.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return s.StartsWith("MILKR") && s.Length == 25;
    }

    public void RestoreRedeem(string key, string name, string world, uint worldId, Action<RestoreCheckReply> ok, Action<string> failed) =>
        Call("POST", "/v1/restore/redeem", new { key, name, world, worldId }, ok, failed);

    public void RestoreCheck(string name, string world, uint worldId, string code, Action<RestoreCheckReply> ok, Action<string> failed) =>
        Call("POST", "/v1/restore/check", new { name, world, worldId, code }, ok, failed);

    /// <summary>The innermost reason a request failed, in one line (no stack trace, so support info stays readable).</summary>
    private static string Why(Exception e)
    {
        while (e.InnerException != null) e = e.InnerException;
        return e is TaskCanceledException ? "timed out" : e.Message.TrimEnd('.');
    }

    private void Heartbeat()
    {
        beating = true;
        nextBeat = Now + 60;
        var here = Estate.Current();
        var g = plugin.Game;
        var body = new
        {
            version = Plugin.Version,
            charHash = MyCharHash(),
            // So a name change or world transfer shows up on your dairy's roster
            name = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "",
            worldName = Plugin.ObjectTable.LocalPlayer?.HomeWorld.ValueNullable?.Name.ExtractText() ?? "",
            houseId = here?.HouseId.ToString(),
            pos = g.Position(),
            // Held until you're awake: the server only counts them then
            dairyXp = g.Wake == DairyGame.Wakefulness.Awake ? g.TakeDairyXp() : 0,
            noticeAfter = lastNotice,
            pumped = g.Wake == DairyGame.Wakefulness.Awake ? g.TakePumped() : null,
            seedPumped = g.Wake == DairyGame.Wakefulness.Awake ? g.TakeSeedPumped() : 0,
            atCreamery = g.AtCreamery,
            creamerySlot = g.CreameryHere?.Slot ?? 0,
            awake = g.Wake == DairyGame.Wakefulness.Awake,
            status = new MemberStatus
            {
                Milk = (float)Math.Round(F.Cow.Milk, 1), Cap = g.Capacity, Grade = g.CurrentGrade, Spoiled = g.Spoiled,
                InDairy = g.Where.InDairy, Level = g.Level, FarmerLevel = g.FarmerLevel, BullLevel = g.BullLevel, Crown = g.Crowned,
                Seed = g.IsBull ? g.SeedNow : 0, Preg = g.Pregnant ? g.Preg.Stage : "", PregFather = g.Pregnant ? g.Preg.FatherName : "",
                BreedOk = g.BreedOkFor, GuestAt = g.Where.Guest ? g.GuestHere?.Id ?? "" : "",
                SeedOz = g.ProducesSeed ? (float)Math.Round(F.Cow.Seed, 2) : 0, SeedCap = g.ProducesSeed ? g.SeedCapacity : 0,
                SeedGrade = g.ProducesSeed ? g.SeedGrade : 0, SeedSpoiled = g.ProducesSeed && g.SeedSpoiled,
                Breeding = g.BreedingTarget,
                StudTonic = g.IsBull && F.StudTonic,
                Bonuses = g.ScanBonuses(out var speed), Speed = speed,
                BredBy = g.BredLeft > TimeSpan.Zero ? F.BredBy : "", BredPct = g.BredLeft > TimeSpan.Zero ? (float)Math.Round(g.BredBonusNow * 100, 1) : 0,
                BredUntil = g.BredLeft > TimeSpan.Zero ? new DateTimeOffset(F.BredUntilUtc, TimeSpan.Zero).ToUnixTimeSeconds() : 0,
                BreedWait = g.IsCow && g.BreedCooldownLeft > TimeSpan.Zero ? DateTimeOffset.UtcNow.Add(g.BreedCooldownLeft).ToUnixTimeSeconds() : 0,
                Scan = F.ScanAllow,
                Needs = g.NeedMinutesNow.ToList(),
                NeedPct = new List<float> { F.Cow.FedBonus, F.Cow.WaterBonus, F.Cow.ComfortBonus, F.Cow.HappyBonus },
                Quality = (int)F.Quality, TotalOz = (int)F.Stats.TotalOz,
                Emotes = g.Producer ? g.MilkEmotesForFarmers() : new List<string>(),
                HandBy = g.Active is { Kind: VelvetDairy.Game.SessionKind.Hand, Hand: { IsTest: false } h } ? h.Name : "",
                AnyEmote = F.AnyLoopEmote,
                Milking = g.MilkingCowName ?? "",
                Ask = g.Producer && (!F.AutoHand || g.Where.Guest),
                Refused = g.RefusedFarmer,
                TreatAt = g.Producer && F.Cow.HappyReadyUtc > DateTime.UtcNow ? new DateTimeOffset(DateTime.SpecifyKind(F.Cow.HappyReadyUtc, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0,
            },
        };
        Call<HeartbeatReply>("POST", "/v1/heartbeat", body, r =>
        {
            beating = false;
            var before = Last?.Membership?.DairyId;
            Last = r;
            Status = $"{r.Online} online";

            // Notices to your dairy (one pop-up each). The first check-in only catches up, so logging in doesn't replay old ones.
            foreach (var n in r.Notices.Where(n => n.Id > lastNotice))
            {
                if (lastNotice >= 0) plugin.Game.OnNotice(n);
                lastNotice = Math.Max(lastNotice, n.Id);
            }

            if (lastNotice < 0) lastNotice = 0;
            plugin.Game.OnMembership(r.Membership);
            if (r.Membership?.DairyId != before) nextMine = 0;

            foreach (var inv in r.Invites.Where(i => announcedInvites.Add(i.Id)))
            {
                var text = $"You're invited to join {inv.Dairy} as a {inv.Role}. Answer on the Dairy tab.";
                Toast(text, 1);
                Plugin.ChatGui.Print(text, "Eorzea Milkers");
            }

            // New applications for whoever can answer them
            if (r.Applications > announcedApplications)
            {
                var text = r.Applications == 1 ? "Someone applied to join your dairy. Answer in Members > Applications." : $"{r.Applications} applications to your dairy are waiting. Answer in Members > Applications.";
                Toast(text, 1);
                Plugin.ChatGui.Print(text, "Eorzea Milkers");
                if (plugin.Members.IsOpen) RefreshApplications(true);
            }

            announcedApplications = r.Applications;
            if (r.Inbox > 0) Claim();
        }, _ => beating = false);
    }

    private void Claim()
    {
        if (claiming) return;
        claiming = true;
        Call<InboxReply>("POST", "/v1/inbox/claim", null, r =>
        {
            claiming = false;
            plugin.Game.ApplyInbox(r.Items);
        }, _ => claiming = false);
    }

    /// <summary>Makes the next dairy refresh happen within this many seconds (e.g. when a window that shows it opens).</summary>
    public void MineWithin(double seconds)
    {
        if (nextMine > Now + seconds) nextMine = Now + seconds;
    }

    /// <summary>
    /// Your dairy's members, stalls and status only change what you see while you're in the dairy, have one of the
    /// windows open. Otherwise it's checked every 5 minutes instead of every minute
    /// (every request counts against the server's daily allowance).
    /// </summary>
    private bool WatchingDairy =>
        plugin.Game.Where.InDairy || plugin.MainWindow.IsOpen || plugin.Members.IsOpen || plugin.Kitchen.IsOpen ||
        plugin.Milking.IsOpen;

    /// <summary>Scan another player (anyone targeting them can, if they allow it).</summary>
    /// <summary>Who's at a dairy right now.</summary>
    public void WhoIsHere(string dairyId, Action<HereReply> ok, Action<string> failed) =>
        Call<HereReply>("GET", $"/v1/dairies/{Uri.EscapeDataString(dairyId)}/details", null, ok, failed);

    public void Scan(string name, string world, Action<ScanReply> ok, Action<string> failed) =>
        Call<ScanReply>("GET", $"/v1/scan/{Uri.EscapeDataString(world)}/{Uri.EscapeDataString(name)}", null, ok, failed);

    /// <summary>Another player's roles, and whom they allowed to breed them (breeding across dairies).</summary>
    public void LookupPlayer(string name, string world, Action<PlayerLookup> done) =>
        Call<PlayerLookup>("GET", $"/v1/player?name={Uri.EscapeDataString(name)}&world={Uri.EscapeDataString(world)}", null, done, _ => done(new PlayerLookup()));

    private int mineSeq, mineApplied;

    public void RefreshMine()
    {
        // A scene where you and your partner choose who breeds: their choice shows up quickly
        nextMine = Now + (plugin.Game.InScene ? 10 : WatchingDairy ? 60 : 300);
        var seq = ++mineSeq;
        Call<MyDairyReply>("GET", "/v1/dairy/mine", null, r =>
        {
            // An older refresh that answers after a newer one was already used (say, from before you cleaned a
            // stall) is ignored. Only "already used": while refreshes are asked for every few seconds (a farmer
            // pressing Milk on you), skipping every reply that isn't the very latest meant none ever got through.
            if (seq < mineApplied) return;
            mineApplied = seq;
            Mine = r.Dairy == null ? null : r;
            plugin.Game.OnMyDairy(Mine);
            if (r.You?.IsOwner == true) RefreshMyOrders();
        });
    }

    public void RefreshDirectory()
    {
        nextDirectory = Now + 120;
        Call<DirectoryReply>("GET", "/v1/dairies", null, r => Directory = r);
        Call<LeaderboardReply>("GET", "/v1/leaderboard", null, r => Leaderboard = r);
    }

    /// <summary>This week's most active dairies.</summary>
    public LeaderboardReply? Leaderboard { get; private set; }

    /// <summary>Your dairy's level, XP and fund (from the last dairy refresh).</summary>
    public DairyProgress? Progress => Mine?.Progress;

    /// <summary>A farmer or owner fills a jug from the dairy's tank.</summary>
    public void TakeFromTank(int grade, int oz, Action<TakeReply> ok, Action<string> failed) =>
        Call<TakeReply>("POST", "/v1/dairy/tank/take", new { grade, oz }, r =>
        {
            Poke();
            ok(r);
        }, failed);

    /// <summary>Developer: removes one of the Grand Creameries.</summary>
    public void RemoveCreamery(int slot, Action ok, Action<string> failed) =>
        Call<object>("DELETE", $"/v1/dev/creamery/{slot}", null, _ =>
        {
            Poke();
            ok();
        }, failed, dev: true);

    /// <summary>Developer: puts a Grand Creamery where you're standing (for everyone).</summary>
    public void SetCreamery(object body, Action ok, Action<string> failed) =>
        Call<IdReply>("PUT", "/v1/dev/creamery", body, _ =>
        {
            Poke();
            ok();
        }, failed, dev: true);

    /// <summary>Milk used up locally (so it can't also go to a contract).</summary>
    public void UseCarried(float oz, bool order = false) =>
        Call<IdReply>("POST", "/v1/carry/use", new { oz = Math.Round(oz, 2), order }, _ => { if (order) MineWithin(5); }, e => Plugin.Log.Debug($"Carry use: {e}"));

    /// <summary>A farmer or the owner sets a member's cow quota (milk) or bull quota (seed) size.</summary>
    public void SetQuota(string memberId, float oz, bool bull, Action ok, Action<string> failed) =>
        Call<object>("PUT", $"/v1/members/{memberId}/quota", new { oz, bull }, _ => { nextMine = 0; ok(); }, failed);

    /// <summary>The owner unlocks a feed type for the dairy's feed bin with the fund.</summary>
    public void BuyFeedBin(string item, Action ok, Action<string> failed) =>
        Call<object>("POST", $"/v1/dairy/feedbin/{item}", null, _ => { nextMine = 0; ok(); }, failed);

    /// <summary>A farmer feeds a cow or bull from the dairy's feed bin.</summary>
    public void FeedBin(string item, string to, Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/feedbin/feed", new { item, to }, _ => ok(), failed);

    /// <summary>Dev server: the owner adds or removes the test bots (a cow, a farmer and a bull).</summary>
    public void SetBots(bool on, Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/dev/bots", new { on }, _ => { nextMine = 0; ok(); }, failed);

    /// <summary>Drops milk off at the other dairy you're standing in (no order needed): their tank gets it, the Grand Creamery pays you.</summary>
    public void DeliverHere(int oz, int level, Action<DropReply> ok, Action<string> failed) =>
        Call<DropReply>("POST", "/v1/deliver", new { oz, grade = 0, level }, r => { Poke(); ok(r); }, failed);

    /// <summary>Dev server: make (with an area) or remove your bot-run public test dairy, for testing visits.</summary>
    public void SetTestDairy(object body, Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/dev/testdairy", body, _ => { nextBeat = 0; nextDirectory = 0; ok(); }, failed);

    /// <summary>Dev server: add a stall to your test dairy where you're sitting.</summary>
    public void AddTestStall(float x, float y, float z, Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/dev/testdairy/stall", new { x, y, z, tier = 2 }, _ => { nextBeat = 0; ok(); }, failed);

    /// <summary>Dev server: hand milk a bot cow or bull (no emote: it's not in the game world).</summary>
    public void MilkBot(string id, Action<BotMilkReply> ok, Action<string> failed) =>
        Call<BotMilkReply>("POST", $"/v1/dev/bots/{id}/milk", null, r => { nextMine = 0; BeatWithin(1); ok(r); }, failed);

    /// <summary>The owner runs the Velvet pump (paid from the fund).</summary>
    public void StartPump(Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/dairy/pump", null, _ => { nextMine = 0; ok(); }, failed);

    /// <summary>Ships milk from your jugs to this week's contract.</summary>
    public void ShipToContract(int grade, int oz, Action<ShipReply> ok, Action<string> failed) =>
        Call<ShipReply>("POST", "/v1/dairy/contract/ship", new { grade, oz }, r =>
        {
            Poke();
            ok(r);
        }, failed);

    /// <summary>Tells the server a farmer hand milked you, so they get their share.</summary>
    public void HandPay(string farmer, string world, float oz, int grade, bool seed = false, bool prized = false) =>
        Call<HandPayReply>("POST", "/v1/handpay", new { farmer, world, oz = Math.Round(oz, 3), grade, seed, prized }, _ => { }, e => Plugin.Log.Debug($"Hand pay: {e}"));

    public void Donate(int coins, Action<int> ok, Action<string> failed) =>
        Call<FundReply>("POST", "/v1/dairy/fund", new { coins }, r =>
        {
            Poke();
            ok(r.Fund);
        }, failed);

    public void RefreshOrders(string dairyId) =>
        Call<OrdersReply>("GET", $"/v1/dairies/{dairyId}/orders", null, r => OrdersByDairy[dairyId] = r.Orders,
            e => OrdersByDairy[dairyId] = new List<BuyOrder>());

    /// <summary>Open buy orders from every other dairy (farmers fill them from their own dairy).</summary>
    public List<BuyOrder> Board { get; private set; } = new();
    private DateTime nextBoard = DateTime.MinValue;

    public void RefreshBoard(bool now = false)
    {
        if (!now && DateTime.UtcNow < nextBoard) return;
        nextBoard = DateTime.UtcNow.AddSeconds(60);
        Call<OrdersReply>("GET", "/v1/orders/board", null, r => Board = r.Orders);
    }

    public void RefreshMyOrders() => Call<OrdersReply>("GET", "/v1/orders/mine", null, r => MyOrders = r.Orders);

    // ----- Dairy -----

    public void SetPrivatePlace(VelvetDairy.Game.DairyArea area, Action ok, Action<string> failed) =>
        Call<object>("PUT", "/v1/dairy/private", new { area }, _ =>
        {
            RefreshMine();
            Poke();
            ok();
        }, failed);

    public void RemovePrivatePlace(Action ok, Action<string> failed) =>
        Call<object>("DELETE", "/v1/dairy/private", null, _ =>
        {
            RefreshMine();
            Poke();
            ok();
        }, failed);

    public void PutDairy(object body, Action<string> ok, Action<string> failed) =>
        Call<IdReply>("PUT", "/v1/dairy", body, r =>
        {
            Poke();
            ok(r.IdText);
        }, failed);

    public void CloseDairy(Action ok, Action<string> failed) =>
        Call<object>("DELETE", "/v1/dairy", null, _ =>
        {
            Mine = null;
            Poke();
            ok();
        }, failed);

    // ----- Invites and members -----

    public void Invite(string toName, uint toWorld, string role, Action ok, Action<string> failed) =>
        Call<IdReply>("POST", "/v1/invites", new { toHash = CharHash(toName, toWorld), toName, role }, _ =>
        {
            Poke();
            ok();
        }, failed);

    public void Accept(InviteInfo inv, Action<AcceptReply> ok, Action<string> failed)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        var body = new { charHash = MyCharHash(), name = me?.Name.TextValue ?? "", worldName = me?.HomeWorld.ValueNullable?.Name.ExtractText() ?? "" };
        Call<AcceptReply>("POST", $"/v1/invites/{inv.Id}/accept", body, r =>
        {
            Last?.Invites.Remove(inv);
            Poke();
            ok(r);
        }, failed);
    }

    public void Decline(InviteInfo inv, Action<string> failed) =>
        Call<object>("POST", $"/v1/invites/{inv.Id}/decline", new { }, _ =>
        {
            Last?.Invites.Remove(inv);
            Poke();
        }, failed);

    public void CancelInvite(long id, Action<string> failed) => Call<object>("DELETE", $"/v1/invites/{id}", null, _ => Poke(), failed);

    // ----- Applications -----
    private int announcedApplications;
    private long lastNotice = -1;   // -1: not caught up yet (the first check-in)

    /// <summary>Tells everyone in your dairy something (a pop-up on their screen), e.g. "breedReady".</summary>
    /// <summary>Ring your dairy's feeding bell (farmers and the owner, in the dairy, once an hour).</summary>
    public void RingBell(Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/dairy/notice", new { kind = "feedingBell" }, _ => ok(), failed);

    public void PostNotice(string kind) => Call<object>("POST", "/v1/dairy/notice", new { kind }, _ => { }, _ => { });
    private DateTime lastApplications = DateTime.MinValue;

    /// <summary>Applications to your dairy (for the owner, or farmers if the owner allows it).</summary>
    public List<ApplicationInfo> Applications { get; private set; } = new();

    public void RefreshApplications(bool now = false)
    {
        if (!now && DateTime.UtcNow - lastApplications < TimeSpan.FromSeconds(30)) return;
        lastApplications = DateTime.UtcNow;
        Call<ApplicationsReply>("GET", "/v1/dairy/applications", null, r => Applications = r.Applications, _ => Applications = new());
    }

    /// <summary>Apply to a dairy with the roles you want and a short note.</summary>
    public void Apply(string dairyId, List<string> roles, string note, Action<string> ok, Action<string> failed)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        var g = plugin.Game;
        var levels = string.Join(" · ", new[] { $"Cow {g.Level}", g.FarmerLevel > 1 ? $"Farmer {g.FarmerLevel}" : "", g.BullLevel > 1 ? $"Bull {g.BullLevel}" : "" }.Where(x => x.Length > 0));
        Call<IdReply>("POST", "/v1/applications", new
        {
            dairyId, roles, note,
            name = me?.Name.TextValue ?? "",
            worldName = me?.HomeWorld.ValueNullable?.Name.ExtractText() ?? "",
            levels,
        }, _ =>
        {
            RefreshDirectory();
            ok(dairyId);
        }, failed);
    }

    public void WithdrawApplication(long id, Action<string> failed) =>
        Call<object>("DELETE", $"/v1/applications/{id}", null, _ => RefreshDirectory(), failed);

    public void AcceptApplication(long id, List<string> roles, Action<string> failed) =>
        Call<object>("POST", $"/v1/dairy/applications/{id}/accept", new { roles }, _ =>
        {
            RefreshApplications(true);
            Poke();
        }, e =>
        {
            RefreshApplications(true);
            failed(e);
        });

    public void DenyApplication(long id, Action<string> failed) =>
        Call<object>("POST", $"/v1/dairy/applications/{id}/deny", new { }, _ => RefreshApplications(true), e =>
        {
            RefreshApplications(true);
            failed(e);
        });

    /// <summary>Owner: which roles the dairy takes applications for, and whether farmers can answer them.</summary>
    public void SetApplying(List<string> lookingFor, bool farmersAnswer, Action<string> failed) =>
        Call<object>("PUT", "/v1/dairy/applying", new { lookingFor, farmersAnswer }, _ => MineWithin(1), failed);

    /// <summary>Play in another of your dairies.</summary>
    public void SwitchDairy(string id, Action<string> ok, Action<string> failed) =>
        Call<SwitchReply>("POST", "/v1/dairy/active", new { id }, r =>
        {
            Mine = null;
            Poke();
            ok(r.Dairy);
        }, failed);

    public sealed class SwitchReply
    {
        public string Dairy { get; set; } = "";
    }

    public void Leave(Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/members/leave", new { }, _ =>
        {
            Mine = null;
            Poke();
            ok();
        }, failed);

    public void RemoveMember(string id, Action<string> failed) => Call<object>("DELETE", $"/v1/members/{id}", null, _ => Poke(), failed);

    public void SetRole(string id, string role, Action<string> failed) =>
        Call<object>("PUT", $"/v1/members/{id}/role", new { role }, _ => Poke(), failed);

    /// <summary>Adds or removes one role (see the server for who may do what).</summary>
    public void ChangeRole(string id, string? add, string? remove, Action<List<string>> ok, Action<string> failed) =>
        Call<RolesReply>("POST", $"/v1/members/{id}/roles", new { add, remove }, r =>
        {
            Poke();
            RefreshMine();
            ok(r.Roles);
        }, failed);

    // ----- Stalls -----

    public void AddStall(string name, float x, float y, float z, int limit, Action ok, Action<string> failed) =>
        Call<IdReply>("POST", "/v1/stalls", new { name, x, y, z, limit }, _ =>
        {
            Poke();
            ok();
        }, failed);

    public void RemoveStall(string id, Action<string> failed) => Call<object>("DELETE", $"/v1/stalls/{id}", null, _ => Poke(), failed);

    /// <summary>Owner: moves a stall to a new seat (keeps its name and tier).</summary>
    public void CleanStall(string id, Action<long> ok, Action<string> failed) =>
        Call<CleanReply>("POST", $"/v1/stalls/{id}/clean", new { }, r =>
        {
            RefreshMine();
            ok(r.Until);
        }, e =>
        {
            // Someone else may have cleaned it: get the stalls' real state so the button greys out
            RefreshMine();
            failed(e);
        });

    // ----- The dairy shelf -----
    public List<ShelfItem> Shelf { get; private set; } = new();
    private DateTime nextShelf = DateTime.MinValue;

    public void RefreshShelf(bool now = false)
    {
        if (!now && DateTime.UtcNow < nextShelf) return;
        nextShelf = DateTime.UtcNow.AddSeconds(30);
        Call<ShelfReply>("GET", "/v1/shelf", null, r => Shelf = r.Items);
    }

    /// <summary>The dairy pantry: ingredients (butter) shared by all the dairy's farmers.</summary>
    public Dictionary<string, int> DairyPantry { get; private set; } = new();
    private DateTime lastPantry = DateTime.MinValue;

    /// <summary>The dairy pantry: every 30 seconds, or more often (every) while the Kitchen is open.</summary>
    public void RefreshPantry(bool now = false, int every = 30)
    {
        if (!now && DateTime.UtcNow - lastPantry < TimeSpan.FromSeconds(every)) return;
        lastPantry = DateTime.UtcNow;
        Call<PantryReply>("GET", "/v1/pantry", null, r => DairyPantry = r.Items ?? new());
    }

    public void PantryAdd(string item, int qty, Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/pantry/add", new { item, qty }, _ =>
        {
            RefreshPantry(true);
            ok();
        }, failed);

    public void PantryTake(string item, int qty, Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/pantry/take", new { item, qty }, _ =>
        {
            RefreshPantry(true);
            ok();
        }, failed);

    public void StockShelf(string item, int qty, Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/shelf/stock", new { item, qty }, _ =>
        {
            RefreshShelf(true);
            ok();
        }, failed);

    public void BuyShelf(string item, Action<ShelfBuyReply> ok, Action<string> failed) =>
        Call<ShelfBuyReply>("POST", "/v1/shelf/buy", new { item }, r =>
        {
            RefreshShelf(true);
            ok(r);
        }, failed);

    public void SetShelfPrice(string item, int price, Action ok, Action<string> failed) =>
        Call<object>("PUT", "/v1/shelf/price", new { item, price }, _ =>
        {
            RefreshShelf(true);
            ok();
        }, failed);

    public void FeedShelf(string item, string to, Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/shelf/feed", new { item, to }, _ =>
        {
            RefreshShelf(true);
            ok();
        }, failed);

    public void Groom(string cowId, Action ok, Action<string> failed) =>
        Call<object>("POST", "/v1/groom", new { cow = cowId }, _ => ok(), failed);

    public void MoveStall(string id, float x, float y, float z, Action ok, Action<string> failed) =>
        Call<object>("PUT", $"/v1/stalls/{id}", new { x, y, z }, _ =>
        {
            RefreshMine();
            ok();
        }, failed);

    public void UpgradeStall(string id, int tier, Action ok, Action<string> failed) =>
        Call<object>("POST", $"/v1/stalls/{id}/tier", new { tier }, _ =>
        {
            Poke();
            ok();
        }, failed);

    // ----- Buy orders -----

    public void PostOrder(int oz, int minGrade, int coins, Action ok, Action<string> failed) =>
        Call<IdReply>("POST", "/v1/orders", new { oz, minGrade, coins }, _ =>
        {
            RefreshMyOrders();
            ok();
        }, failed);

    public void CancelOrder(long id, Action<string> failed) =>
        Call<object>("DELETE", $"/v1/orders/{id}", null, _ =>
        {
            RefreshMyOrders();
            Poke();
        }, failed);

    public void Fill(BuyOrder o, int grade, int level, Action<FillReply> ok, Action<string> failed)
    {
        var here = Estate.Current();
        Call<FillReply>("POST", $"/v1/orders/{o.Id}/fill", new { grade, level, houseId = here?.HouseId.ToString(), pos = plugin.Game.Position() }, r =>
        {
            RefreshOrders(o.DairyId);
            Poke();
            ok(r);
        }, failed);
    }
}
