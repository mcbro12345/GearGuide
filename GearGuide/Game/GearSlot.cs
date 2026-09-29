namespace GearGuide.Game;

// The equipment slots, numbered like the game's equipped-items container so a
// slot doubles as its equip index. Waist (5) no longer exists in the game.
public enum GearSlot
{
    MainHand = 0,
    OffHand = 1,
    Head = 2,
    Body = 3,
    Hands = 4,
    Legs = 6,
    Feet = 7,
    Ears = 8,
    Neck = 9,
    Wrists = 10,
    RingRight = 11,
    RingLeft = 12,
}

internal static class GearSlots
{
    public static readonly GearSlot[] All =
    [
        GearSlot.MainHand, GearSlot.OffHand, GearSlot.Head, GearSlot.Body, GearSlot.Hands, GearSlot.Legs,
        GearSlot.Feet, GearSlot.Ears, GearSlot.Neck, GearSlot.Wrists, GearSlot.RingRight, GearSlot.RingLeft,
    ];

    public static string Name(GearSlot slot) => slot switch
    {
        GearSlot.MainHand => "Main Hand",
        GearSlot.OffHand => "Off Hand",
        GearSlot.RingRight or GearSlot.RingLeft => "Ring",
        _ => slot.ToString(),
    };

    // The name the character window's tooltip gives an empty slot.
    public static string TooltipName(GearSlot slot) => slot switch
    {
        GearSlot.Ears => "Earrings",
        GearSlot.Neck => "Necklace",
        GearSlot.Wrists => "Bracelets",
        _ => Name(slot),
    };

    // Rings share one candidate pool: an item that fits one ring slot fits both.
    public static GearSlot Pool(GearSlot slot) => slot == GearSlot.RingLeft ? GearSlot.RingRight : slot;
}
