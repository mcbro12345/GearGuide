using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

using GearGuide.Base;

namespace GearGuide.UI;

// Options that don't belong on the Gear Guide window itself. The source
// filters live on the window, as round toggles.
internal sealed class SettingsWindow : Window
{
    private readonly Configuration configuration;

    public SettingsWindow(Configuration configuration)
        : base("Gear Guide Settings###GearGuideSettings")
    {
        this.configuration = configuration;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 120),
            MaximumSize = new Vector2(720, 600),
        };
    }

    public override void Draw()
    {
        Section("CHARACTER WINDOW");
        Toggle("Recommended Gear button opens Gear Guide", configuration.ReplaceRecommendedGearButton,
            v => configuration.ReplaceRecommendedGearButton = v,
            "The Recommended Gear button in the game's Character window opens Gear Guide instead of the game's Recommended Gear window.");
    }

    private void Toggle(string label, bool value, Action<bool> set, string? tooltip = null)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            configuration.Save();
        }
        if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
    }

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(title);
        ImGui.Separator();
    }
}
