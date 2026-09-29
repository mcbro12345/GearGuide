using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using GearGuide.Base;

namespace GearGuide.Game;

// Cheapest current listing of an item on the data center, 0 when none.
public readonly record struct MarketPrice(uint Nq, uint Hq)
{
    public bool Listed(bool hq) => (hq ? Hq : Nq) > 0;
}

// Live market board listings from Universalis, asked for only for the items
// the planner is about to recommend. Results are kept for a while so flicking
// between filters doesn't re-ask.
internal sealed class MarketBoard : IDisposable
{
    private const int BatchSize = 100;
    // Only the cheapest prices, for both the single-item and multi-item replies.
    private const string Fields = "itemID%2CminPriceNQ%2CminPriceHQ%2Citems.itemID%2Citems.minPriceNQ%2Citems.minPriceHQ";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ConcurrentDictionary<uint, (MarketPrice Price, DateTime Fetched)> prices = new();
    private readonly ConcurrentDictionary<uint, byte> inFlight = new();
    private string dataCenter = "";
    private volatile bool changed;

    public MarketBoard()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GearGuide-Dalamud/1.0");
    }

    public void Dispose() => http.Dispose();

    public string DataCenter => dataCenter;

    // True once since the last call if new prices arrived.
    public bool TakeChanged()
    {
        bool value = changed;
        changed = false;
        return value;
    }

    public void SetDataCenter(string name)
    {
        if (name == dataCenter) return;
        dataCenter = name;
        prices.Clear();
    }

    public MarketPrice? Get(uint itemId)
        => prices.TryGetValue(itemId, out var entry) && DateTime.UtcNow - entry.Fetched < CacheLifetime ? entry.Price : null;

    public bool IsPending(uint itemId) => inFlight.ContainsKey(itemId);

    public void Clear() => prices.Clear();

    // Fetches every item not already known or on its way.
    public void Request(IEnumerable<uint> itemIds)
    {
        if (dataCenter.Length == 0) return;
        var wanted = itemIds.Distinct().Where(id => Get(id) == null && inFlight.TryAdd(id, 0)).ToList();
        foreach (var batch in wanted.Chunk(BatchSize))
            _ = FetchAsync(dataCenter, batch);
    }

    private async Task FetchAsync(string world, uint[] ids)
    {
        try
        {
            string url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(world)}/{string.Join(',', ids)}?listings=0&entries=0&fields={Fields}";
            using var response = await http.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
            var root = document.RootElement;

            // One id comes back as the item itself; several come back keyed by id.
            var now = DateTime.UtcNow;
            var seen = new HashSet<uint>();
            if (root.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateObject())
                    if (uint.TryParse(item.Name, out uint id)) Store(id, item.Value, now, seen);
            }
            else if (root.TryGetProperty("itemID", out var single))
            {
                Store(single.GetUInt32(), root, now, seen);
            }
            // Items Universalis doesn't know have no listings.
            foreach (uint id in ids.Where(id => !seen.Contains(id)))
                prices[id] = (new MarketPrice(0, 0), now);
            if (world == dataCenter) changed = true;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Couldn't fetch market board listings from Universalis.");
        }
        finally
        {
            foreach (uint id in ids) inFlight.TryRemove(id, out _);
        }
    }

    private void Store(uint id, JsonElement item, DateTime now, HashSet<uint> seen)
    {
        prices[id] = (new MarketPrice(ReadPrice(item, "minPriceNQ"), ReadPrice(item, "minPriceHQ")), now);
        seen.Add(id);
    }

    private static uint ReadPrice(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double price) && price > 0
            ? (uint)Math.Min(price, uint.MaxValue)
            : 0;
}
