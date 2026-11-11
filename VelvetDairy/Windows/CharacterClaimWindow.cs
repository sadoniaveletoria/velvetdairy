using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VelvetDairy.Game;

namespace VelvetDairy.Windows;

/// <summary>
/// Saves used to be one per PC; now they're one per character. The first time a character without a save logs in
/// while the old save is still unclaimed, this asks whether the old progress is theirs. Nothing else draws until it's answered.
/// </summary>
public sealed class CharacterClaimWindow : Window
{
    private readonly Plugin plugin;
    private readonly Fonts fonts;

    /// <summary>The character being asked about.</summary>
    public string Key { get; private set; } = "";
    private string who = "";

    public CharacterClaimWindow(Plugin plugin, Fonts fonts)
        : base("Eorzea Milkers: whose progress?##VelvetDairyClaim", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking)
    {
        this.plugin = plugin;
        this.fonts = fonts;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        PositionCondition = ImGuiCond.Appearing;
    }

    public void Ask(string key, string name, string world)
    {
        Key = key;
        who = world.Length > 0 ? $"{name} ({world})" : name;
        IsOpen = true;
    }

    public override void PreDraw() => PaperStyle.Push();

    public override void PostDraw() => PaperStyle.Pop();

    public override void Draw()
    {
        var old = plugin.Config.Unclaimed;
        if (old == null)
        {
            IsOpen = false;
            return;
        }

        const string title = "Progress is now saved per character";
        ImGui.PushTextWrapPos(460);
        using (fonts.For(fonts.Heading, title)!.Push()) ImGui.TextColored(Theme.RoseDeep, title);
        ImGui.Spacing();
        ImGui.TextColored(Theme.Ink, "Eorzea Milkers used to keep one save for every character on this PC. Now each character has their own: their own levels, coins, roles and dairy.");
        ImGui.Spacing();

        var level = Leveling.FromXp(old.Xp).Level;
        var dairy = old.NetMember?.Dairy ?? old.Dairy?.Name;
        var summary = $"Your progress so far: cow level {level}, {old.Coins:N0} coins" + (string.IsNullOrEmpty(dairy) ? "." : $", in {dairy}.");
        ImGui.TextColored(Theme.InkSoft, summary);
        ImGui.Spacing();
        ImGui.TextColored(Theme.Ink, $"Is {who} the character you've been playing with?");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        if (Ui.Button("##claimyes", "Yes, it's this one", new Vector2(220, 34), "sage", true, "Your progress, roles and dairy move to this character."))
        {
            plugin.AnswerClaim(Key, true);
            IsOpen = false;
        }

        ImGui.SameLine();
        if (Ui.Button("##claimno", "No, start fresh here", new Vector2(220, 34), "cream", true,
                "This character starts as a new player. Your progress waits for the next character you log in with, and you'll be asked again then."))
        {
            plugin.AnswerClaim(Key, false);
            IsOpen = false;
        }
    }
}
