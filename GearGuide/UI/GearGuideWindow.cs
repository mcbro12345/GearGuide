using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using Lumina.Text;

using GearGuide.Base;
using GearGuide.Game;

namespace GearGuide.UI;

// The gear half of the character window, rebuilt from the game's own layout
// (Character.uld): the class header, the equipment slots down both sides and
// the item level over the middle panel. The middle panel, where the game shows
// your character, lists the recommended set's stats against what you wear,
// and the round toggles under it pick which sources recommendations may come
// from. Positions below are the character window's, in its gear panel's
// coordinates.
internal sealed unsafe class GearGuideWindow : NativeAddon
{
    public const float PanelWidth = 332.0f;
    public const float PanelHeight = 466.0f;

    private const string CharacterTexture = "ui/uld/Character.tex";
    private const string GearSetTexture = "ui/uld/CharacterGearSet.tex";
    private const string BgPartsTexture = "ui/uld/BgParts.tex";

    private static readonly Vector4 BeigeText = Rgba(0xEE, 0xE1, 0xC5);
    private static readonly Vector4 GreyText = Rgba(0xA0, 0xA0, 0xA0);
    private static readonly Vector4 GoldText = Rgba(0xCF, 0xB2, 0x76);
    private static readonly Vector4 ItemLevelText = Rgba(0x5C, 0xC8, 0xC8);
    private static readonly Vector4 BetterText = Rgba(0x7E, 0xD9, 0x4A);
    private static readonly Vector4 WorseText = Rgba(0xE0, 0x5A, 0x5A);
    private static readonly Vector4 Edge = new(0.0f, 0.0f, 0.0f, 1.0f);

    // (slot, position, bar on the left, silhouette in Character.tex)
    private static readonly (GearSlot Slot, Vector2 Position, bool BarLeft, Vector2 Silhouette)[] SlotLayout =
    [
        (GearSlot.MainHand, new(4, 47), true, new(0, 72)),
        (GearSlot.Head, new(4, 108), true, new(64, 72)),
        (GearSlot.Body, new(4, 155), true, new(96, 72)),
        (GearSlot.Hands, new(4, 202), true, new(128, 72)),
        (GearSlot.Legs, new(4, 249), true, new(192, 72)),
        (GearSlot.Feet, new(4, 296), true, new(0, 104)),
        (GearSlot.OffHand, new(274, 108), false, new(32, 72)),
        (GearSlot.Ears, new(274, 155), false, new(32, 104)),
        (GearSlot.Neck, new(274, 202), false, new(64, 104)),
        (GearSlot.Wrists, new(274, 249), false, new(96, 104)),
        (GearSlot.RingRight, new(274, 296), false, new(128, 104)),
        (GearSlot.RingLeft, new(274, 343), false, new(128, 104)),
    ];

    private readonly Configuration configuration;
    private readonly Dictionary<GearSlot, GearSlotNode> slots = new();
    private readonly List<CircleToggleNode> toggles = new();
    private Vector2 origin;
    private GearGuideView? view;

    private TextureButtonNode? equipButton;
    private TextNode? summaryText;
    private TextNode? levelText;
    private TextNode? jobText;
    private IconImageNode? jobIcon;
    private SimpleImageNode? itemLevelStar;
    private TextNode? itemLevelText;
    private TextNode? statNames;
    private TextNode? statValues;

    public GearGuideWindow(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public Action? OnFiltersChanged { get; init; }
    public Action? OnEquip { get; init; }
    public Action? OnRefreshClicked { get; init; }
    public Action<GearSlotNode>? OnOpenMenu { get; init; }

    public bool CanEquip { get; set; }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        origin = ContentStartPosition + new Vector2(MathF.Max(0.0f, (ContentSize.X - PanelWidth) / 2.0f), -4.0f);

        BuildTopBar();
        BuildHeader();
        BuildMiddlePanel();
        BuildSlots();
        BuildToggles();

        if (view != null) Apply(view);
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        slots.Clear();
        toggles.Clear();
        equipButton = null;
        summaryText = levelText = jobText = itemLevelText = statNames = statValues = null;
        jobIcon = null;
        itemLevelStar = null;
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        if (equipButton != null && equipButton.IsEnabled != CanEquip) equipButton.IsEnabled = CanEquip;

        var framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
        if (framework == null) return;
        var cursor = new Vector2(framework->CursorInputs.PositionX, framework->CursorInputs.PositionY);
        foreach (var node in slots.Values) node.UpdateHighlight(cursor);
    }

    public void Show(GearGuideView newView)
    {
        view = newView;
        if (IsOpen && statValues != null) Apply(newView);
    }

