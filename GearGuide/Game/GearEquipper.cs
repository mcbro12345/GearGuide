using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;

using GearGuide.Base;

namespace GearGuide.Game;

// Puts on the recommended pieces you own, one at a time, waiting for the
// server to confirm each move before making the next, as the game's own
// Recommended Gear button does.
internal sealed unsafe class GearEquipper
{
    private static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(3);

    private readonly Queue<(GearSlot Slot, uint ItemId, bool Hq)> queue = new();
    private (GearSlot Slot, uint ItemId, bool Hq)? current;
    private DateTime currentStarted;

    public bool IsRunning => current != null || queue.Count > 0;

    public static bool CanEquipNow
        => !Services.Condition[ConditionFlag.InCombat] && !Services.Condition[ConditionFlag.Casting]
           && !Services.Condition[ConditionFlag.BetweenAreas] && !Services.Condition[ConditionFlag.OccupiedInQuestEvent];

    public void Start(GearPlan plan)
    {
        if (IsRunning) return;
        if (!CanEquipNow)
        {
            Services.Chat.PrintError("You can't change gear right now.", "Gear Guide");
            return;
        }
        foreach (var (slot, choice) in plan.Upgrades())
            queue.Enqueue((slot, choice.Item.Id, choice.Hq));
    }

    public void Update()
    {
        if (current is { } move)
        {
            if (!IsWorn(move) && DateTime.UtcNow - currentStarted < MoveTimeout) return;
            current = null;
        }

        if (queue.Count == 0) return;
        if (!CanEquipNow)
        {
            queue.Clear();
            Services.Chat.PrintError("Stopped equipping: you can't change gear right now.", "Gear Guide");
            return;
        }

        var next = queue.Dequeue();
        if (IsWorn(next)) return;
        var source = OwnedGear.Scan().FirstOrDefault(piece => piece.ItemId == next.ItemId && piece.Hq == next.Hq
                                                              && !piece.IsEquipped && piece.CanEquipDirectly);
        if (source.ItemId == 0) return;

        var manager = InventoryManager.Instance();
        if (manager == null) return;
        manager->MoveItemSlot(source.Container, (ushort)source.Slot, InventoryType.EquippedItems, (ushort)next.Slot, true);
        current = next;
        currentStarted = DateTime.UtcNow;
    }

    private static bool IsWorn((GearSlot Slot, uint ItemId, bool Hq) move)
    {
        var manager = InventoryManager.Instance();
        var item = manager == null ? null : manager->GetInventorySlot(InventoryType.EquippedItems, (int)move.Slot);
        return item != null && item->GetBaseItemId() == move.ItemId && item->IsHighQuality() == move.Hq;
    }
}
