using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KamiToolKit;
using Lumina.Excel.Sheets;

using GearGuide.Base;
using GearGuide.Game;
using GearGuide.UI;

namespace GearGuide;

public sealed class Plugin : IDalamudPlugin, IDisposable
{
    private const string CommandName = "/gearguide";
    // How often the job, level and data center are checked for changes while
    // the window is open.
    private static readonly TimeSpan ProfileCheckInterval = TimeSpan.FromSeconds(1);

    private readonly Configuration configuration;
    private readonly MarketBoard market = new();
    private readonly GearEquipper equipper = new();
    private readonly ItemMenu itemMenu = new();
    private readonly WindowSystem windowSystem = new("GearGuide");
    private readonly SettingsWindow settingsWindow;
    private readonly RecommendedGearRedirect recommendedGearRedirect;
    private readonly Task<ItemCatalog> catalogLoad;
    private readonly Task nativeUiInitialization;
    private GearPlanner? planner;
    private GearGuideWindow? window;
    private JobProfile? profile;
    private GearPlan? plan;
    private bool dirty = true;
    private DateTime nextProfileCheck;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();

        configuration = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Migrate();
        configuration.Save();

        catalogLoad = Task.Run(ItemCatalog.Build);
        settingsWindow = new SettingsWindow(configuration);
        windowSystem.AddWindow(settingsWindow);
        recommendedGearRedirect = new RecommendedGearRedirect(configuration, OpenWindow);

        nativeUiInitialization = KamiToolKitLibrary.InitializeAsync(Services.PluginInterface, "Gear Guide")
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Services.Log.Error(task.Exception!, "KamiToolKit failed to initialise; the Gear Guide window is unavailable.");
                    return Task.CompletedTask;
                }
                return Services.Framework.RunOnFrameworkThread(CreateWindow);
            }).Unwrap();

        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Gear Guide: the best gear for your current class and level.\n/gearguide settings → Open the Gear Guide settings.",
        });
        Services.Framework.Update += OnFrameworkUpdate;
        Services.GameInventory.InventoryChanged += OnInventoryChanged;
        Services.PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;
        Services.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnFrameworkUpdate;
        Services.GameInventory.InventoryChanged -= OnInventoryChanged;
        Services.CommandManager.RemoveHandler(CommandName);
        Services.PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        Services.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        windowSystem.RemoveAllWindows();
        recommendedGearRedirect.Dispose();
        itemMenu.Dispose();
        market.Dispose();
        if (nativeUiInitialization.IsCompletedSuccessfully)
        {
            window?.Dispose();
            KamiToolKitLibrary.Dispose();
        }
    }

    private void CreateWindow()
    {
        window = new GearGuideWindow(configuration)
        {
            InternalName = "GearGuide",
            Title = "Gear Guide",
            Size = new Vector2(GearGuideWindow.PanelWidth + 28.0f, GearGuideWindow.PanelHeight + 64.0f),
            OnFiltersChanged = () => dirty = true,
            OnEquip = () =>
            {
                if (plan != null) equipper.Start(plan);
            },
            OnRefreshClicked = () =>
            {
                market.Clear();
                dirty = true;
            },
            OnOpenMenu = OpenMenu,
        };
    }

    private void ToggleWindow()
    {
        if (window == null)
        {
            Services.Chat.PrintError("Gear Guide is still starting up.", "Gear Guide");
            return;
        }
        if (!window.IsOpen) dirty = true;
        window.Toggle();
    }

    private void OpenWindow()
    {
        if (window == null || window.IsOpen) return;
        dirty = true;
        window.Open();
    }

    private void OpenSettings() => settingsWindow.IsOpen = true;

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("settings", StringComparison.OrdinalIgnoreCase)) OpenSettings();
        else ToggleWindow();
    }

    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events) => dirty = true;

    private void OpenMenu(GearSlotNode node)
    {
        if (node.Choice is not { } choice) return;
        if (choice.Owned is { } owned) ItemMenu.OpenForOwned(owned, (uint)(window?.AddonId ?? 0));
        else itemMenu.OpenForItem(choice.Item.Id, choice.Hq, choice.Item.Name);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        itemMenu.OnFrameworkUpdate();
        equipper.Update();
        if (window is not { IsOpen: true }) return;

        if (DateTime.UtcNow >= nextProfileCheck)
        {
            nextProfileCheck = DateTime.UtcNow + ProfileCheckInterval;
            var current = JobProfile.ForLocalPlayer(profile);
            if (current != profile)
            {
                profile = current;
                dirty = true;
            }
            var world = Services.PlayerState.CurrentWorld;
            if (world.IsValid) market.SetDataCenter(world.Value.DataCenter.Value.Name.ToString());
        }
        if (market.TakeChanged()) dirty = true;

        window.CanEquip = plan != null && plan.Upgrades().Any() && !equipper.IsRunning && GearEquipper.CanEquipNow;

        if (!dirty || profile == null || !catalogLoad.IsCompletedSuccessfully) return;
        dirty = false;
        Replan(profile, catalogLoad.Result);
    }

    private void Replan(JobProfile current, ItemCatalog catalog)
    {
        planner ??= new GearPlanner(catalog, configuration, market);
        var owned = OwnedGear.Scan();
        plan = planner.Plan(current, owned);
        window!.Show(new GearGuideView
        {
            Profile = current,
            Plan = plan,
            Stats = StatLines(current, plan, owned, catalog),
        });
    }

    private static List<StatLine> StatLines(JobProfile current, GearPlan plan, List<OwnedPiece> owned, ItemCatalog catalog)
    {
        var worn = owned.Where(piece => piece.IsEquipped && catalog.ById.ContainsKey(piece.ItemId))
            .Select(piece => (Item: catalog.ById[piece.ItemId], piece.Hq)).ToList();
        var names = Services.DataManager.GetExcelSheet<BaseParam>();
        return current.KeyStats.Select(param => new StatLine(
            names.TryGetRow(param, out var row) ? row.Name.ToString() : $"Stat {param}",
            plan.Picks.Values.Sum(pick => pick.Item.Stat(param, pick.Hq)),
            worn.Sum(piece => piece.Item.Stat(param, piece.Hq)))).ToList();
    }
}