    // The row above the header: the Recommended Gear button, a title and
    // summary where the gear set name goes, and a refresh button.
    private void BuildTopBar()
    {
        equipButton = new TextureButtonNode
        {
            Position = origin + new Vector2(0, 6),
            Size = new Vector2(32, 32),
            TexturePath = GearSetTexture,
            TextureCoordinates = new Vector2(64, 0),
            TextureSize = new Vector2(32, 32),
            TextTooltip = "Equip Recommended Gear\nPuts on every recommended piece you own.",
            OnClick = () => OnEquip?.Invoke(),
            IsEnabled = false,
        };
        equipButton.AttachNode(this);

        Text(new Vector2(38, 3), new Vector2(220, 15), FontType.MiedingerMed, 12, GreyText, AlignmentType.Left, "Recommended Gear");
        summaryText = Text(new Vector2(38, 18), new Vector2(256, 15), FontType.Axis, 14, BeigeText, AlignmentType.Left, "");

        var refresh = new TextureButtonNode
        {
            Position = origin + new Vector2(300, 6),
            Size = new Vector2(32, 32),
            TexturePath = GearSetTexture,
            TextureCoordinates = new Vector2(32, 0),
            TextureSize = new Vector2(32, 32),
            TextTooltip = "Refresh\nRe-reads your gear and asks the market board again.",
            OnClick = () => OnRefreshClicked?.Invoke(),
        };
        refresh.AttachNode(this);
    }

    private void BuildHeader()
    {
        var background = new SimpleNineGridNode
        {
            Position = origin + new Vector2(1, 44),
            Size = new Vector2(330, 56),
            TexturePath = BgPartsTexture,
            TextureCoordinates = new Vector2(61, 37),
            TextureSize = new Vector2(16, 16),
            Offsets = new Vector4(7, 7, 7, 7),
        };
        background.AttachNode(this);

        levelText = Text(new Vector2(60, 46), new Vector2(200, 20), FontType.MiedingerMed, 12, BeigeText, AlignmentType.Left, "");

        // The frame's middle is filled, so the icon goes on after it.
        var iconFrame = new SimpleNineGridNode
        {
            Position = origin + new Vector2(55, 60),
            Size = new Vector2(34, 34),
            TexturePath = BgPartsTexture,
            TextureCoordinates = new Vector2(1, 33),
            TextureSize = new Vector2(32, 32),
            Offsets = new Vector4(8, 8, 8, 8),
        };
        iconFrame.AttachNode(this);

        jobIcon = new IconImageNode
        {
            Position = origin + new Vector2(58, 65),
            Size = new Vector2(28, 28),
            FitTexture = true,
        };
        jobIcon.AttachNode(this);

        jobText = Text(new Vector2(89, 69), new Vector2(180, 28), FontType.Jupiter, 23, GoldText, AlignmentType.Left, "");
    }

    // The frame the game shows your character in, here holding the stats of
    // the recommended set, with the average item level in its corner.
    private void BuildMiddlePanel()
    {
        var frame = new SimpleNineGridNode
        {
            Position = origin + new Vector2(65, 109),
            Size = new Vector2(200, 328),
            TexturePath = "ui/uld/PreviewA.tex",
            TextureCoordinates = Vector2.Zero,
            TextureSize = new Vector2(36, 36),
            Offsets = new Vector4(14, 14, 14, 14),
        };
        frame.AttachNode(this);

        itemLevelStar = new SimpleImageNode
        {
            Position = origin + new Vector2(179, 110),
            Size = new Vector2(24, 24),
            TextureCoordinates = new Vector2(176, 136),
            TextureSize = new Vector2(24, 24),
            TextTooltip = "Average item level of the recommended gear",
        };
        itemLevelStar.LoadTexture(CharacterTexture);
        itemLevelStar.AttachNode(this);
        itemLevelText = Text(new Vector2(177, 109), new Vector2(80, 24), FontType.Miedinger, 16, ItemLevelText, AlignmentType.Right, "");

        statNames = Text(new Vector2(78, 144), new Vector2(110, 240), FontType.Axis, 12, BeigeText, AlignmentType.TopLeft, "");
        statNames.LineSpacing = 18;
        statNames.TextFlags |= TextFlags.MultiLine;
        statValues = Text(new Vector2(150, 144), new Vector2(104, 240), FontType.Axis, 12, BeigeText, AlignmentType.TopRight, "");
        statValues.LineSpacing = 18;
        statValues.TextFlags |= TextFlags.MultiLine;
    }

    private void BuildSlots()
    {
        foreach (var (slot, position, barLeft, silhouette) in SlotLayout)
        {
            var node = new GearSlotNode(slot, barLeft, silhouette)
            {
                Position = origin + position,
                OnOpenMenu = node => OnOpenMenu?.Invoke(node),
            };
            node.AttachNode(this);
            slots[slot] = node;
        }
    }

