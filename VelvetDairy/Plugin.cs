using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using VelvetDairy.Game;
using VelvetDairy.Network;
using VelvetDairy.Windows;

namespace VelvetDairy;

public sealed class Plugin : IDalamudPlugin
{
    /// <summary>The Eorzea Milkers community Discord (18+).</summary>
    public const string DiscordUrl = "https://discord.gg/yacq26rw4u";

    public static void OpenDiscord() => Dalamud.Utility.Util.OpenLink(DiscordUrl);

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private const string Command = "/milkers";

    public Configuration Config { get; }
    public DairyGame Game { get; private set; }
    public MainWindow MainWindow { get; }
    public HudWindow Hud { get; }
    public Sounds Sounds { get; }
    public CorruptionLink Corruption { get; }
    public Companions Companions { get; }
    public NetClient Net { get; private set; }
    public MembersWindow Members { get; }
    public ApplyWindow Apply { get; }
    public GuideWindow Guide { get; }
    public MilkAnimationWindow MilkAnimation { get; }
    public KitchenWindow Kitchen { get; }
    public ChangelogWindow Changes { get; }
    public MilkingWindow Milking { get; }
    public MilkAskWindow MilkAskPopup { get; }
    public TutorialWindow Tutorial { get; }
    public HelperNoticeWindow Helpers { get; }
    public NoticeWindow Notices { get; }
    public ScanWindow Scanner { get; }
    public WhoHereWindow WhoHere { get; }
    public FeedingBellWindow FeedingBell { get; }
    public RestoreWindow Restore { get; }
    public CharacterClaimWindow Claim { get; }

    /// <summary>This build's version, sent to the network so old versions can be told to update.</summary>
    public static string Version { get; } = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0";

    private readonly WindowSystem windowSystem = new("VelvetDairy");
    private readonly WindowSystem claimSystem = new("VelvetDairyClaim");
    private readonly ContextMenuHandler contextMenu;
    private readonly Art art;
    private readonly Fonts fonts;
    private double saveTimer;

