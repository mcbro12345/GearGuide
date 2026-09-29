namespace GearGuide.Game;

public readonly record struct ItemStat(byte Param, short Value);

// One piece of equipment from the Item sheet, reduced to what the planner needs.
public sealed class GearItem
{
    // BaseParam ids of weapon damage and defence. The sheet stores the NQ
    // values in their own columns; their HQ bonuses arrive through
    // BaseParamSpecial like any other stat.
    public const byte PhysicalDamageParam = 12;
    public const byte MagicDamageParam = 13;
    public const byte DefenseParam = 21;
    public const byte MagicDefenseParam = 24;

    public required uint Id { get; init; }
    public required string Name { get; init; }
    public required uint Icon { get; init; }
    public required byte LevelEquip { get; init; }
    public required ushort ItemLevel { get; init; }
    public required uint ClassJobCategory { get; init; }
    public required uint EquipRace { get; init; }
    public required uint GrandCompany { get; init; }
    public required GearSlot Slot { get; init; }
    // Other slots the piece leaves unusable (a two-handed weapon, a robe that
    // covers the legs...).
    public required GearSlot[] Blocks { get; init; }
    public required bool CanBeHq { get; init; }
    public required bool IsUnique { get; init; }
    public required ItemStat[] Stats { get; init; }
    public required ItemStat[] HqBonus { get; init; }
    public required ushort PhysicalDamage { get; init; }
    public required ushort MagicDamage { get; init; }
    public required ushort Defense { get; init; }
    public required ushort MagicDefense { get; init; }
    public required uint VendorPrice { get; init; }
    // Sources that don't depend on the character: sold by a gil vendor,
    // craftable, tradable on the market board, or none of those.
    public required GearSource Sources { get; init; }

    public int Stat(byte param, bool hq)
    {
        int total = param switch
        {
            PhysicalDamageParam => PhysicalDamage,
            MagicDamageParam => MagicDamage,
            DefenseParam => Defense,
            MagicDefenseParam => MagicDefense,
            _ => 0,
        };
        foreach (var stat in Stats)
            if (stat.Param == param) total += stat.Value;
        if (hq)
            foreach (var bonus in HqBonus)
                if (bonus.Param == param) total += bonus.Value;
        return total;
    }
}
