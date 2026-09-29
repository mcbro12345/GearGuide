using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

using GearGuide.Base;

namespace GearGuide.Game;

// When switched on, the Recommended Gear button in the game's Character
// window opens Gear Guide instead of the game's Recommended Gear window.
internal sealed unsafe class RecommendedGearRedirect : IDisposable
{
    private const string CharacterAddon = "Character";
    // The Recommended Gear button's node in Character.uld.
    private const uint RecommendedGearButtonNodeId = 12;
    // How far up from the clicked node to look for the button (the click can
    // land on one of the button's own parts).
    private const int MaxParentDepth = 4;

    private readonly Configuration configuration;
    private readonly Action openGearGuide;

    public RecommendedGearRedirect(Configuration configuration, Action openGearGuide)
    {
        this.configuration = configuration;
        this.openGearGuide = openGearGuide;
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, CharacterAddon, OnReceiveEvent);
    }

    public void Dispose() => Services.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, CharacterAddon, OnReceiveEvent);

    private void OnReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (!configuration.ReplaceRecommendedGearButton || args is not AddonReceiveEventArgs receive) return;
        if ((AtkEventType)receive.AtkEventType != AtkEventType.ButtonClick) return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        var atkEvent = (AtkEvent*)receive.AtkEvent;
        if (addon == null || atkEvent == null) return;
        var button = addon->GetNodeById(RecommendedGearButtonNodeId);
        if (button == null || !IsFrom(atkEvent, button))
        {
            Services.Log.Debug($"Character button click not redirected: param {receive.EventParam}, node {(nint)atkEvent->Node:X}, button {(nint)button:X}.");
            return;
        }

        receive.PreventOriginal();
        openGearGuide();
    }

    private static bool IsFrom(AtkEvent* atkEvent, AtkResNode* button)
    {
        if (atkEvent->Target == (AtkEventTarget*)button) return true;
        var component = button->GetComponent();
        if (component != null && atkEvent->Target == (AtkEventTarget*)component) return true;
        var node = atkEvent->Node;
        for (int depth = 0; node != null && depth <= MaxParentDepth; depth++, node = node->ParentNode)
            if (node == button) return true;
        return false;
    }
}
