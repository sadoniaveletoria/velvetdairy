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
    public DairyGame Game { get; }
    public MainWindow MainWindow { get; }
    public HudWindow Hud { get; }
    public Sounds Sounds { get; }
    public NetClient Net { get; }
    public OwnerHudWindow OwnerHud { get; }
    public MembersWindow Members { get; }
    public GuideWindow Guide { get; }
    public MilkAnimationWindow MilkAnimation { get; }
    public KitchenWindow Kitchen { get; }
    public ChangelogWindow Changes { get; }
    public MilkingWindow Milking { get; }
    public MilkAskWindow MilkAskPopup { get; }
    public TutorialWindow Tutorial { get; }

    /// <summary>This build's version, sent to the network so old versions can be told to update.</summary>
    public static string Version { get; } = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0";

    private readonly WindowSystem windowSystem = new("VelvetDairy");
    private readonly ContextMenuHandler contextMenu;
    private readonly Art art;
    private readonly Fonts fonts;
    private double saveTimer;

    public Plugin()
    {
        Configuration? cfg = null;
        try
        {
            cfg = PluginInterface.GetPluginConfig() as Configuration;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Eorzea Milkers: couldn't read the old save, starting fresh.");
        }

        Config = cfg ?? new Configuration();

        // Catch saves edited outside the game before anything uses them
        var integrity = Integrity.Check(Config);
        Config.EnsureDefaults();

        art = new Art();
        fonts = new Fonts();
        Ui.Art = art;
        Ui.Fonts = fonts;

        Net = new NetClient(this);
        Game = new DairyGame(this);
        Sounds = new Sounds(this);
        MainWindow = new MainWindow(this, art, fonts);
        Hud = new HudWindow(this, art, fonts);
        windowSystem.AddWindow(MainWindow);
        windowSystem.AddWindow(Hud);
        OwnerHud = new OwnerHudWindow(this, art, fonts);
        windowSystem.AddWindow(OwnerHud);
        Members = new MembersWindow(this, fonts);
        windowSystem.AddWindow(Members);
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
        contextMenu = new ContextMenuHandler(this);

        switch (integrity)
        {
            case Integrity.Result.Restored:
                ChatGui.Print("Your save was changed outside the game, so it was put back to your last saved progress.", "Eorzea Milkers");
                break;
            case Integrity.Result.Reset:
                ChatGui.Print("Your save was changed outside the game and no good backup was found, so your progress started over. Your dairy, stalls and farmhands were kept.", "Eorzea Milkers");
                break;
        }

        if (integrity != Integrity.Result.Fine) SaveConfig();

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Eorzea Milkers. \"/milkers hud\" shows or hides the HUD, \"/milkers guide\" opens How to play, \"/milkers tutorial\" runs the walkthrough, \"/milkers changes\" shows what's new.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
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
        Game.Update(dt);
        Sounds.Update();
        Net.Update();
        Tutorial.CheckFirstRun();
        if (!Tutorial.IsOpen) Changes.CheckAfterUpdate();

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
        Net.Dispose();
        contextMenu.Dispose();

        PluginInterface.UiBuilder.OpenConfigUi -= MainWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= MainWindow.Toggle;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= DrawOverlay;
        if (chatHandler != null) ChatGui.ChatMessage -= chatHandler;
        CommandManager.RemoveHandler(Command);
        windowSystem.RemoveAllWindows();
        fonts.Dispose();
        SaveConfig();
    }

    public void SaveConfig()
    {
        Integrity.Seal(Config.Farm);
        PluginInterface.SavePluginConfig(Config);
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
        var a = args.Trim();
        if (a.Equals("guide", StringComparison.OrdinalIgnoreCase) || a.Equals("help", StringComparison.OrdinalIgnoreCase))
            Guide.Open();
        else if (a.Equals("tutorial", StringComparison.OrdinalIgnoreCase))
            Tutorial.Start();
        else if (a.Equals("changes", StringComparison.OrdinalIgnoreCase) || a.Equals("new", StringComparison.OrdinalIgnoreCase))
            Changes.OpenAll();
        else if (a.Equals("hud", StringComparison.OrdinalIgnoreCase))
        {
            Config.Farm.ShowHud = !Config.Farm.ShowHud;
            SaveConfig();
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
        if (Game.Draft is { } d && ClientState.TerritoryType == d.Territory) Windows.AreaOverlay.Draw(d);
        if (Config.Farm.ShowStallMarkers && Game.Where.InDairy && Game.Blocked() == null)
            // While you're in the dairy with markers on, fetch everyone's stall states every 10 seconds
            if (Game.Networked) Game.Net.MineWithin(10);
            foreach (var s in Game.StallsHere()) Windows.AreaOverlay.DrawStall(s);
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
