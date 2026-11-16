using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;
using VelvetDairy.Network;

namespace VelvetDairy.Windows;

/// <summary>
/// Backup and restore (Settings, or /milkers restore). Your save is backed up on the Eorzea Milkers server every few
/// minutes. On a new install (a rebuilt PC, a lost settings file) you get it back, with your dairy and memberships,
/// with your recovery code, with a restore key from the Discord walkthrough (#restore-save: verify the character on the
/// Lodestone there), or by verifying on the Lodestone right here.
/// </summary>
public sealed class RestoreWindow : Window
{
    private const string LodestoneProfile = "https://na.finalfantasyxiv.com/lodestone/my/setting/profile/";

    private readonly Plugin plugin;
    private readonly Fonts fonts;

    // Offering a restore when a save is new or had to start over
    private bool offerPending, offerAfterReset;
    private double offerAt;
    private string offer = "";

    private bool showCode;
    private string recovery = "";
    private string lodeCode = "";
    private string message = "";
    private bool messageGood;
    private bool busy;
    private double confirmUntil;
    private string confirming = "";

    public RestoreWindow(Plugin plugin, Fonts fonts)
        : base("Backup and restore##VelvetDairyRestore", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        PositionCondition = ImGuiCond.FirstUseEver;
        Position = new Vector2(420, 220);
    }

    private static double Now => ImGui.GetTime();
    private NetClient Net => plugin.Net;

    /// <summary>A save was just made or reset: once the network is up, see if there's a backup to offer.</summary>
    public void OfferSoon(bool afterReset)
    {
        offerPending = true;
        offerAfterReset = afterReset;
        offerAt = Now + 8;
        offer = "";
    }

    /// <summary>Called every frame (the window may be closed).</summary>
    public void Tick()
    {
        if (!offerPending || Now < offerAt || !Net.Active || PluginIsDev) return;
        offerPending = false;
        if (offerAfterReset)
        {
            // Our own backup, from before the reset (the server never lets a reset save replace it)
            Net.GetMyBackup(r =>
            {
                if (!r.Found) return;
                offer = $"Your save had to start over, but your cloud backup from {Ago(r.Saved)} is safe. Restore it?";
                IsOpen = true;
            }, _ => { });
        }
        else
        {
            Net.RestoreAvailable(r =>
            {
                if (!r.Available) return;
                offer = r.HasSave
                    ? $"This character has a cloud backup from {Ago(r.Saved)} on another install. Paste your recovery code below, or get a restore key in #restore-save on the Discord."
                    : "This character has a dairy on another install. Get it back with a restore key from #restore-save on the Discord, or the Lodestone below (there's no cloud backup of the progress yet).";
                IsOpen = true;
            });
        }
    }

    private static bool PluginIsDev => Plugin.PluginInterface.IsDev;

    public override void PreDraw() => PaperStyle.Push();
    public override void PostDraw() => PaperStyle.Pop();

    private static string Ago(long unix)
    {
        if (unix <= 0) return "a while ago";
        var s = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix;
        return s < 90 ? "just now" : s < 5400 ? $"{s / 60} minutes ago" : s < 129600 ? $"{s / 3600} hours ago" : $"{s / 86400} days ago";
    }

