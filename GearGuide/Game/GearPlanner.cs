using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;

using GearGuide.Base;

namespace GearGuide.Game;

// One recommended piece and how to get it.
public sealed record GearChoice(GearItem Item, bool Hq, GearSource Source, float Score, OwnedPiece? Owned, MarketPrice? Price)
{
    // Item ids of high-quality pieces are offset by 1,000,000, as in the game.
    public uint TooltipItemId => Hq ? Item.Id + 1_000_000 : Item.Id;
}

internal sealed class GearPlan
{
    public required JobProfile Profile { get; init; }
    public required Dictionary<GearSlot, GearChoice> Picks { get; init; }
    // Slots left empty because another pick covers them (a two-handed weapon
    // covers the off hand), mapped to the slot of the piece covering them.
    public required Dictionary<GearSlot, GearSlot> Blocked { get; init; }

    public int AverageItemLevel
    {
        get
        {
            // The game's average: twelve slots (no soul crystal), with a piece
            // that covers other slots counted once for each slot it fills.
            int total = 0;
            foreach (var slot in GearSlots.All)
            {
                if (slot == GearSlot.SoulCrystal) continue;
                if (Picks.TryGetValue(slot, out var pick)) total += pick.Item.ItemLevel;
                else if (Blocked.TryGetValue(slot, out var by) && Picks.TryGetValue(by, out var cover)) total += cover.Item.ItemLevel;
            }
            return total / 12;
        }
    }

    // The best set made only of gear you have on you (not in the saddlebag),
    // whatever the filters say. It's what the Recommended Gear button puts on:
    // a slot's overall pick may be a better copy you'd still have to get (a
    // high-quality one, say) while the piece you own is still an upgrade.
    public required Dictionary<GearSlot, GearChoice> OwnedPicks { get; init; }

    // Pieces from the owned set that aren't worn yet, and the slot each goes in.
    public List<(GearSlot Slot, GearChoice Choice)> Upgrades()
    {
        var upgrades = OwnedPicks
            .Where(pair => pair.Key is not (GearSlot.RingRight or GearSlot.RingLeft) && pair.Value.Owned is { IsEquipped: false })
            .OrderBy(pair => (int)pair.Key)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();

        // Either ring fits either finger, so a new ring goes on whichever
        // finger isn't wearing one of the two picked rings.
        var rings = new[] { GearSlot.RingRight, GearSlot.RingLeft }
            .Where(OwnedPicks.ContainsKey).Select(slot => OwnedPicks[slot]).ToList();
        var kept = rings.Where(ring => ring.Owned is { IsEquipped: true }).Select(ring => (GearSlot)ring.Owned!.Value.Slot).ToHashSet();
        var freeFingers = new Queue<GearSlot>(new[] { GearSlot.RingRight, GearSlot.RingLeft }.Where(slot => !kept.Contains(slot)));
        foreach (var ring in rings.Where(ring => ring.Owned is { IsEquipped: false }))
            if (freeFingers.TryDequeue(out var finger)) upgrades.Add((finger, ring));
        return upgrades;
    }
}

// Picks the best piece for every slot from the sources the filters allow.
internal sealed class GearPlanner(ItemCatalog catalog, Configuration configuration, MarketBoard market)
{
    // How many market-only pieces per slot are looked up at once when they
    // would beat what's picked from other sources.
    private const int MarketLookupsPerSlot = 12;

    // Slots whose pieces can cover other slots go first (weapons, then body
    // and legs), so a covering piece is weighed before the slots it covers
    // are filled. Rings are picked separately.
    private static readonly GearSlot[] PickOrder =
    [
        GearSlot.MainHand, GearSlot.OffHand, GearSlot.Body, GearSlot.Legs, GearSlot.Head, GearSlot.Hands,
        GearSlot.Feet, GearSlot.Ears, GearSlot.Neck, GearSlot.Wrists, GearSlot.SoulCrystal,
    ];

    public GearPlan Plan(JobProfile profile, IReadOnlyList<OwnedPiece> owned)
    {
        var ownedById = owned.GroupBy(piece => piece.ItemId).ToDictionary(group => group.Key, group => group.ToList());
        var pools = new Dictionary<GearSlot, List<GearChoice>>();
        var ownedPools = new Dictionary<GearSlot, List<GearChoice>>();
        var marketWanted = new Dictionary<GearSlot, List<(uint Id, float Score)>>();

        foreach (var item in catalog.Items)
        {
            if (!profile.CanWear(item)) continue;

            if (ownedById.TryGetValue(item.Id, out var pieces))
                foreach (var piece in pieces)
                {
                    var choice = new GearChoice(item, piece.Hq, GearSource.Owned, profile.Score(item, piece.Hq), piece, null);
                    if (configuration.IncludeOwned) PoolFor(pools, item.Slot).Add(choice);
                    if (piece.CanEquipDirectly) PoolFor(ownedPools, item.Slot).Add(choice);
                }

            if (BestObtainable(profile, item, marketWanted) is { } obtainable) PoolFor(pools, item.Slot).Add(obtainable);
        }

        var plan = Choose(Sorted(pools));
        var ownedPlan = Choose(Sorted(ownedPools));
        RequestMarketLookups(plan, marketWanted);
        return new GearPlan { Profile = profile, Picks = plan.Picks, Blocked = plan.Blocked, OwnedPicks = ownedPlan.Picks };
    }

