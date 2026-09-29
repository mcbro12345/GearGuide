using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace GearGuide.Base;

// Populated by PluginInterface.Create<Services>() from Plugin's constructor.
// Every other class reaches Dalamud's services through here instead of taking
// each one as a constructor parameter.
internal sealed class Services
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; set; } = null!;
    [PluginService] internal static IFramework Framework { get; set; } = null!;
    [PluginService] internal static IClientState ClientState { get; set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; set; } = null!;
    [PluginService] internal static IChatGui Chat { get; set; } = null!;
    [PluginService] internal static IPluginLog Log { get; set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; set; } = null!;
    [PluginService] internal static ICondition Condition { get; set; } = null!;
}