    private static void Heading(string text)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Theme.RoseDeep, text);
    }

    /// <summary>Restoring replaces this character's save: the button has to be pressed twice.</summary>
    private bool Confirmed(string id, string label, float w, bool enabled, string tip)
    {
        var armed = confirming == id && Now < confirmUntil;
        if (!Ui.Button($"##{id}", armed ? "Press again to replace my save" : label, new Vector2(armed ? 240 : w, 28), armed ? "honey" : "rose", enabled && !busy, tip))
            return false;
        if (armed)
        {
            confirming = "";
            return true;
        }

        confirming = id;
        confirmUntil = Now + 6;
        return false;
    }

    private void Done(string text, bool good)
    {
        busy = false;
        message = text;
        messageGood = good;
    }

    public override void Draw()
    {
        const float width = 440;
        var f = plugin.Config.Farm;
        using (fonts.For(fonts.Heading, "Backup and restore")!.Push()) ImGui.TextColored(Theme.RoseDeep, "Backup and restore");

        if (offer.Length > 0)
        {
            ImGui.Spacing();
            Ui.Wrapped(offer, Theme.SageDeep, width);
            if (offerAfterReset && Confirmed("rsown", "Restore my backup", 170, Net.Active, "Puts your cloud backup back in place of this save. The save it replaces is kept on your PC."))
            {
                busy = true;
                Net.GetMyBackup(r => Done(r.Found ? plugin.ApplyRestore(r.Save, r.Prev, f.NetId, f.NetKey) : "There's no backup on the server.", r.Found),
                    e => Done(e, false));
                offer = "";
            }
        }

        if (message.Length > 0)
        {
            ImGui.Spacing();
            Ui.Wrapped(message, messageGood ? Theme.SageDeep : Theme.Warn, width);
        }

        // ----- This install's backup -----
        Heading("Your cloud backup");
        if (PluginIsDev)
            Ui.Wrapped("This is a dev build: it keeps its own save (dev-config.json) and never uploads it.", Theme.InkSoft, width);
        else if (!f.NetEnabled)
            Ui.Wrapped("Cloud backups need the Eorzea Milkers Network (Settings).", Theme.InkSoft, width);
        else
        {
            var last = Net.LastBackupUtc;
            ImGui.TextColored(Theme.Ink, last.HasValue ? $"Backed up {Ago(new DateTimeOffset(last.Value).ToUnixTimeSeconds())}. It saves again every few minutes." : "Your save is backed up every few minutes while you play.");
            if (Net.BackupNote.Length > 0) Ui.Wrapped(Net.BackupNote, Theme.InkSoft, width);
            if (Ui.Button("##rsnow", "Back up now", new Vector2(120, 26), "cream", Net.Active, "Send your save to the server right now."))
                Net.BackupNow();
        }

        ImGui.Spacing();
        Ui.Wrapped("Recovery code: write it down or screenshot it. On a new install, paste it below to get everything back. Anyone with it can sign in as you, so keep it private.", Theme.Ink, width);
        if (!showCode)
        {
            if (Ui.Button("##rsshow", "Show my recovery code", new Vector2(190, 26), "cream", f.NetId.Length > 0)) showCode = true;
        }
        else
        {
            var code = Net.RecoveryCode();
            Ui.Wrapped(code, Theme.RoseDeep, width);
            if (Ui.Button("##rscopy", "Copy", new Vector2(80, 26), "cream", code.Length > 0)) ImGui.SetClipboardText(code);
            ImGui.SameLine();
            if (Ui.Button("##rshide", "Hide", new Vector2(80, 26), "cream")) showCode = false;
        }

        // ----- Restoring on this install -----
        var me = Plugin.ObjectTable.LocalPlayer;
        var name = me?.Name.TextValue ?? "";
        var world = me?.HomeWorld.ValueNullable?.Name.ExtractText() ?? "";
        var worldId = me?.HomeWorld.RowId ?? 0;

        Heading("Restore with a code");
        Ui.Wrapped("Paste your recovery code (EMR1-...) or the restore key the Discord gave you (MILKR-...). " +
                   "No code? Go to #restore-save in the Eorzea Milkers Discord: it walks you through verifying your character on the Lodestone and gives you a restore key.", Theme.Ink, width);
        if (Ui.Button("##rsdiscord", "Open the Discord", new Vector2(150, 26), "cream", true, Plugin.DiscordUrl)) Plugin.OpenDiscord();
        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##rscode", "EMR1-... or MILKR-...", ref recovery, 120);
        var isKey = NetClient.LooksLikeRestoreKey(recovery);
        string rid = "", rkey = "";
        var readable = !isKey && NetClient.TryReadRecoveryCode(recovery, out rid, out rkey);
        if (recovery.Length > 0 && !readable && !isKey) ImGui.TextColored(Theme.Warn, "That isn't a whole recovery code or restore key yet.");
        if (readable && rid == f.NetId) ImGui.TextColored(Theme.InkSoft, "That's this install's own code: it's already signed in.");
        if (isKey && name.Length == 0) ImGui.TextColored(Theme.Warn, "Log in as the character you verified first.");
        if (Confirmed("rsbycode", "Restore", 110, ((readable && rid != f.NetId) || (isKey && name.Length > 0)) && Net.Active,
                "Brings back your save, dairy and memberships in place of this save. The save it replaces is kept on your PC."))
        {
            busy = true;
            if (isKey)
            {
                var myKey = f.NetKey;
                Net.RestoreRedeem(recovery.Trim(), name, world, worldId, r =>
                {
                    if (!r.Ok) Done(r.Why, false);
                    else
                    {
                        recovery = "";
                        Done(plugin.ApplyRestore(r.Save, r.Prev, r.NetId, myKey) + " You can take the code out of your Lodestone profile now.", true);
                    }
                }, e => Done(e, false));
            }
            else
            {
                Net.GetBackupAs(rid, rkey, r =>
                {
                    if (!r.Found && r.Memberships == 0) Done("That code works, but there's nothing on the server for it (no backup and no dairy).", false);
                    else
                    {
                        recovery = "";
                        Done(plugin.ApplyRestore(r.Save, r.Prev, rid, rkey), true);
                    }
                }, e => Done(e.Contains("key", StringComparison.OrdinalIgnoreCase) ? "That recovery code doesn't match any install." : e, false));
            }
        }

        Heading("Or verify on the Lodestone here");
        Ui.Wrapped($"Prove {(name.Length > 0 ? name : "your character")} is yours without Discord: get a code, put it in your Lodestone Character Profile, then check it here.", Theme.Ink, width);
        if (Ui.Button("##rsget", "Get a code", new Vector2(110, 26), "cream", Net.Active && name.Length > 0 && !busy, "A code for your Lodestone profile. It lasts an hour."))
        {
            busy = true;
            Net.RestoreStart(name, world, r =>
            {
                lodeCode = r.Code;
                Done($"Put {r.Code} anywhere in your Lodestone Character Profile and save it, then press Check.", true);
            }, e => Done(e, false));
        }

        ImGui.SameLine();
        if (Ui.Button("##rslode", "Open my Lodestone profile", new Vector2(210, 26), "cream", true, LodestoneProfile))
            Dalamud.Utility.Util.OpenLink(LodestoneProfile);

        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##rslodecode", "Get a code first", ref lodeCode, 20);
        ImGui.SameLine();
        if (Ui.Button("##rslodecopy", "Copy", new Vector2(70, 26), "cream", lodeCode.Length > 0)) ImGui.SetClipboardText(lodeCode);
        if (Confirmed("rscheck", "Check the Lodestone and restore", 250, Net.Active && name.Length > 0 && lodeCode.Trim().Length >= 13,
                "Checks your Lodestone profile for the code, then brings back your old install's backup, dairy and memberships. The save it replaces is kept on your PC."))
        {
            busy = true;
            var key = f.NetKey;
            Net.RestoreCheck(name, world, worldId, lodeCode.Trim(), r =>
            {
                if (!r.Ok) Done(r.Why, false);
                else
                {
                    lodeCode = "";
                    Done(plugin.ApplyRestore(r.Save, r.Prev, r.NetId, key) + " You can take the code out of your Lodestone profile now.", true);
                }
            }, e => Done(e, false));
        }

        ImGui.Spacing();
        if (Ui.Button("##rsclose", "Close", new Vector2(90, 28), "cream")) IsOpen = false;
    }
}
