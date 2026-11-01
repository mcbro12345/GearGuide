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

// Rolls on duty loot when switched on: Need on what beats everything you own
// for your current job, Greed on the rest, and Pass only when that's all the
// game allows. Everything goes through the game's loot window: it's opened,
// each item is selected in its list and the real button pressed, and the
// window is closed again if this is what opened it. Items with a weekly limit
// are left for you to decide on.
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
    // How long the window stays up after the last roll, to show the result.
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(1);

    // Rolled slots, so a roll the server hasn't confirmed yet isn't repeated.
    private readonly HashSet<(uint Chest, uint Index, uint Item)> rolled = new();
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

        int pending = -1;
        bool anyLoot = false;
        for (int i = 0; i < loot->Items.Length; i++)
        {
            ref var item = ref loot->Items[i];
            if (item.ItemId == 0 || item.ChestObjectId is 0 or InvalidChest) continue;
            anyLoot = true;
            if (pending < 0 && NeedsRoll(item)) pending = i;
        }
        if (!anyLoot) rolled.Clear();

        var now = DateTime.UtcNow;
        if (pending < 0)
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

        ref var target = ref loot->Items[pending];
        if (window->SelectedItemIndex != pending)
        {
            var list = window->AtkUnitBase.GetComponentListById(ItemListNodeId);
            if (list != null) list->SelectItem(pending, true);
            return;
        }

        profile = JobProfile.ForLocalPlayer(profile);
        if (profile == null) return;

        var key = (target.ChestObjectId, target.ChestItemIndex, target.ItemId);
        var choice = Choose(target, IsUpgrade(target.ItemId, catalog, profile));
        rolled.Add(key);
        closeAt = now + CloseDelay;

        // The list has to be showing this item, or the button would roll on
        // something else. Anything unexpected is left for you to roll.
        if (window->Items[pending].ItemId != target.ItemId || !Press(window, ButtonFor(choice)))
        {
            Services.Log.Warning($"Couldn't roll on {ItemName(target.ItemId)}; leaving it for you.");
            return;
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
           && !rolled.Contains((item.ChestObjectId, item.ChestItemIndex, item.ItemId));

    private static RollResult Choose(in LootItem item, bool upgrade) => item.RollState switch
    {
        RollState.UpToNeed => upgrade ? RollResult.Needed : RollResult.Greeded,
        RollState.UpToGreed => RollResult.Greeded,
        _ => RollResult.Passed,
    };

    private static uint ButtonFor(RollResult choice) => choice switch
    {
        RollResult.Needed => NeedButtonNodeId,
        RollResult.Greeded => GreedButtonNodeId,
        _ => PassButtonNodeId,
    };

    // Whether a piece beats everything you own for its slot, or for rings,
    // the weaker of your two best.
    private static bool IsUpgrade(uint itemId, ItemCatalog catalog, JobProfile profile)
    {
        bool hq = itemId >= HqOffset;
        if (!catalog.ById.TryGetValue(itemId % HqOffset, out var item) || !profile.CanWear(item)) return false;

        var scores = OwnedGear.Scan()
            .Where(piece => catalog.ById.TryGetValue(piece.ItemId, out var other) && other.Slot == item.Slot && profile.CanWear(other))
            .Select(piece => profile.Score(catalog.ById[piece.ItemId], piece.Hq))
            .OrderDescending();
        return profile.Score(item, hq) > scores.ElementAtOrDefault(item.Slot == GearSlot.RingRight ? 1 : 0);
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
