using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

using GearGuide.Base;

namespace GearGuide.Game;

// Rolls on duty loot when switched on: Need on the best upgrades for your
// current job, Greed on other upgrades once Need is used up, and Pass on any
// other gear. Anything that isn't gear, and items with a weekly limit, are
// left for you. Everything goes through the game's loot window: it's opened,
// each item is selected in its list and the real button pressed, and the
// window is closed again if this is what opened it.
internal sealed unsafe class AutoRoller(Configuration configuration)
{
    private const string LootWindow = "NeedGreed";
    // Nodes in NeedGreed.uld.
    private const uint ItemListNodeId = 6;
    private const uint NeedButtonNodeId = 13;
    private const uint GreedButtonNodeId = 14;
    private const uint PassButtonNodeId = 16;

    private const uint InvalidChest = 0xE0000000;
    private const uint HqOffset = 1_000_000;
    private static readonly TimeSpan FirstRollDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BetweenSteps = TimeSpan.FromMilliseconds(500);
    // How long to wait after a Need, so the game can say whether more are left.
    private static readonly TimeSpan NeedSettleTime = TimeSpan.FromMilliseconds(1500);
    // How long the window stays up after the last roll, to show the result.
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(1);

    // Rolled slots, so a roll the server hasn't confirmed yet isn't repeated.
    private readonly HashSet<(uint Chest, uint Index, uint Item)> rolled = new();
    private readonly HashSet<GearSlot> neededSlots = new();
    private JobProfile? profile;
    private bool waiting;
    private bool openedWindow;
    private DateTime nextStep;
    private DateTime closeAt;

    public void Update(ItemCatalog catalog)
    {
        if (!configuration.AutoRollLoot || !Services.Condition[ConditionFlag.BoundByDuty]) return;
        var loot = Loot.Instance();
        var agent = AgentLoot.Instance();
        if (loot == null || agent == null) return;

        var pending = new List<int>();
        bool anyLoot = false;
        for (int i = 0; i < loot->Items.Length; i++)
        {
            ref var item = ref loot->Items[i];
            if (item.ItemId == 0 || item.ChestObjectId is 0 or InvalidChest) continue;
            anyLoot = true;
            if (NeedsRoll(item)) pending.Add(i);
        }
        if (!anyLoot)
        {
            rolled.Clear();
            neededSlots.Clear();
        }

        var now = DateTime.UtcNow;
        if (pending.Count == 0)
        {
            waiting = false;
            if (openedWindow && now >= closeAt)
            {
                openedWindow = false;
                agent->Hide();
            }
            return;
        }

        if (!waiting)
        {
            waiting = true;
            nextStep = now + FirstRollDelay;
        }
        if (now < nextStep) return;
        nextStep = now + BetweenSteps;

        var windowPtr = Services.GameGui.GetAddonByName(LootWindow);
        if (windowPtr.IsNull || !windowPtr.IsVisible)
        {
            agent->Show();
            openedWindow = true;
            return;
        }
        var window = (AddonNeedGreed*)windowPtr.Address;

        profile = JobProfile.ForLocalPlayer(profile);
        if (profile == null) return;

        // The biggest upgrade goes first so it's the one that gets your Need.
        var owned = OwnedGear.Scan();
        int index = pending[0];
        double bestGain = 0.0;
        foreach (int i in pending)
        {
            double gain = UpgradeGain(loot->Items[i].ItemId, catalog, profile, owned);
            if (gain > bestGain)
            {
                bestGain = gain;
                index = i;
            }
        }

        ref var target = ref loot->Items[index];
        if (window->SelectedItemIndex != index)
        {
            var list = window->AtkUnitBase.GetComponentListById(ItemListNodeId);
            if (list != null) list->SelectItem(index, true);
            return;
        }

        GearSlot? slot = bestGain > 0.0 ? catalog.ById[target.ItemId % HqOffset].Slot : null;
        var choice = Choose(target, slot);
        rolled.Add((target.ChestObjectId, target.ChestItemIndex, target.ItemId));
        closeAt = now + CloseDelay;

        // The list has to be showing this item, or the button would roll on
        // something else. Anything unexpected is left for you to roll.
        bool pressed = window->Items[index].ItemId == target.ItemId && Press(window, ButtonFor(choice));
        if (!pressed && choice == RollResult.Needed && window->Items[index].ItemId == target.ItemId)
        {
            // Need is used up for this item.
            choice = RollResult.Greeded;
            pressed = Press(window, GreedButtonNodeId);
        }
        if (!pressed)
        {
            Services.Log.Warning($"Couldn't roll on {ItemName(target.ItemId)}; leaving it for you.");
            return;
        }
        if (choice == RollResult.Needed)
        {
            neededSlots.Add(slot!.Value);
            nextStep = now + NeedSettleTime;
        }
        Services.Chat.Print($"{Label(choice)}: {ItemName(target.ItemId)}", "Gear Guide");
    }