    public Plugin()
    {
        Configuration? cfg = null;
        try
        {
            // A dev build (loaded from Dev Plugin Locations) never reads or writes the live save: it has its own file
            cfg = PluginInterface.IsDev ? LoadDevConfig() : PluginInterface.GetPluginConfig() as Configuration;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Eorzea Milkers: couldn't read the old save, starting fresh.");
        }

        Config = cfg ?? new Configuration();
        Config.EnsureDefaults();

        // The save from before saves were per character: catch edits now, before it's handed to a character
        var integrity = Integrity.Result.Fine;
        if (Config.Unclaimed != null)
        {
            integrity = Integrity.Check(Config.Unclaimed, null, out var old);
            old.EnsureDefaults();
            Config.Unclaimed = old;
            if (integrity != Integrity.Result.Fine) Integrity.Seal(old, null);
        }

        art = new Art();
        fonts = new Fonts();
        Ui.Art = art;
        Ui.Fonts = fonts;

        Net = new NetClient(this);
        Game = new DairyGame(this);
        Sounds = new Sounds(this);
        // Breeding and pregnancy come from the Corruption plugin; its events go to whichever character is playing
        Corruption = new CorruptionLink();
        Companions = new Companions(this);
        Corruption.PartnerClimax += (name, world) => Game.OnPartnerClimax(name, world);
        Corruption.Climax += () => Game.OnClimax();
        Corruption.Changed += () => Game.ReadPregnancySoon();
        Corruption.Reloaded += () => Game.CorruptionReloaded();
        MainWindow = new MainWindow(this, art, fonts);
        Hud = new HudWindow(this, art, fonts);
        windowSystem.AddWindow(MainWindow);
        windowSystem.AddWindow(Hud);
        Members = new MembersWindow(this, fonts);
        windowSystem.AddWindow(Members);
        Apply = new ApplyWindow(this, fonts);
        windowSystem.AddWindow(Apply);
        Guide = new GuideWindow(this, fonts);
        windowSystem.AddWindow(Guide);
        MilkAnimation = new MilkAnimationWindow(this);
        windowSystem.AddWindow(MilkAnimation);
        Kitchen = new KitchenWindow(this);
        windowSystem.AddWindow(Kitchen);
        Milking = new MilkingWindow(this);
        MilkAskPopup = new MilkAskWindow(this);
        windowSystem.AddWindow(MilkAskPopup);
        windowSystem.AddWindow(Milking);
        Changes = new ChangelogWindow(this, fonts);
        windowSystem.AddWindow(Changes);
        Tutorial = new TutorialWindow(this, fonts);
        windowSystem.AddWindow(Tutorial);
        Helpers = new HelperNoticeWindow(this, fonts);
        windowSystem.AddWindow(Helpers);
        Notices = new NoticeWindow(this, fonts);
        windowSystem.AddWindow(Notices);
        Scanner = new ScanWindow(this, fonts);
        windowSystem.AddWindow(Scanner);
        WhoHere = new WhoHereWindow(this, fonts);
        windowSystem.AddWindow(WhoHere);
        FeedingBell = new FeedingBellWindow(this, fonts);
        windowSystem.AddWindow(FeedingBell);
        Restore = new RestoreWindow(this, fonts);
        windowSystem.AddWindow(Restore);
        Claim = new CharacterClaimWindow(this, fonts);
        claimSystem.AddWindow(Claim);
        contextMenu = new ContextMenuHandler(this);

        Diag.Info("start", $"Eorzea Milkers {Version} loaded · {Config.Characters.Count} character save(s){(Config.Unclaimed != null ? ", old save unclaimed" : "")}");
        TellIntegrity(integrity);
        if (integrity != Integrity.Result.Fine) SaveConfig();

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Eorzea Milkers. \"/milkers hud\" shows or hides the HUD, \"/milkers guide\" opens How to play, \"/milkers tutorial\" runs the walkthrough, \"/milkers changes\" shows what's new, \"/milkers support\" copies support info for the Discord.",
        });

        PluginInterface.UiBuilder.Draw += DrawWindows;
        PluginInterface.UiBuilder.Draw += DrawOverlay;
        chatHandler = m => OnChat(m);
        ChatGui.ChatMessage += chatHandler;

        PluginInterface.UiBuilder.OpenMainUi += MainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += MainWindow.Toggle;
        Framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        var dt = (float)fw.UpdateDelta.TotalSeconds;
        WatchCharacter();
        Units.Metric = Config.Farm.Metric;   // how milk amounts are written (Settings)
        // Waiting to hear whose the old save is: nothing runs meanwhile (but the server's answer still comes in)
        if (Waiting || Config.ActiveKey == null)
        {
            Net.Pump();
            return;
        }

        Game.Update(dt);
        Sounds.Update();
        Net.Update();
        Tutorial.CheckFirstRun();
        if (!Tutorial.IsOpen) Changes.CheckAfterUpdate();
        Helpers.Check(dt);
        Restore.Tick();
        // Backup setup needed (new players, and everyone once after the update): open the window to it once
        if (BackupSetupNeeded && !setupShown && ObjectTable.LocalPlayer != null)
        {
            setupShown = true;
            MainWindow.IsOpen = true;
            ChatGui.Print("[Eorzea Milkers] Before you play: set up your save backup (it takes a minute). It's open in the Eorzea Milkers window.");
        }

        // Needs and milk change every second; write them out now and then
        saveTimer += dt;
        if (saveTimer > 60)
        {
            saveTimer = 0;
            SaveConfig();
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        Game.Stop("Plugin unloaded.");
        Game.EndMilkMod();
        Sounds.Dispose();
        Game.StopForcedCreampie();
        Corruption.Dispose();
        Net.Dispose();
        contextMenu.Dispose();

        PluginInterface.UiBuilder.OpenConfigUi -= MainWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= MainWindow.Toggle;
        PluginInterface.UiBuilder.Draw -= DrawWindows;
        PluginInterface.UiBuilder.Draw -= DrawOverlay;
        if (chatHandler != null) ChatGui.ChatMessage -= chatHandler;
        CommandManager.RemoveHandler(Command);
        windowSystem.RemoveAllWindows();
        claimSystem.RemoveAllWindows();
        fonts.Dispose();
        SaveConfig();
    }

    public void SaveConfig()
    {
        // The blank save shown before a character logs in is never written out
        if (Config.ActiveKey != null) Integrity.Seal(Config.Farm, Config.ActiveKey);
        if (PluginInterface.IsDev) SaveDevConfig();
        else PluginInterface.SavePluginConfig(Config);
    }

    // ----- Dev builds keep their own save -----
    // A build of your own (from Dev Plugin Locations) is signed with a different key, so the live save would look
    // edited to it. It uses dev-config.json instead and never touches the live save or its backups.

    private static string DevConfigPath => System.IO.Path.Combine(PluginInterface.ConfigDirectory.FullName, "dev-config.json");

    private static Configuration? LoadDevConfig()
    {
        if (!System.IO.File.Exists(DevConfigPath)) return null;
        return Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(System.IO.File.ReadAllText(DevConfigPath));
    }

    private void SaveDevConfig()
    {
        try
        {
            System.IO.Directory.CreateDirectory(PluginInterface.ConfigDirectory.FullName);
            var tmp = DevConfigPath + ".tmp";
            System.IO.File.WriteAllText(tmp, Newtonsoft.Json.JsonConvert.SerializeObject(Config, Newtonsoft.Json.Formatting.Indented));
            System.IO.File.Move(tmp, DevConfigPath, true);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Eorzea Milkers couldn't write the dev save");
        }
    }

    /// <summary>Copies the support info (setup + recent log) to the clipboard and saves it next to the settings.</summary>
    public void CopySupportInfo()
    {
        var text = Diag.Report(this);
        Dalamud.Bindings.ImGui.ImGui.SetClipboardText(text);
        var path = System.IO.Path.Combine(PluginInterface.ConfigDirectory.FullName, "support-info.txt");
        try { System.IO.File.WriteAllText(path, text); }
        catch (Exception e) { Diag.Warn("support", "Couldn't save support-info.txt", e); }
        ChatGui.Print("Support info copied. Paste it in #bug-reports on the Discord (it's also saved as support-info.txt in the plugin's settings folder).", "Eorzea Milkers");
    }

    private void DrawWindows()
    {
        if (Claim.IsOpen) claimSystem.Draw();
        else if (askingFor != null) return;
        else if (Config.ActiveKey != null) windowSystem.Draw();   // saves are per character: nothing to show until one logs in
    }

    private static void TellIntegrity(Integrity.Result r)
    {
        if (r is Integrity.Result.Restored or Integrity.Result.Reset) Diag.Warn("save", $"Save check: {r}");
        switch (r)
        {
            case Integrity.Result.Restored:
                ChatGui.Print("Your save was changed outside the game, so it was put back to your last saved progress.", "Eorzea Milkers");
                break;
            case Integrity.Result.Reset:
                ChatGui.Print("Your save didn't pass its check and no good local backup was found, so your progress started over (the old save was kept as rejected-save-*.json). Your dairy and network identity were kept, and your cloud backup will be offered back if you have one (/milkers restore).", "Eorzea Milkers");
                break;
        }
    }

    // ----- One save per character -----
    // Each character keeps their own save (and their own id on the network, so the server sees them as different players).
    // The save follows whoever is logged in; logging out keeps the last one until another character logs in.

    private string? askingFor;   // waiting for the server to say whose the old save is

    /// <summary>Waiting to find out whose the old save is: nothing runs and only the question shows.</summary>
    public bool Waiting => Claim.IsOpen || askingFor != null;

    /// <summary>The logged-in character's content id: the same through name changes and world transfers. 0 when not logged in.</summary>
    internal static unsafe ulong ContentId()
    {
        var ps = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        return ps == null ? 0 : ps->ContentId;
    }

    private void WatchCharacter()
    {
        var me = ObjectTable.LocalPlayer;
        if (me == null) return;
        var cid = ContentId();
        if (cid == 0) return;
        var key = cid.ToString("x16");
        if (key == Config.ActiveKey || key == askingFor || (Claim.IsOpen && Claim.Key == key)) return;

        if (!Config.Characters.ContainsKey(key) && Config.Unclaimed != null)
        {
            // The old save never got past the 18+ check, so there's nothing to keep
            if (!Config.Unclaimed.Confirmed18)
            {
                Config.Unclaimed = null;
            }
            else
            {
                // Ask the server which character the old save last played as. If it's this one, the progress just
                // moves over; only if it isn't (or nobody knows) does the player get asked.
                var name = me.Name.TextValue;
                var world = me.HomeWorld.ValueNullable?.Name.ExtractText() ?? "";
                var hash = NetClient.CharHash(name, me.HomeWorld.RowId);
                Claim.IsOpen = false;
                askingFor = key;
                Net.LastCharacterOf(Config.Unclaimed, last =>
                {
                    if (askingFor != key) return;   // someone else logged in meanwhile
                    askingFor = null;
                    if (last != null && last == hash)
                    {
                        AnswerClaim(key, true);
                        ChatGui.Print($"Eorzea Milkers now keeps a save for each character. Your progress is saved on {name}.", "Eorzea Milkers");
                    }
                    else
                    {
                        Claim.Ask(key, name, world);
                    }
                });
                return;
            }
        }

        SwitchTo(key);
    }

    /// <summary>The player said whether the old save belongs to this character.</summary>
    public void AnswerClaim(string key, bool mine)
    {
        Diag.Info("save", $"Old save {(mine ? "given to" : "not given to")} character {key[..Math.Min(8, key.Length)]}");
        if (mine && Config.Unclaimed != null)
        {
            Config.Characters[key] = Config.Unclaimed;
            Config.Unclaimed = null;
            // Its last good backup comes along too
            try
            {
                var from = Integrity.BackupPath(null);
                if (System.IO.File.Exists(from)) System.IO.File.Move(from, Integrity.BackupPath(key), true);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Eorzea Milkers couldn't move the save backup");
            }
        }

        SwitchTo(key);
    }

    private void SwitchTo(string key)
    {
        if (Config.ActiveKey != null)
        {
            Game.Stop("Switched character.");
            Game.EndMilkMod();
            SaveConfig();
        }

        var fresh = !Config.Characters.ContainsKey(key);
        if (fresh) Config.Characters[key] = Config.NewCharacterSave();
        Config.Use(key);
        Diag.Info("save", $"Playing as character {key[..Math.Min(8, key.Length)]} ({(fresh ? "new save" : "existing save")})");

        var r = Integrity.Check(Config.Farm, key, out var save);
        Config.Farm = save;
        FreshSave = fresh;
        LastCheck = r;
        Reload(key);
        TellIntegrity(r);
        SaveConfig();
        // A brand new save, or one that had to start over: offer the cloud backup once the server knows this character
        if (fresh || r == Integrity.Result.Reset) Restore.OfferSoon(r == Integrity.Result.Reset);
    }

    /// <summary>The save check's result when this character's save loaded.</summary>
    public Integrity.Result LastCheck { get; private set; } = Integrity.Result.Fine;

    /// <summary>This character's save was made new this session (a new install, or a new character).</summary>
    public bool FreshSave { get; private set; }

    /// <summary>
    /// Backup setup comes before playing (after the 18+ check): back up once and confirm the recovery code, or choose to
    /// play without the network. Existing players do it once too. Dev builds don't upload, so they skip it.
    /// </summary>
    public bool BackupSetupNeeded =>
        !PluginInterface.IsDev && Config.ActiveKey != null && Config.Farm.Confirmed18 && !Config.Farm.BackupReady && !Config.Farm.BackupOptOut;

    private bool setupShown;

    /// <summary>Cloud backups go up from live installs only: a dev build's save stays on this PC.</summary>
    public bool CloudBackupAllowed => !PluginInterface.IsDev && Config.ActiveKey != null && Config.Farm.Confirmed18 && Config.Farm.NetEnabled;

    /// <summary>Starts the game and network over on the current character's save (after switching or restoring).</summary>
    private void Reload(string key)
    {
        Config.EnsureDefaults();
        devCache = null;

        // Shop items that were taken out: their coins come back
        foreach (var (id, gone, price) in new[] { ("heifer", "Prize heifer sash", 1600), ("deeprest", "Deep rest", 900) })
            if (Config.Farm.Upgrades.Remove(id))
            {
                Config.Farm.Coins += price;
                Diag.Info("save", $"{gone} was taken out of the Shop: refunded {price} coins");
                ChatGui.Print($"[Eorzea Milkers] {gone} was taken out of the Shop, so you got its {price} coins back.");
            }

        // Name change or world transfer: the save follows the character (the next heartbeat updates your dairy's roster)
        var me = ObjectTable.LocalPlayer;
        var name = me?.Name.TextValue ?? "";
        var world = me?.HomeWorld.ValueNullable?.Name.ExtractText() ?? "";
        if (name.Length > 0)
        {
            var f = Config.Farm;
            if (f.CharName.Length > 0 && (f.CharName != name || (world.Length > 0 && f.CharWorld != world)))
            {
                var was = f.CharWorld != world && world.Length > 0 ? $"{f.CharName} ({f.CharWorld})" : f.CharName;
                ChatGui.Print($"Welcome back! Your Eorzea Milkers progress followed you from {was}.", "Eorzea Milkers");
            }

            f.CharName = name;
            if (world.Length > 0) f.CharWorld = world;
        }

        // Everything the game and network remember belongs to the old save: start both over
        Net.Dispose();
        Net = new NetClient(this);
        Game = new DairyGame(this);
        Sounds.Attach(Game);
        MainWindow.OnCharacterChanged();
        Kitchen.IsOpen = false;
        Milking.IsOpen = false;
        Members.IsOpen = false;
    }

    /// <summary>
    /// Puts a cloud backup in place of this character's save. The backup must pass this build's signature check (or
    /// the one from the day before is used); with neither, only the network identity comes back (dairy, memberships).
    /// The save it replaces is kept as before-restore-*.json. Returns what happened, for the player.
    /// </summary>
    public string ApplyRestore(string saveJson, string prevJson, string netId, string netKey)
    {
        if (Config.ActiveKey is not { } key) return "Log in to the character first.";
        FarmSave? pick = null;
        var usedPrev = false;
        foreach (var (text, prev) in new[] { (saveJson, false), (prevJson, true) })
        {
            if (string.IsNullOrEmpty(text)) continue;
            try
            {
                var s = System.Text.Json.JsonSerializer.Deserialize<FarmSave>(text);
                if (s != null && Integrity.IsGood(s))
                {
                    pick = s;
                    usedPrev = prev;
                    break;
                }

                Diag.Warn("restore", $"The {(prev ? "day-before" : "latest")} cloud backup didn't pass the save check");
            }
            catch (Exception e)
            {
                Diag.Warn("restore", "A cloud backup couldn't be read", e);
            }
        }

        var cur = Config.Farm;
        Integrity.KeepAside(cur, key, "before-restore");
        Game.Stop("Restoring a backup.");
        Game.EndMilkMod();

        var f = pick ?? cur;
        f.NetId = netId;
        f.NetKey = netKey;
        f.DevKey = cur.DevKey;
        f.NetEnabled = true;
        f.NetServerOverride = cur.NetServerOverride;
        f.Confirmed18 = cur.Confirmed18 || f.Confirmed18;
        Config.Farm = f;
        LastCheck = Integrity.Result.Fine;
        FreshSave = false;
        Reload(key);
        SaveConfig();
        Net.BackupSoon(30);
        Diag.Info("restore", pick != null ? $"Restored the {(usedPrev ? "day-before" : "latest")} cloud backup" : "Restored the network identity only");

        return pick != null
            ? usedPrev ? "Your save is back (the backup from the day before: the newest one didn't pass the save check)." : "Your save is back."
            : saveJson.Length > 0 ? "Your dairy and memberships are back, but the backup didn't pass the save check, so your progress couldn't be restored."
            : "Your dairy and memberships are back. There was no cloud backup of your progress yet (it started in version 0.9.84).";
    }

    // ----- Developer mode -----
    // On automatically when loaded from Dev Plugin Locations. On a live install, "/milkers dev <phrase>" turns it on.
    // Only the SHA-256 of the phrase is in the code, so reading the source or the settings file doesn't reveal it.
    // To change the phrase: PowerShell
    //   $p="your-new-phrase"; [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($p))).Replace("-","")
    // and paste the result here.
    private const string DevKeyHash = "7D4E0A8C4A26DE8D2843E2A563F667D74BEDA3B81FFA447C22BD40D93B5AF76F";

    private static bool KeyOk(string? phrase) =>
        !string.IsNullOrEmpty(phrase) &&
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(phrase))) == DevKeyHash;

    private bool? devCache;

    public bool Dev => devCache ??= PluginInterface.IsDev || KeyOk(Config.Farm.DevKey);

    private void OnCommand(string command, string args)
    {
        if (Config.ActiveKey == null || Waiting)
        {
            ChatGui.Print(Waiting
                ? "One moment: Eorzea Milkers is finding this character's save."
                : "Log in to a character first: each character has their own Eorzea Milkers progress.", "Eorzea Milkers");
            return;
        }

        var a = args.Trim();
        if (a.Equals("guide", StringComparison.OrdinalIgnoreCase) || a.Equals("help", StringComparison.OrdinalIgnoreCase))
            Guide.Open();
        else if (a.Equals("restore", StringComparison.OrdinalIgnoreCase) || a.Equals("backup", StringComparison.OrdinalIgnoreCase))
            Restore.IsOpen = true;
        else if (a.Equals("support", StringComparison.OrdinalIgnoreCase) || a.Equals("log", StringComparison.OrdinalIgnoreCase))
            CopySupportInfo();
        else if (a.Equals("tutorial", StringComparison.OrdinalIgnoreCase))
            Tutorial.Start();
        else if (a.Equals("changes", StringComparison.OrdinalIgnoreCase) || a.Equals("new", StringComparison.OrdinalIgnoreCase))
            Changes.OpenAll();
        else if (a.Equals("hud", StringComparison.OrdinalIgnoreCase))
        {
            Config.Farm.ShowHud = !Config.Farm.ShowHud;
            SaveConfig();
            if (Config.Farm.ShowHud && Config.Farm.HudOnlyInDairy && !Game.HudPlaceNow)
                Game.Notify("HUD on. It shows once you're in a dairy (Settings: \"Only while I'm in a dairy\").", 0);
        }
        else if (a.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
        {
            var phrase = a.Length > 3 ? a[3..].Trim() : "";
            if (phrase.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                Config.Farm.DevKey = "";
                devCache = null;
                SaveConfig();
                ChatGui.Print(PluginInterface.IsDev ? "Developer key cleared (still on because this is a dev build)." : "Developer mode off.", "Eorzea Milkers");
            }
            else if (KeyOk(phrase))
            {
                Config.Farm.DevKey = phrase;
                devCache = null;
                SaveConfig();
                ChatGui.Print("Developer mode on. See Settings → Developer.", "Eorzea Milkers");
            }
            else
            {
                // Same message whether or not dev mode exists, so it can't be probed
                MainWindow.Toggle();
            }
        }
        else
        {
            MainWindow.Toggle();
        }
    }

    /// <summary>The dairy box being marked out, drawn on the ground (only while editing).</summary>
    private void DrawOverlay()
    {
        if (Config.ActiveKey == null || Waiting) return;
        if (Game.Draft is { } d && ClientState.TerritoryType == d.Territory) Windows.AreaOverlay.Draw(d);
        if (Config.Farm.ShowStallMarkers && Game.Where.InDairy && Game.Blocked() == null)
        {
            // Just a dot where each stall is: the stall list from the regular dairy refresh is all it needs
            foreach (var s in Game.StallsHere()) Windows.AreaOverlay.DrawStall(s);
        }
    }

    private IChatGui.OnHandleableChatMessageDelegate? chatHandler;

    /// <summary>Sending a chat message (say, party, FC, tells...) counts as being awake. Only the sender's name is looked at.</summary>
    private void OnChat(object message)
    {
        try
        {
            var me = ObjectTable.LocalPlayer?.Name.TextValue;
            if (string.IsNullOrEmpty(me)) return;
            var sender = message.GetType().GetProperty("Sender")?.GetValue(message);
            if (sender == null) return;
            var text = sender.GetType().GetProperty("TextValue")?.GetValue(sender) as string
                       ?? sender.GetType().GetMethod("ExtractText", System.Type.EmptyTypes)?.Invoke(sender, null) as string
                       ?? sender.ToString() ?? "";
            if (text.Contains(me, System.StringComparison.Ordinal)) Game.Touch();
        }
        catch
        {
            // Never let chat handling break anything
        }
    }
}
