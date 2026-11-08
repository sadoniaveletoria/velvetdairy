using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// First-run walkthrough: what the game is, picking a path, and for owners, setting up a dairy step by step
/// (play role, name and listing, marking out the area, the first stall, inviting people).
/// Shows once after the 18+ check; "/vdairy tutorial" runs it again.
/// </summary>
public sealed class TutorialWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;
    private Step step = Step.Welcome;
    private string name = "";

    private enum Step { Welcome, Loop, Path, Join, OwnerRole, OwnerListing, OwnerArea, OwnerStall, OwnerInvite, Done }

    private FarmSave F => plugin.Config.Farm;
    private DairyGame G => plugin.Game;

    private static readonly string[] RoleKeys = { "cow", "farmer", "bull" };
    private static readonly string[] RoleText =
    {
        "Cow: you make milk and get milked in your own dairy.",
        "Farmer: you hand milk your dairy's cows and deliver the milk.",
        "Bull: a role only for now (more is coming).",
    };

    public TutorialWindow(Plugin plugin, Fonts fonts) : base("Welcome to Velvet Dairy##VelvetDairyTutorial", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        RespectCloseHotkey = false;
    }

    public void Start()
    {
        step = Step.Welcome;
        IsOpen = true;
    }

    /// <summary>Opens on its own the first time (after the 18+ check).</summary>
    public void CheckFirstRun()
    {
        if (!IsOpen && F.Confirmed18 && !F.TutorialDone && Plugin.ObjectTable.LocalPlayer != null) Start();
    }

    public override void OnClose()
    {
        // Closing it any time counts as seen; "/vdairy tutorial" brings it back
        if (!F.TutorialDone)
        {
            F.TutorialDone = true;
            plugin.SaveConfig();
        }
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    private void Title(string text)
    {
        using (fonts.For(fonts.Heading, text)!.Push()) ImGui.TextColored(Theme.RoseDeep, text);
        ImGui.Spacing();
    }

    private static void Para(string text)
    {
        ImGui.PushTextWrapPos(520);
        ImGui.TextWrapped(text);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    private static void Soft(string text)
    {
        ImGui.PushTextWrapPos(520);
        ImGui.TextColored(Theme.InkSoft, text);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    private static bool Next(string label = "Next") => ImGui.Button($"{label}##tut", new Vector2(150, 30));

    public override void Draw()
    {
        ImGui.Dummy(new Vector2(530, 0));
        switch (step)
        {
            case Step.Welcome:
                Title("Welcome to Velvet Dairy");
                Para("A dairy life game for adults. You play a cow, a farmer, a bull or the owner of your own dairy, alone or with friends.");
                Soft("Work in progress: expect changes and new things in updates. Nothing is ever said in public chat, and nothing moves your character for you.");
                if (Next()) step = Step.Loop;
                break;

            case Step.Loop:
                Title("How it works");
                Para("1. Cows make milk over time while they play. Eat, drink, rest and have treats (Barn tab) for 30-minute bonuses that make milk come faster.");
                Para("2. Get milked at one of your dairy's stalls (sit on its seat) or by one of its farmers. The dairy pays you for every ounce, more for better grades.");
                Para("3. The milk goes into the dairy's tank. Farmers and the owner deliver it to customers, other dairies and the Grand Creamery's weekly contract.");
                Para("4. You level up as you make milk, and your dairy levels up as its members play together.");
                Soft("Full bottles spoil after 72 hours, so empty them before you log off for a while. Standing still for 30 minutes puts you to sleep (no milk).");
                if (Next()) step = Step.Path;
                break;

            case Step.Path:
                Title("What would you like to do?");
                if (ImGui.Button("Join a dairy as a cow or farmer##tutjoin", new Vector2(320, 34))) step = Step.Join;
                if (ImGui.Button("Start my own dairy (owner)##tutown", new Vector2(320, 34))) step = G.Networked || G.Net.Api != null ? Step.OwnerRole : Step.Join;
                if (ImGui.Button("Just look around##tutlook", new Vector2(320, 34))) step = Step.Done;
                Soft("You can change your mind later: owners can close their dairy, and members can leave theirs.");
                break;

            case Step.Join:
                Title("Joining a dairy");
                Para("Dairies are run by players. An owner (or one of their farmers) invites you as a cow, a farmer or a bull.");
                Para("Find one on the Dairies tab: public dairies show where they are, and Visit flags them on your map. Say hello to the owner and ask for an invite.");
                Para("When someone invites you, it appears on your Dairy tab with Accept and Decline. You can only be in one dairy at a time.");
                if (ImGui.Button("Open the Dairies tab##tutdirs", new Vector2(200, 30))) plugin.MainWindow.OpenTab(MainWindow.TabDairies);
                ImGui.SameLine();
                if (Next("Got it")) step = Step.Done;
                break;

            case Step.OwnerRole:
                Title("Your dairy, step 1: your role");
                Para("As the owner you run the dairy, and you can also play in it. Pick any mix (or none). You can add or take away roles later, and your progress in each is kept.");
                for (var i = 0; i < RoleKeys.Length; i++)
                {
                    var on = F.OwnerRoles!.Contains(RoleKeys[i]);
                    if (ImGui.Checkbox($"{RoleText[i]}##tutrole{i}", ref on))
                    {
                        if (on) F.OwnerRoles.Add(RoleKeys[i]);
                        else F.OwnerRoles.Remove(RoleKeys[i]);
                        F.OwnerRoles = VelvetDairy.Game.Roles.Clean(F.OwnerRoles);
                        plugin.SaveConfig();
                    }
                }

                ImGui.Spacing();
                if (Next()) step = F.NetMember?.IsOwner == true ? Step.OwnerStall : Step.OwnerListing;
                break;

            case Step.OwnerListing:
                Title("Step 2: name and listing");
                ImGui.SetNextItemWidth(300);
                ImGui.InputTextWithHint("##tutname", "Dairy name", ref name, 32);
                var b = F.ListPublic;
                if (ImGui.Checkbox("Public: anyone can see where it is, visit and deliver##tutpub", ref b)) { F.ListPublic = b; plugin.SaveConfig(); }
                b = F.ListShowName;
                if (ImGui.Checkbox("Show my character name as the owner##tutshow", ref b)) { F.ListShowName = b; plugin.SaveConfig(); }
                Soft("You can change all of this later on Dairy > Listing.");
                ImGui.BeginDisabled(name.Trim().Length == 0);
                if (Next()) step = Step.OwnerArea;
                ImGui.EndDisabled();
                break;

            case Step.OwnerArea:
                DrawArea();
                break;

            case Step.OwnerStall:
                Title("Step 4: your first stall");
                Para("A stall is a seat inside your dairy where cows get milked: a chair, bench or bed.");
                Para("Sit on it, open Dairy > Stalls and press Add. The first stall is free; more come from the dairy fund as your dairy levels up.");
                if (G.Stalls.Count > 0) ImGui.TextColored(Theme.SageDeep, $"You have {G.Stalls.Count} stall(s). Nice!");
                if (ImGui.Button("Open Dairy tab##tutstalltab", new Vector2(160, 30))) plugin.MainWindow.OpenTab(MainWindow.TabDairy);
                ImGui.SameLine();
                if (Next(G.Stalls.Count > 0 ? "Next" : "Later")) step = Step.OwnerInvite;
                break;

            case Step.OwnerInvite:
                Title("Step 5: invite people");
                Para("Right-click a player > Velvet Dairy > Invite as a cow, bull or farmer. Or target them and use the invite buttons on Dairy > Overview.");
                Para("Farmers can invite cows and bulls too. Manage members opens a list where you can change roles or remove people.");
                Soft("Every week your dairy gets a creamery contract (Dairy > Contract). Fill it together for dairy XP and fund coins.");
                if (Next()) step = Step.Done;
                break;

            case Step.Done:
                Title("You're all set!");
                Para("The full guide explains everything in more detail: roles, care, pay, levels, your dairy, deliveries and contracts.");
                Soft("Open it any time with the ? in the main window or /vdairy guide. /vdairy tutorial runs this walkthrough again.");
                if (ImGui.Button("Open the guide##tutguide", new Vector2(160, 30))) plugin.Guide.Open();
                ImGui.SameLine();
                if (Next("Start playing"))
                {
                    F.TutorialDone = true;
                    plugin.SaveConfig();
                    plugin.MainWindow.IsOpen = true;
                    IsOpen = false;
                }

                break;
        }
    }

    /// <summary>Step 3 for owners: mark out the dairy where you stand, with the box drawn on the ground.</summary>
    private void DrawArea()
    {
        if (F.NetMember?.IsOwner == true)
        {
            step = Step.OwnerStall;
            return;
        }

        Title("Step 3: where is your dairy?");
        Para("Go to where you want your dairy: a field, a quiet corner of a city, or a room in any house. Stand in the middle of it.");
        var d = G.Draft;
        if (d == null)
        {
            if (ImGui.Button("Start marking here##tutstart", new Vector2(200, 32))) G.StartDraft();
            Soft("A box appears on the ground around you: pink sides, blue front.");
            return;
        }

        ImGui.TextColored(Theme.Ink, $"{(d.Zone.Length > 0 ? d.Zone : "Here")}{(d.House.Length > 0 ? " (inside this house)" : "")}");
        var w = d.Width;
        var l = d.Length;
        var turn = d.Rot * 180f / MathF.PI;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("Width##tutw", ref w, DairyArea.MinSize, DairyArea.MaxSize, "%.0f y")) d.Width = w;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("Length##tutl", ref l, DairyArea.MinSize, DairyArea.MaxSize, "%.0f y")) d.Length = l;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("Turn##tutr", ref turn, -180f, 180f, "%.0f°")) d.Rot = turn * MathF.PI / 180f;
        if (ImGui.SmallButton("Center on me##tutcenter")) G.CenterDraftOnMe();
        ImGui.SameLine();
        if (ImGui.SmallButton("Face my way##tutface")) G.FaceDraftMyWay();
        ImGui.SameLine();
        if (ImGui.SmallButton("Start over##tutcancel")) G.CancelDraft();
        ImGui.Spacing();
        var problem = G.DairyHereProblem();
        ImGui.BeginDisabled(problem != null);
        if (ImGui.Button("Register my dairy##tutreg", new Vector2(200, 32))) G.RegisterDairyHere(name);
        ImGui.EndDisabled();
        if (problem != null) Soft(problem);
        else Soft("It can't overlap another dairy. You can move or resize it later (Dairy > Listing).");
    }
}
