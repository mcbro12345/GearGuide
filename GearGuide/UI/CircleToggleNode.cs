using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes.Simplified;

namespace GearGuide.UI;

// A round toggle like the display toggles under the character window's model
// (weapon, headgear...): a round icon that gets a gold ring while it's on.
internal sealed unsafe class CircleToggleNode : SimpleComponentNode
{
    private const string CircleButtonsTexture = "ui/uld/CircleButtons.tex";
    // The generic UI click sound.
    private const uint ClickSoundEffectId = 1;

    private readonly SimpleImageNode icon;
    private readonly SimpleImageNode ring;
    private bool isChecked;

    public Action<bool>? OnToggle { get; init; }

    // The ring's centre (measured from CircleButtons.tex, ring drawn at 2,2)
    // and how wide every icon's round plate is drawn, so each plate fills the
    // ring the way the widest icon's (the dice) does.
    private static readonly Vector2 RingCentre = new(2.0f + 13.9f, 2.0f + 14.0f);
    private const float PlateDiameter = 21.0f;

    // iconPart/iconSize: the icon's rectangle in CircleButtons.tex (24 or 28).
    // plate: where the icon's round plate sits inside that rectangle (left,
    // top, right, bottom), since the plates vary in size from icon to icon.
    public CircleToggleNode(Vector2 iconPart, float iconSize, Vector4 plate, string tooltip)
    {
        Size = new Vector2(32.0f, 32.0f);

        float scale = PlateDiameter / (plate.Z - plate.X);
        var plateCentre = new Vector2(plate.X + plate.Z, plate.Y + plate.W) / 2.0f;
        icon = new SimpleImageNode
        {
            Position = RingCentre - plateCentre * scale,
            Size = new Vector2(iconSize * scale, iconSize * scale),
            TextureCoordinates = iconPart,
            TextureSize = new Vector2(iconSize, iconSize),
            WrapMode = WrapMode.Stretch,
        };
        icon.LoadTexture(CircleButtonsTexture);
        icon.AttachNode(this);

        ring = new SimpleImageNode
        {
            Position = new Vector2(2.0f, 2.0f),
            Size = new Vector2(28.0f, 28.0f),
            TextureCoordinates = new Vector2(84.0f, 84.0f),
            TextureSize = new Vector2(28.0f, 28.0f),
        };
        ring.LoadTexture(CircleButtonsTexture);
        ring.AttachNode(this);

        CollisionNode.Position = new Vector2(3.0f, 3.0f);
        CollisionNode.Size = new Vector2(26.0f, 26.0f);
        CollisionNode.ShowClickableCursor = true;
        CollisionNode.TextTooltip = tooltip;
        CollisionNode.AddEvent(AtkEventType.MouseClick, () =>
        {
            UIGlobals.PlaySoundEffect(ClickSoundEffectId);
            IsChecked = !IsChecked;
            OnToggle?.Invoke(IsChecked);
        });
        CollisionNode.AddEvent(AtkEventType.MouseOver, () => icon.AddColor = new Vector3(0.1f, 0.1f, 0.1f));
        CollisionNode.AddEvent(AtkEventType.MouseOut, () => icon.AddColor = Vector3.Zero);
        IsChecked = false;
    }

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            isChecked = value;
            ring.IsVisible = value;
            icon.MultiplyColor = value ? Vector3.One : new Vector3(0.6f, 0.6f, 0.6f);
        }
    }
}
