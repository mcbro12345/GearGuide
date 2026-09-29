using System.Collections.Generic;
using System.Linq;
using Lumina.Excel;
using Lumina.Excel.Sheets;

using GearGuide.Base;

namespace GearGuide.Game;

// Every piece of equipment in the game, read once from the sheets.
internal sealed class ItemCatalog
{
    public IReadOnlyList<GearItem> Items { get; }
    public IReadOnlyDictionary<uint, GearItem> ById { get; }

    private ItemCatalog(List<GearItem> items)
    {
        Items = items;
        ById = items.ToDictionary(item => item.Id);
    }

    public static ItemCatalog Build()
    {
        var vendorItems = new HashSet<uint>();
        foreach (var shop in Services.DataManager.GetSubrowExcelSheet<GilShopItem>())
            foreach (var entry in shop)
                vendorItems.Add(entry.Item.RowId);

        var craftedItems = new HashSet<uint>();
        foreach (var recipe in Services.DataManager.GetExcelSheet<Recipe>())
            if (recipe.ItemResult.RowId != 0) craftedItems.Add(recipe.ItemResult.RowId);

        var items = new List<GearItem>();
        foreach (var row in Services.DataManager.GetExcelSheet<Item>())
        {
            if (row.EquipSlotCategory.RowId == 0 || row.IsPvP || row.LevelEquip == 0 || row.Name.IsEmpty) continue;
            if (!row.EquipSlotCategory.IsValid || !TryGetSlots(row.EquipSlotCategory.Value, out var slot, out var blocks)) continue;
            var stats = ReadStats(row.BaseParam, row.BaseParamValue);
            // Costumes (Chocobo Suit and the like) cover several slots and have
            // no stats; they're never worth recommending.
            if (blocks.Length > 0 && stats.Length == 0) continue;

            var sources = GearSource.None;
            if (vendorItems.Contains(row.RowId)) sources |= GearSource.Vendor;
            if (craftedItems.Contains(row.RowId)) sources |= GearSource.Crafted;
            if (!row.IsUntradable && row.ItemSearchCategory.RowId != 0) sources |= GearSource.Market;
            if (sources == GearSource.None) sources = GearSource.Other;

            items.Add(new GearItem
            {
                Id = row.RowId,
                Name = row.Name.ToString(),
                Icon = row.Icon,
                LevelEquip = row.LevelEquip,
                ItemLevel = (ushort)row.LevelItem.RowId,
                ClassJobCategory = row.ClassJobCategory.RowId,
                EquipRace = row.EquipRestriction.RowId,
                GrandCompany = row.GrandCompany.RowId,
                Slot = slot,
                Blocks = blocks,
                CanBeHq = row.CanBeHq,
                IsUnique = row.IsUnique,
                Stats = stats,
                HqBonus = row.CanBeHq ? ReadStats(row.BaseParamSpecial, row.BaseParamValueSpecial) : [],
                PhysicalDamage = row.DamagePhys,
                MagicDamage = row.DamageMag,
                Defense = row.DefensePhys,
                MagicDefense = row.DefenseMag,
                VendorPrice = row.PriceMid,
                Sources = sources,
            });
        }
        return new ItemCatalog(items);
    }

    private static ItemStat[] ReadStats(Collection<RowRef<BaseParam>> parameters, Collection<short> values)
    {
        var stats = new List<ItemStat>();
        for (int i = 0; i < parameters.Count && i < values.Count; i++)
            if (parameters[i].RowId != 0 && values[i] != 0)
                stats.Add(new ItemStat((byte)parameters[i].RowId, values[i]));
        return stats.ToArray();
    }

    // EquipSlotCategory marks the slot an item goes in with 1 and the slots it
    // blocks with -1. Rings are 1 in both finger slots.
    private static bool TryGetSlots(EquipSlotCategory category, out GearSlot slot, out GearSlot[] blocks)
    {
        (sbyte Value, GearSlot Slot)[] fields =
        [
            (category.MainHand, GearSlot.MainHand), (category.OffHand, GearSlot.OffHand),
            (category.Head, GearSlot.Head), (category.Body, GearSlot.Body), (category.Gloves, GearSlot.Hands),
            (category.Legs, GearSlot.Legs), (category.Feet, GearSlot.Feet), (category.Ears, GearSlot.Ears),
            (category.Neck, GearSlot.Neck), (category.Wrists, GearSlot.Wrists),
            (category.FingerR, GearSlot.RingRight), (category.FingerL, GearSlot.RingLeft),
        ];
        var occupies = fields.Where(field => field.Value > 0).Select(field => field.Slot).ToList();
        blocks = fields.Where(field => field.Value < 0).Select(field => field.Slot).ToArray();
        slot = occupies.Count > 0 ? GearSlots.Pool(occupies[0]) : default;
        return occupies.Count > 0 && category.Waist <= 0;
    }
}
