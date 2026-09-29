using System;

namespace GearGuide.Game;

// Where a recommended piece comes from, in the order ties are broken: a piece
// you already have beats one you'd have to go and get.
[Flags]
public enum GearSource
{
    None = 0,
    Owned = 1,
    Vendor = 2,
    Crafted = 4,
    Market = 8,
    Other = 16,
}
