using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VelvetDairy.Game;

namespace VelvetDairy;

/// <summary>
/// What the plugin did, for helping players: every line goes to Dalamud's log (/xllog, dalamud.log) at Information
/// level, so it's there without changing any setting, and the last few hundred are kept so "Copy support info"
/// (Settings > Help, or /milkers support) can hand a player one block of text to paste in #bug-reports.
/// Never logs secrets (the network key, the developer phrase).
/// </summary>
public static class Diag
{
    private const int Keep = 400;
    private static readonly LinkedList<string> lines = new();
    private static readonly object gate = new();

    private static void Add(string line)
    {
        lock (gate)
        {
            lines.AddLast($"{DateTime.Now:HH:mm:ss} {line}");
            while (lines.Count > Keep) lines.RemoveFirst();
        }
    }

    public static void Info(string area, string text)
    {
        Add($"[{area}] {text}");
        Plugin.Log.Information($"[{area}] {text}");
    }

    public static void Warn(string area, string text, Exception? e = null)
    {
        Add($"[{area}] WARN {text}{(e != null ? $" ({e.GetType().Name}: {e.Message})" : "")}");
        if (e != null) Plugin.Log.Warning(e, $"[{area}] {text}");
        else Plugin.Log.Warning($"[{area}] {text}");
    }

    /// <summary>One block of text describing this player's setup and what happened recently.</summary>
    public static string Report(Plugin p)
    {
        var f = p.Config.Farm;
        var g = p.Game;
        var n = p.Net;
        var me = Plugin.ObjectTable.LocalPlayer;
        var sb = new StringBuilder();
        void L(string s) => sb.AppendLine(s);

        L("=== Eorzea Milkers support info ===");
        L($"Plugin {Plugin.Version}{(p.Dev ? " (dev)" : "")} · Dalamud {typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly.GetName().Version} · {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        L($"Character: {(me == null ? "not logged in" : $"{me.Name.TextValue} ({me.HomeWorld.ValueNullable?.Name.ExtractText()}), now on {me.CurrentWorld.ValueNullable?.Name.ExtractText()}")}");
        L($"Save: {Short(p.Config.ActiveKey)} · {p.Config.Characters.Count} character save(s){(p.Config.Unclaimed != null ? " · an old save is still unclaimed" : "")} · id {Short(f.NetId)}");
        L($"Network: {(f.NetEnabled ? "on" : "off")} · {n.Status} · server {(n.Api != null ? "found" : "not found")} · {(n.Connected ? "connected" : "not connected")}");
        var m = f.NetMember;
        L(m == null ? "Dairy: none" : $"Dairy: {m.Dairy}{(m.IsOwner ? " (owner)" : "")} · roles {string.Join(", ", g.MyRoles)}");
        L($"Levels: cow {g.Level}, farmer {g.FarmerLevel}, bull {g.BullLevel} · grade {Grades.Name(g.CurrentGrade)} · milk {f.Cow.Milk:0.#}/{g.Capacity:0} oz · coins {f.Coins:N0} · earned today {f.DayEarned:N0}");
        L($"Where: territory {Plugin.ClientState.TerritoryType} · in dairy: {(g.Where.InDairy ? "yes" : "no")} ({g.Where.Why}) · wake {g.Wake}");
        L($"Cow settings: stalls start milking {f.AutoStall} · farmers milk without asking {f.AutoHand} · any emote counts {f.AnyLoopEmote}");
        L($"Milk button: does {g.MilkEmoteFor(null)} · own pick '{f.MilkButtonEmote}' · Penumbra {(PenumbraLink.Available ? "yes" : "no")}");
        if (f.MilkModDir.Length > 0)
        {
            L($"Animation mod: {PenumbraLink.NameOf(f.MilkModDir)} · redraw {f.MilkModRedraw} · emote from mod: {(g.MilkModPick is { } pick ? $"{pick.Name} {pick.Command}" : "none found")}");
            foreach (var (group, opts) in f.MilkModOptions) L($"  option {group}: {string.Join(" | ", opts)}");
        }

        L("");
        L("--- Recent ---");
        lock (gate) foreach (var line in lines) L(line);
        return sb.ToString();
    }

    private static string Short(string? s) => string.IsNullOrEmpty(s) ? "-" : s.Length <= 8 ? s : s[..8];
}