    // Presses a button the way a click does, by giving the window the button's
    // own click event. A disabled or hidden button is never pressed.
    private static bool Press(AddonNeedGreed* window, uint nodeId)
    {
        var button = window->AtkUnitBase.GetComponentButtonById(nodeId);
        if (button == null || !button->IsEnabled) return false;
        var node = button->OwnerNode;
        if (node == null || !node->IsVisible()) return false;

        for (var atkEvent = node->AtkEventManager.Event; atkEvent != null; atkEvent = atkEvent->NextEvent)
        {
            if (atkEvent->State.EventType != AtkEventType.ButtonClick) continue;
            var data = default(AtkEventData);
            window->AtkUnitBase.ReceiveEvent(atkEvent->State.EventType, (int)atkEvent->Param, atkEvent, &data);
            return true;
        }
        return false;
    }

    private bool NeedsRoll(in LootItem item)
        => item.RollResult == RollResult.UnAwarded
           && item.RollState is RollState.UpToNeed or RollState.UpToGreed or RollState.UpToPass
           && item.LootMode != LootMode.Unavailable
           // Winning one of these uses up a weekly claim, so it's your call.
           && !item.WeeklyLootItem
           && IsGear(item.ItemId)
           && !rolled.Contains((item.ChestObjectId, item.ChestItemIndex, item.ItemId));

    private static bool IsGear(uint itemId)
        => Services.DataManager.GetExcelSheet<Item>().TryGetRow(itemId % HqOffset, out var item) && item.EquipSlotCategory.RowId != 0;

    // slot: the slot of an upgrade, null for anything else. A slot that has
    // already had a Need gets Greed for any other upgrade to it.
    private RollResult Choose(in LootItem item, GearSlot? slot)
    {
        if (slot == null || item.RollState == RollState.UpToPass) return RollResult.Passed;
        return item.RollState == RollState.UpToNeed && !neededSlots.Contains(slot.Value) ? RollResult.Needed : RollResult.Greeded;
    }

    private static uint ButtonFor(RollResult choice) => choice switch
    {
        RollResult.Needed => NeedButtonNodeId,
        RollResult.Greeded => GreedButtonNodeId,
        _ => PassButtonNodeId,
    };

    // How much a piece beats what you own for its slot, or for rings the
    // weaker of your two best. Zero when it can't be worn or isn't an upgrade.
    private static double UpgradeGain(uint itemId, ItemCatalog catalog, JobProfile profile, List<OwnedPiece> owned)
    {
        bool hq = itemId >= HqOffset;
        if (!catalog.ById.TryGetValue(itemId % HqOffset, out var item) || !profile.CanWear(item)) return 0.0;

        var scores = owned
            .Where(piece => catalog.ById.TryGetValue(piece.ItemId, out var other) && other.Slot == item.Slot && profile.CanWear(other))
            .Select(piece => profile.Score(catalog.ById[piece.ItemId], piece.Hq))
            .OrderDescending();
        return Math.Max(0.0, profile.Score(item, hq) - scores.ElementAtOrDefault(item.Slot == GearSlot.RingRight ? 1 : 0));
    }

    private static string Label(RollResult choice) => choice switch
    {
        RollResult.Needed => "Need",
        RollResult.Greeded => "Greed",
        _ => "Pass",
    };

    private static string ItemName(uint itemId)
        => Services.DataManager.GetExcelSheet<Item>().TryGetRow(itemId % HqOffset, out var item) ? item.Name.ToString() : $"Item {itemId}";
}