    private static List<GearChoice> PoolFor(Dictionary<GearSlot, List<GearChoice>> pools, GearSlot slot)
        => pools.TryGetValue(slot, out var pool) ? pool : pools[slot] = new List<GearChoice>();

    private static Dictionary<GearSlot, List<GearChoice>> Sorted(Dictionary<GearSlot, List<GearChoice>> pools)
    {
        foreach (var pool in pools.Values)
            pool.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : Rank(a).CompareTo(Rank(b)));
        return pools;
    }

    private GearChoice? BestObtainable(JobProfile profile, GearItem item, Dictionary<GearSlot, List<(uint, float)>> marketWanted)
    {
        GearChoice? best = null;
        void Consider(GearSource source, bool hq, MarketPrice? price = null)
        {
            var choice = new GearChoice(item, hq, source, profile.Score(item, hq), null, price);
            if (best == null || choice.Score > best.Score) best = choice;
        }

        if (configuration.IncludeVendor && item.Sources.HasFlag(GearSource.Vendor)) Consider(GearSource.Vendor, false);
        if (configuration.IncludeCrafted && item.Sources.HasFlag(GearSource.Crafted)) Consider(GearSource.Crafted, item.CanBeHq);
        if (configuration.IncludeOther && item.Sources.HasFlag(GearSource.Other)) Consider(GearSource.Other, false);
        if (configuration.IncludeMarket && item.Sources.HasFlag(GearSource.Market))
        {
            if (!configuration.CheckLiveListings)
            {
                Consider(GearSource.Market, item.CanBeHq);
            }
            else if (market.Get(item.Id) is { } price)
            {
                if (item.CanBeHq && price.Listed(true)) Consider(GearSource.Market, true, price);
                else if (price.Listed(false)) Consider(GearSource.Market, false, price);
            }
            else
            {
                var wanted = marketWanted.TryGetValue(item.Slot, out var list) ? list : marketWanted[item.Slot] = new();
                wanted.Add((item.Id, profile.Score(item, item.CanBeHq)));
            }
        }
        return best;
    }

    private static (Dictionary<GearSlot, GearChoice> Picks, Dictionary<GearSlot, GearSlot> Blocked) Choose(
        Dictionary<GearSlot, List<GearChoice>> pools)
    {
        var picks = new Dictionary<GearSlot, GearChoice>();
        var blocked = new Dictionary<GearSlot, GearSlot>();

        GearChoice? BestSingle(GearSlot slot)
            => pools.TryGetValue(slot, out var pool) ? pool.FirstOrDefault(choice => choice.Item.Blocks.Length == 0) : null;

        foreach (var slot in PickOrder)
        {
            if (blocked.ContainsKey(slot)) continue;
            if (!pools.TryGetValue(slot, out var pool)) continue;

            // A piece covering other slots only wins if it beats the best
            // separate pieces for all the slots it takes up.
            GearChoice? best = null;
            float bestValue = float.MinValue;
            foreach (var choice in pool)
            {
                if (choice.Item.Blocks.Any(other => picks.ContainsKey(other) || blocked.ContainsKey(other))) continue;
                float value = choice.Score - choice.Item.Blocks.Sum(other => BestSingle(other)?.Score ?? 0.0f);
                if (value > bestValue)
                {
                    best = choice;
                    bestValue = value;
                }
            }
            if (best == null) continue;
            picks[slot] = best;
            foreach (var other in best.Item.Blocks) blocked[other] = slot;
        }

        if (pools.TryGetValue(GearSlot.RingRight, out var rings))
        {
            var first = rings.FirstOrDefault();
            if (first != null)
            {
                picks[GearSlot.RingRight] = first;
                var second = rings.FirstOrDefault(ring => ring != first
                    && !(first.Item.IsUnique && ring.Item.Id == first.Item.Id)
                    && !(first.Owned != null && ring.Owned == first.Owned));
                if (second != null) picks[GearSlot.RingLeft] = second;
            }
        }
        return (picks, blocked);
    }

    // Looks up the market pieces that would beat each slot's current pick.
    private void RequestMarketLookups((Dictionary<GearSlot, GearChoice> Picks, Dictionary<GearSlot, GearSlot> Blocked) plan,
        Dictionary<GearSlot, List<(uint Id, float Score)>> marketWanted)
    {
        var ids = new List<uint>();
        foreach (var (slot, wanted) in marketWanted)
        {
            float current = slot == GearSlot.RingRight
                ? plan.Picks.TryGetValue(GearSlot.RingLeft, out var ring) ? ring.Score : float.MinValue
                : plan.Picks.TryGetValue(slot, out var pick) ? pick.Score : float.MinValue;
            ids.AddRange(wanted.Where(entry => entry.Score > current)
                .OrderByDescending(entry => entry.Score)
                .Take(MarketLookupsPerSlot)
                .Select(entry => entry.Id));
        }
        market.Request(ids);
    }

    // Tie-break order: what you're wearing, what you have, then how easy the
    // piece is to get.
    private static int Rank(GearChoice choice) => choice.Source switch
    {
        GearSource.Owned => choice.Owned?.Container == InventoryType.EquippedItems ? 0 : choice.Owned?.CanEquipDirectly == true ? 1 : 2,
        GearSource.Vendor => 3,
        GearSource.Crafted => 4,
        GearSource.Market => 5,
        _ => 6,
    };
}
