using System;
using System.Runtime.InteropServices;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

using GearGuide.Base;

namespace GearGuide.Game;

// Opens the game's own item context menu for a slot. A piece you own gets the
// same menu as right-clicking it in your inventory or on your character (Equip,
// Link, Search for Item...). Any other piece gets the menu of an item link in
// chat (Try On, Link, Search for Item...), which other plugins add entries to too.
internal sealed unsafe class ItemMenu : IDisposable
{
    private GCHandle linkPayload;
    private nint linkData;
    private (uint ItemId, bool Hq, string Name)? pendingLink;

    public void Dispose() => ReleaseLinkData();

    public static void OpenForOwned(OwnedPiece piece, uint addonId)
    {
        var agent = AgentInventoryContext.Instance();
        if (agent != null) agent->OpenForItemSlot(piece.Container, piece.Slot, 0, addonId);
    }

    // Opened on the next framework tick, outside the click event that asked
    // for it, like a click on a chat link.
    public void OpenForItem(uint itemId, bool hq, string name) => pendingLink = (itemId, hq, name);

    public void OnFrameworkUpdate()
    {
        if (pendingLink is not { } link) return;
        pendingLink = null;

        var panelPtr = Services.GameGui.GetAddonByName("ChatLogPanel_0");
        if (panelPtr.IsNull)
        {
            Services.Log.Warning("Can't open the item menu: the chat log isn't available.");
            return;
        }
        var panel = (AddonChatLogPanel*)panelPtr.Address;
        if (panel->LogViewer.ChatText == null) return;

        ReleaseLinkData();
        var payload = new SeString(new ItemPayload(link.ItemId, link.Hq), new TextPayload(link.Name), RawPayload.LinkTerminator).Encode();
        linkPayload = GCHandle.Alloc(payload, GCHandleType.Pinned);
        linkData = Marshal.AllocHGlobal(sizeof(LinkData));
        var data = (LinkData*)linkData;
        *data = new LinkData
        {
            LinkType = (byte)Lumina.Text.Payloads.LinkMacroPayloadType.Item,
            UIntValue1 = link.Hq ? link.ItemId + 1_000_000 : link.ItemId,
            Payload = (byte*)linkPayload.AddrOfPinnedObject(),
            PayloadEnd = checked((ushort)payload.Length),
        };
        // Goes through the chat log's own link handler so the menu is the
        // real one, other plugins' entries included.
        panel->LogViewer.HandleLinkClick(data);
    }

    private void ReleaseLinkData()
    {
        if (linkPayload.IsAllocated) linkPayload.Free();
        if (linkData != 0) Marshal.FreeHGlobal(linkData);
        linkData = 0;
    }
}
