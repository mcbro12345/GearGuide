using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace GearGuide.Game;

// One piece of gear the character has, and where it is.
public readonly record struct OwnedPiece(uint ItemId, bool Hq, InventoryType Container, short Slot)
{
    public bool IsEquipped => Container == InventoryType.EquippedItems;

    // Saddlebag pieces count as owned but have to be taken out before they can
    // be worn.
    public bool CanEquipDirectly => Container != InventoryType.SaddleBag1 && Container != InventoryType.SaddleBag2
                                    && Container != InventoryType.PremiumSaddleBag1 && Container != InventoryType.PremiumSaddleBag2;
}

internal static unsafe class OwnedGear
{
    private static readonly InventoryType[] Containers =
    [
        InventoryType.EquippedItems,
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
        InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
        // Only filled once the saddlebag has been opened this session.
        InventoryType.SaddleBag1, InventoryType.SaddleBag2, InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2,
    ];

    public static List<OwnedPiece> Scan()
    {
        var pieces = new List<OwnedPiece>();
        var manager = InventoryManager.Instance();
        if (manager == null) return pieces;

        foreach (var type in Containers)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded) continue;
            for (int i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || item->ItemId == 0) continue;
                pieces.Add(new OwnedPiece(item->GetBaseItemId(), item->IsHighQuality(), type, (short)i));
            }
        }
        return pieces;
    }
}