    // The round toggles under the middle panel.
    private void BuildToggles()
    {
        // The game wraps tooltips itself, so each has a title line and then
        // one unbroken description.
        AddToggle(new Vector2(60, 432), new Vector2(224, 0), 28, new Vector4(3.5f, 3.5f, 24.5f, 25.0f), "Owned\nGear you're wearing or have in your inventory, armoury chest or saddlebag.",
            () => configuration.IncludeOwned, value => configuration.IncludeOwned = value);
        AddToggle(new Vector2(84, 432), new Vector2(196, 84), 28, new Vector4(3.5f, 3.5f, 24.5f, 25.0f), "Vendors\nGear sold for gil by NPC vendors.",
            () => configuration.IncludeVendor, value => configuration.IncludeVendor = value);
        AddToggle(new Vector2(108, 432), new Vector2(24, 112), 24, new Vector4(3.5f, 3.5f, 20.5f, 21.0f), "Crafted\nGear with a crafting recipe, counted as high quality.",
            () => configuration.IncludeCrafted, value => configuration.IncludeCrafted = value);
        AddToggle(new Vector2(132, 432), new Vector2(0, 112), 24, new Vector4(3.5f, 3.5f, 20.5f, 21.0f), "Market Board\nGear you can buy from other players.",
            () => configuration.IncludeMarket, value => configuration.IncludeMarket = value);
        AddToggle(new Vector2(156, 432), new Vector2(168, 28), 28, new Vector4(1.5f, 1.5f, 26.0f, 26.0f), "Other Sources\nGear you can't buy or craft, like dungeon drops, tomestone and seal exchanges and quest rewards.",
            () => configuration.IncludeOther, value => configuration.IncludeOther = value);
        AddToggle(new Vector2(214, 432), new Vector2(112, 56), 28, new Vector4(3.5f, 3.5f, 23.5f, 24.0f), "Live Listings\nMarket Board only counts gear listed on your data center right now, checked with Universalis.",
            () => configuration.CheckLiveListings, value => configuration.CheckLiveListings = value);
    }

    private void AddToggle(Vector2 position, Vector2 iconPart, float iconSize, Vector4 plate, string tooltip, Func<bool> get, Action<bool> set)
    {
        var toggle = new CircleToggleNode(iconPart, iconSize, plate, tooltip)
        {
            Position = origin + position,
            OnToggle = value =>
            {
                set(value);
                configuration.Save();
                OnFiltersChanged?.Invoke();
            },
        };
        toggle.IsChecked = get();
        toggle.AttachNode(this);
        toggles.Add(toggle);
    }

    private void Apply(GearGuideView current)
    {
        var profile = current.Profile;
        var plan = current.Plan;

        levelText!.String = $"LEVEL {profile.Level}";
        jobText!.String = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(profile.JobName);
        jobIcon!.IconId = 62100 + profile.JobId;

        var upgradeSlots = plan.Upgrades().Select(upgrade => upgrade.Slot).ToHashSet();
        foreach (var (slot, node) in slots)
            node.SetChoice(plan.AlreadyWorn.Contains(slot) ? null : plan.Picks.GetValueOrDefault(slot),
                plan.Blocked.ContainsKey(slot), upgradeSlots.Contains(slot));

        itemLevelText!.String = plan.AverageItemLevel.ToString("D4");
        float textWidth = itemLevelText.GetTextDrawSize().X;
        itemLevelStar!.X = itemLevelText.X + itemLevelText.Width - textWidth - 25.0f;

        int upgrades = upgradeSlots.Count;
        summaryText!.String = upgrades > 0
            ? $"{upgrades} owned upgrade{(upgrades == 1 ? "" : "s")} ready to equip"
            : "You're wearing the best you own";

        var names = new SeStringBuilder();
        var numbers = new SeStringBuilder();
        foreach (var (line, index) in current.Stats.Select((line, index) => (line, index)))
        {
            if (index > 0)
            {
                names.Append("\n");
                numbers.Append("\n");
            }
            names.Append(line.Name);
            numbers.Append(line.Recommended.ToString("N0"));
            int delta = line.Recommended - line.Current;
            if (delta != 0)
            {
                numbers.PushColorRgba(delta > 0 ? BetterText : WorseText);
                numbers.Append(delta > 0 ? $" +{delta:N0}" : $" {delta:N0}");
                numbers.PopColor();
            }
        }
        statNames!.String = names.ToReadOnlySeString();
        statValues!.String = numbers.ToReadOnlySeString();
    }

    private TextNode Text(Vector2 position, Vector2 size, FontType font, uint fontSize, Vector4 color, AlignmentType alignment, string text)
    {
        var node = new TextNode
        {
            Position = origin + position,
            Size = size,
            FontType = font,
            FontSize = fontSize,
            TextColor = color,
            TextOutlineColor = Edge,
            AlignmentType = alignment,
            TextFlags = TextFlags.Emboss,
            String = text,
        };
        node.AttachNode(this);
        return node;
    }

    private static Vector4 Rgba(byte r, byte g, byte b) => new(r / 255.0f, g / 255.0f, b / 255.0f, 1.0f);
}
