using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;

namespace VelvetDairy.Game;

/// <summary>Adds a "Velvet Dairy" submenu when you right-click a player in the world.</summary>
public sealed class ContextMenuHandler : IDisposable
{
    private readonly Plugin plugin;

    public ContextMenuHandler(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => Plugin.ContextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!plugin.Config.Farm.Confirmed18) return;
        if (args.MenuType != ContextMenuType.Default) return;
        if (args.Target is not MenuTargetDefault target) return;
        if (target.TargetObject is not IPlayerCharacter pc) return;

        var me = Plugin.ObjectTable.LocalPlayer;
        if (me != null && pc.GameObjectId == me.GameObjectId) return;

        // Copy values now; the game object may be gone by the time an item is clicked.
        var name = pc.Name.TextValue;
        var worldId = pc.HomeWorld.RowId;
        var worldName = pc.HomeWorld.ValueNullable?.Name.ExtractText() ?? "";

        args.AddMenuItem(new MenuItem
        {
            Name = "Velvet Dairy",
            PrefixChar = 'V',
            IsSubmenu = true,
            OnClicked = clicked => clicked.OpenSubmenu(Build(name, worldId, worldName)),
        });
    }

    private List<MenuItem> Build(string name, uint worldId, string worldName)
    {
        var game = plugin.Game;
        var items = new List<MenuItem>();
        var fh = game.FindFarmhand(name, worldId);

        if (game.Networked)
        {
            var member = game.MemberNamed(name, worldName);
            if (member == null)
                foreach (var role in game.InvitableRoles)
                    items.Add(Item($"Invite to {game.DairyName} as a {role}", () => game.InvitePlayer(name, worldId, role)));
            else if (game.CanRemove(member))
                items.Add(Item($"Remove from {game.DairyName}", () => game.RemoveMember(member)));

            if (fh != null && game.ProducesMilk)
            {
                if (game.Active == null) items.Add(Item("Let them milk me", () => game.StartHand(fh)));
                else items.Add(Item("Stop milking", () => game.Stop()));
                items.Add(Item("Use their current emote for milking", () => game.RecordEmote(fh)));
            }

            if (items.Count == 0) items.Add(Item(member != null ? $"{member.Name} is a {member.Role} in your dairy" : "Nothing to do here", () => { }));
            return items;
        }

        if (fh == null)
        {
            items.Add(Item("Register as farmhand", () => game.AddFarmhand(name, worldId, worldName)));
            return items;
        }

        if (game.Active == null) items.Add(Item("Let them milk me", () => game.StartHand(fh)));
        else items.Add(Item("Stop milking", () => game.Stop()));
        items.Add(Item("Use their current emote for milking", () => game.RecordEmote(fh)));
        items.Add(Item("Remove farmhand", () => game.RemoveFarmhand(fh)));
        return items;
    }

    private static MenuItem Item(string label, Action action) => new()
    {
        Name = label,
        OnClicked = _ => action(),
    };
}
