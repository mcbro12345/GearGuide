using Dalamud.Configuration;

namespace GearGuide.Base;

public sealed class Configuration : IPluginConfiguration
{
    private const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    // Which sources a recommendation may come from. A piece qualifies when
    // any one of its sources is switched on.
    public bool IncludeOwned { get; set; } = true;
    public bool IncludeMarket { get; set; } = true;
    public bool IncludeVendor { get; set; } = true;
    public bool IncludeCrafted { get; set; } = true;
    public bool IncludeOther { get; set; }

    // On: the Market Board source only counts items with listings on your
    // data center right now (asks Universalis). Off: any item that can be
    // sold on the market board counts.
    public bool CheckLiveListings { get; set; } = true;

    // The Character window's Recommended Gear button opens Gear Guide
    // instead of the game's Recommended Gear window.
    public bool ReplaceRecommendedGearButton { get; set; } = true;

    public void Migrate() => Version = CurrentVersion;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
