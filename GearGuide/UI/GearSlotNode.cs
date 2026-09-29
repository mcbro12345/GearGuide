using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;

using GearGuide.Game;

namespace GearGuide.UI;

// One equipment slot drawn like the character window's: the item in a
// drag-drop cell with the game's item frame, the slot's silhouette while it's
// empty, and a badge on pieces where you own an upgrade you aren't wearing. Hovering shows the
// game's item tooltip; clicking opens the game's item menu. It's a plain
// container, not a component, so nothing sits between the mouse and the cell.
internal sealed unsafe class GearSlotNode : ResNode
{
    private const string CharacterTexture = "ui/uld/Character.tex";
    private const string IconFrameTexture = "ui/uld/IconA_Frame.tex";
    // The character window keeps a durability bar this wide beside each
    // cell. It isn't drawn here, but the cells keep their places.
    private const float BarWidth = 8.0f;
    private const float CellSize = 44.0f;
    private const float HoverLift = 16.0f / 255.0f;
    // Repeated click events from one click (drag-drop click and mouse click)
    // open the menu once.
    private static readonly TimeSpan ClickDebounce = TimeSpan.FromMilliseconds(150);

    private readonly DragDropNode cell;
    private readonly ImageNode highlight;
    private readonly SimpleImageNode silhouette;
    private readonly SimpleImageNode upgradeBadge;
    private DateTime lastClick;

    public GearSlot Slot { get; }
    public GearChoice? Choice { get; private set; }
    public Action<GearSlotNode>? OnOpenMenu { get; init; }

    // barOnLeft: slots in the left column (and the main hand) had their bar
    // on the left, so their cell sits right of it.
    public GearSlotNode(GearSlot slot, bool barOnLeft, Vector2 silhouettePart)
    {
        Slot = slot;
        Size = new Vector2(CellSize + BarWidth, 48.0f);
        float cellX = barOnLeft ? BarWidth : 0.0f;

        cell = new DragDropNode
        {
            Position = new Vector2(cellX, 1.0f),
            Size = new Vector2(CellSize, CellSize),
            IsDraggable = false,
            IsClickable = true,
            AcceptedType = DragDropType.Nothing,
        };
        cell.IconId = 0;
        // Every slot shows its name, as in the character window; a slot with a
        // piece in it shows the item tooltip alongside.
        cell.TextTooltip = GearSlots.TooltipName(slot);
        cell.OnRollOver = _ => Hover();
        cell.OnRollOut = _ => EndHover();
        // Plain mouse-over and mouse-out too, since the drag-drop roll-over
        // events don't always arrive.
        cell.AddEvent(AtkEventType.MouseOver, Hover);
        cell.AddEvent(AtkEventType.MouseOut, EndHover);
        cell.AddEvent(AtkEventType.DragDropClick, Click);
        cell.AddEvent(AtkEventType.MouseClick, Click);
        cell.AttachNode(this);

        // The game's own hovered-slot glow: IconA_Frame part 16, drawn 72x72
        // and offset so it frames the 44px cell.
        highlight = new ImageNode
        {
            Position = new Vector2(cellX - 14.0f, 1.0f - 12.0f),
            Size = new Vector2(72.0f, 72.0f),
            PartId = 16,
            WrapMode = KamiToolKit.Enums.WrapMode.Tile,
            IsVisible = false,
        };
        IconNodeTextureHelper.LoadIconAFrameTexture(highlight);
        highlight.AttachNode(this);

        // The character window draws the silhouette over the empty cell.
        silhouette = new SimpleImageNode
        {
            Position = new Vector2(cellX + 6.0f, 7.0f),
            Size = new Vector2(32.0f, 32.0f),
            TextureCoordinates = silhouettePart,
            TextureSize = new Vector2(32.0f, 32.0f),
        };
        silhouette.LoadTexture(CharacterTexture);
        silhouette.AttachNode(this);

        upgradeBadge = new SimpleImageNode
        {
            Position = new Vector2(cellX + 27.0f, -1.0f),
            Size = new Vector2(18.0f, 18.0f),
            TextureCoordinates = new Vector2(360.0f, 114.0f),
            TextureSize = new Vector2(18.0f, 18.0f),
            IsVisible = false,
        };
        upgradeBadge.LoadTexture(IconFrameTexture);
        upgradeBadge.AttachNode(this);
    }

    // ownedUpgrade: you own something better for this slot than what you wear.
    public void SetChoice(GearChoice? choice, bool blocked, bool ownedUpgrade)
    {
        Choice = choice;
        cell.IconId = choice == null ? 0 : choice.Hq ? choice.Item.Icon + 1_000_000 : choice.Item.Icon;
        silhouette.IsVisible = choice == null;
        silhouette.Alpha = blocked ? 0.35f : 1.0f;

        // Every piece gets the plain item tooltip (as for an item link). The
        // inventory-slot tooltip would add condition and spiritbond bars.
        cell.ItemTooltip = choice?.TooltipItemId ?? 0;

        upgradeBadge.IsVisible = ownedUpgrade;
    }

    private void Hover()
    {
        // KamiToolKit anchors the tooltip to the cell, so it sits in the same
        // place every time.
        cell.ShowTooltip();
    }

    private void EndHover()
    {
        cell.HideTooltip();
    }

    // Lights the slot while the cursor is over it: the glow around an item, or
    // a brighter silhouette on an empty slot, as in the character window.
    // Checked every frame, since the mouse-over and mouse-out events can go
    // missing (when the tooltip or the item menu opens, say) and leave it on
    // or off.
    public void UpdateHighlight(Vector2 cursor)
    {
        bool hovered = cell.CheckCollision(cursor);
        highlight.IsVisible = Choice != null && hovered;
        // The character window's drag-drop cell lifts its frame by 16/255 on
        // hover (DragDrop timeline label 2 in Character.uld); the silhouette
        // drawn over it here gets the same lift so the whole slot brightens.
        var lift = Choice == null && hovered ? new Vector3(HoverLift) : Vector3.Zero;
        cell.DragDropBackgroundNode.AddColor = lift;
        silhouette.AddColor = lift;
    }

    private void Click(AtkEventListener* listener, AtkEventType type, int param, AtkEvent* atkEvent, AtkEventData* data)
    {
        if (Choice == null || DateTime.UtcNow - lastClick < ClickDebounce) return;
        lastClick = DateTime.UtcNow;
        cell.HideTooltip();
        OnOpenMenu?.Invoke(this);
    }
}
