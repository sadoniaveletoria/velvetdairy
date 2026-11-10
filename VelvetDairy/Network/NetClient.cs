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
/// Talks to the Velvet Dairy Network server: presence, the dairy directory, your dairy's members and stalls,
/// invites, buy orders, deliveries and the inbox. Requests run in the background; their results are applied
/// on the game's thread in <see cref="Update"/>.
///
/// The server's address comes from network.json in the GitHub repo (written when the server deploys), so the
/// server can move without a plugin update. Until that file exists the network is simply off and the game runs locally.
/// </summary>
public sealed class NetClient : IDisposable
{
    private const string NetworkFileUrl = "https://raw.githubusercontent.com/sadoniaveletoria/velvetdairy/main/network.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Plugin plugin;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
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
        while (mainThread.TryDequeue(out var a))
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "Velvet Dairy network callback failed");
            }
        }

        if (Now >= nextUpdateCheck) CheckForUpdate();
        if (!F.NetEnabled || !F.Confirmed18 || Plugin.ObjectTable.LocalPlayer == null) return;
        var t = Now;

        if (Api == null)
        {
            if (t >= nextResolve) Resolve();
            return;
        }

        if (t >= nextBeat && !beating) Heartbeat();
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
                if (!string.IsNullOrEmpty(api) && api.StartsWith("https://"))
                    mainThread.Enqueue(() =>
                    {
                        Api = api;
                        nextBeat = 0;
                        Status = "Connecting...";
                    });
            }
            catch
            {
                mainThread.Enqueue(() => Status = "The Velvet Dairy network isn't set up yet. Playing locally.");
            }
        });
    }

    // ----- Update check: the repo's repo.json says which version is released -----

    private const string RepoFileUrl = "https://raw.githubusercontent.com/sadoniaveletoria/velvetdairy/main/repo.json";
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

    private void Call<T>(string method, string path, object? body, Action<T> ok, Action<string>? failed = null, bool dev = false) where T : class
    {
        var devPhrase = dev ? F.DevKey : "";
        var api = Api;
        if (api == null)
        {
            failed?.Invoke("The Velvet Dairy network isn't available.");
            return;
        }

        var id = F.NetId;
        var key = F.NetKey;
        Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(new HttpMethod(method), api + path);
                req.Headers.Add("X-VD-Id", id);
                req.Headers.Add("X-VD-Key", key);
                if (devPhrase.Length > 0) req.Headers.Add("X-VD-Dev", devPhrase);
                if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
                using var res = await http.SendAsync(req);
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

                    mainThread.Enqueue(() =>
                    {
                        Connected = true;
                        failed?.Invoke(msg);
                    });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Debug($"Velvet Dairy network: {e.Message}");
                mainThread.Enqueue(() =>
                {
                    Connected = false;
                    Status = "Can't reach the Velvet Dairy network right now.";
                    failed?.Invoke("Can't reach the Velvet Dairy network right now.");
                });
            }
        });
    }

    private void Toast(string text, int kind) => plugin.Game.Notify(text, kind);

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
            houseId = here?.HouseId.ToString(),
            pos = g.Position(),
            // Held until you're awake: the server only counts them then
            dairyXp = g.Wake == DairyGame.Wakefulness.Awake ? g.TakeDairyXp() : 0,
            pumped = g.Wake == DairyGame.Wakefulness.Awake ? g.TakePumped() : null,
            atCreamery = g.AtCreamery,
            awake = g.Wake == DairyGame.Wakefulness.Awake,
            status = new MemberStatus
            {
                Milk = (float)Math.Round(F.Cow.Milk, 1), Cap = g.Capacity, Grade = g.CurrentGrade, Spoiled = g.Spoiled,
                InDairy = g.Where.InDairy, Level = g.Level, FarmerLevel = g.FarmerLevel, BullLevel = g.BullLevel,
                Quality = (int)F.Quality, TotalOz = (int)F.Stats.TotalOz,
                Emotes = g.IsCow ? g.MilkEmotesForFarmers() : new List<string>(),
                HandBy = g.Active is { Kind: VelvetDairy.Game.SessionKind.Hand, Hand: { IsTest: false } h } ? h.Name : "",
                AnyEmote = F.AnyLoopEmote,
                Milking = g.MilkingCowName ?? "",
            },
        };
        Call<HeartbeatReply>("POST", "/v1/heartbeat", body, r =>
        {
            beating = false;
            var before = Last?.Membership?.DairyId;
            Last = r;
            Status = $"{r.Online} online";
            plugin.Game.OnMembership(r.Membership);
            if (r.Membership?.DairyId != before) nextMine = 0;

            foreach (var inv in r.Invites.Where(i => announcedInvites.Add(i.Id)))
            {
                var text = $"You're invited to join {inv.Dairy} as a {inv.Role}. Answer on the Dairy tab.";
                Toast(text, 1);
                Plugin.ChatGui.Print(text, "Velvet Dairy");
            }

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

    /// <summary>Makes the next dairy refresh happen within this many seconds (stall markers keep others' states fresh this way).</summary>
    public void MineWithin(double seconds)
    {
        if (nextMine > Now + seconds) nextMine = Now + seconds;
    }

    public void RefreshMine()
    {
        nextMine = Now + 60;
        Call<MyDairyReply>("GET", "/v1/dairy/mine", null, r =>
        {
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

    /// <summary>Developer: puts the Grand Creamery where you're standing (for everyone).</summary>
    public void SetCreamery(object body, Action ok, Action<string> failed) =>
        Call<IdReply>("PUT", "/v1/dev/creamery", body, _ =>
        {
            Poke();
            ok();
        }, failed, dev: true);

    /// <summary>Milk used on the creamery's own customer and visitor orders (so it can't also go to a contract).</summary>
    public void UseCarried(float oz) =>
        Call<IdReply>("POST", "/v1/carry/use", new { oz = Math.Round(oz, 2) }, _ => { }, e => Plugin.Log.Debug($"Carry use: {e}"));

    /// <summary>Ships milk from your jugs to this week's contract.</summary>
    public void ShipToContract(int grade, int oz, Action<ShipReply> ok, Action<string> failed) =>
        Call<ShipReply>("POST", "/v1/dairy/contract/ship", new { grade, oz }, r =>
        {
            Poke();
            ok(r);
        }, failed);

    /// <summary>Tells the server a farmer hand milked you, so they get their share.</summary>
    public void HandPay(string farmer, string world, float oz, int grade) =>
        Call<HandPayReply>("POST", "/v1/handpay", new { farmer, world, oz = Math.Round(oz, 2), grade }, _ => { }, e => Plugin.Log.Debug($"Hand pay: {e}"));

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

    public void Fill(BuyOrder o, int grade, Action<FillReply> ok, Action<string> failed)
    {
        var here = Estate.Current();
        Call<FillReply>("POST", $"/v1/orders/{o.Id}/fill", new { grade, houseId = here?.HouseId.ToString(), pos = plugin.Game.Position() }, r =>
        {
            RefreshOrders(o.DairyId);
            Poke();
            ok(r);
        }, failed);
    }
}
