using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.Sheets;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CharacterModes = FFXIVClientStructs.FFXIV.Client.Game.Character.CharacterModes;

namespace VelvetDairy.Game;

/// <summary>Emote lookups by text command (/pet, /hug...) and reading what a character is doing.</summary>
public static class Emotes
{
    private static Dictionary<string, ushort>? byCommand;

    private static Dictionary<string, ushort> Map()
    {
        if (byCommand != null) return byCommand;

        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Plugin.DataManager.GetExcelSheet<Emote>())
        {
            if (e.RowId == 0) continue;
            var id = (ushort)e.RowId;

            var tc = e.TextCommand.ValueNullable;
            if (tc != null)
            {
                Add(map, tc.Value.Command.ExtractText(), id);
                Add(map, tc.Value.ShortCommand.ExtractText(), id);
            }

            var name = e.Name.ExtractText().Replace(" ", "");
            if (name.Length > 0) Add(map, "/" + name, id);
        }

        byCommand = map;
        return map;
    }

    private static void Add(Dictionary<string, ushort> map, string cmd, ushort id)
    {
        cmd = cmd.Trim();
        if (cmd.Length > 1) map.TryAdd(cmd, id);
    }

    /// <summary>Emote id for a text command, or 0 if the game doesn't have it.</summary>
    public static ushort Find(string command)
    {
        command = command.Trim();
        if (!command.StartsWith('/')) command = "/" + command;
        return Map().TryGetValue(command, out var id) ? id : (ushort)0;
    }

    public static string Name(ushort id)
    {
        if (id == 0) return "none";
        var row = Plugin.DataManager.GetExcelSheet<Emote>().GetRowOrDefault(id);
        var name = row?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? $"Emote {id}" : name;
    }

    /// <summary>Text command for an emote id ("/pet"), or "" if it has none.</summary>
    public static string Command(ushort id)
    {
        var row = Plugin.DataManager.GetExcelSheet<Emote>().GetRowOrDefault(id);
        var tc = row?.TextCommand.ValueNullable;
        return tc?.Command.ExtractText() ?? "";
    }

    /// <summary>The emote a character is doing right now (0 = none).</summary>
    public static unsafe ushort Current(IGameObject? o)
    {
        if (o is not ICharacter || o.Address == nint.Zero) return 0;
        return ((CSCharacter*)o.Address)->EmoteController.EmoteId;
    }

    /// <summary>Sitting on furniture (chair, bed, bench).</summary>
    public static unsafe bool InPositionLoop(IGameObject? o)
    {
        if (o is not ICharacter || o.Address == nint.Zero) return false;
        return ((CSCharacter*)o.Address)->Mode == CharacterModes.InPositionLoop;
    }

    public static unsafe string Mode(IGameObject? o)
    {
        if (o is not ICharacter || o.Address == nint.Zero) return "-";
        return ((CSCharacter*)o.Address)->Mode.ToString();
    }

    /// <summary>
    /// Does an emote for you, exactly as if you typed it (only ever from a button you pressed, like Milk).
    /// Returns false when the text isn't a known emote command.
    /// </summary>
    public static unsafe bool Perform(string command)
    {
        var cmd = command.Trim();
        if (!cmd.StartsWith('/')) cmd = "/" + cmd;
        if (Find(cmd) == 0) return false;
        var ui = FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance();
        if (ui == null) return false;
        var text = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String.FromString(cmd);
        ui->ProcessChatBoxEntry(text);
        text->Dtor(true);
        return true;
    }
}
