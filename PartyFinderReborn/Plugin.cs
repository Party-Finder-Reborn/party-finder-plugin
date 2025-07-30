using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using PartyFinderReborn.Windows;
using PartyFinderReborn.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using ImGuiNET;
using Dalamud.Plugin.Services;

namespace PartyFinderReborn;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pfreborn";
    private const string ConfigCommandName = "/pfreborn config";
    private const string RefreshCommandName = "/pfrefresh";
    private const string DebugCommandName = "/pfdebug";

    public Configuration Configuration { get; init; }
    public PartyFinderApiService ApiService { get; init; }
    public ContentFinderService ContentFinderService { get; init; }
    public DutyProgressService DutyProgressService { get; init; }
    public ActionNameService ActionNameService { get; init; }
    public ActionTrackingService ActionTrackingService { get; init; }
    public WorldService WorldService { get; init; }

    public readonly WindowSystem WindowSystem = new("PartyFinderReborn");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private DateTime lastFrameworkUpdate = DateTime.Now;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        // Initialize ECommons
        ECommonsMain.Init(pluginInterface, this);

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        
        // Initialize services
        ApiService = new PartyFinderApiService(Configuration);
        ContentFinderService = new ContentFinderService();
        DutyProgressService = new DutyProgressService(ContentFinderService, ApiService, Configuration);
        ActionNameService = new ActionNameService();
        ActionTrackingService = new ActionTrackingService(DutyProgressService, ContentFinderService, Configuration);
        WorldService = new WorldService();

        // Initialize windows
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        // Register commands
        Svc.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Party Finder Reborn interface"
        });
        
        Svc.Commands.AddHandler(RefreshCommandName, new CommandInfo(OnRefreshCommand)
        {
            HelpMessage = "Refreshes duty progress data from the game"
        });
        
        Svc.Commands.AddHandler(DebugCommandName, new CommandInfo(OnDebugCommand)
        {
            HelpMessage = "Shows debug information about duty progress"
        });
        
        Svc.Commands.AddHandler(ConfigCommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Opens the Party Finder Reborn configuration window"
        });

        // Initialize duty progress tracking
        _ = DutyProgressService.RefreshProgressData();

        // Hook into UI events
        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        // Subscribe to login and logout events
        Svc.ClientState.Login += OnLogin;
        Svc.Framework.Update += OnFrameworkUpdate;

        Svc.Log.Info("Party Finder Reborn initialized successfully!");
    }

    private void OnLogin()
    {
        Svc.Log.Info("Player logged in, running initial sync of completed duties.");
        _ = Task.Run(async () =>
        {
            try
            {
                await DutyProgressService.SyncDutiesOnLoginAsync();
                Svc.Log.Info("Initial duty sync completed successfully.");
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to sync duties on login: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow?.Dispose();
        MainWindow?.Dispose();
        ActionTrackingService?.Dispose();
        ActionNameService?.Dispose();
        DutyProgressService?.Dispose();
        ApiService?.Dispose();
        ContentFinderService?.Dispose();
        WorldService?.Dispose();

        Svc.Commands.RemoveHandler(CommandName);
        Svc.Commands.RemoveHandler(RefreshCommandName);
        Svc.Commands.RemoveHandler(DebugCommandName);
        Svc.Commands.RemoveHandler(ConfigCommandName);

        Svc.Framework.Update -= OnFrameworkUpdate;

        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        // Toggle main window on command
        ToggleMainUI();
    }
    
    private void OnRefreshCommand(string command, string args)
    {
        Svc.Log.Info("Manual refresh of duty progress data requested");
        _ = Task.Run(async () =>
        {
            try
            {
                await DutyProgressService.RefreshProgressData();
                Svc.Log.Info("Duty progress data refreshed successfully");
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to refresh duty progress data: {ex.Message}");
            }
        });
    }
    
    private void OnConfigCommand(string command, string args)
    {
        ToggleConfigUI();
    }
    
    private void OnDebugCommand(string command, string args)
    {
        try
        {
            var completedCount = DutyProgressService.GetCompletedDutiesCount();
            var progPointsDutiesCount = DutyProgressService.GetProgPointsDutiesCount();
            var totalProgPointsCount = DutyProgressService.GetTotalProgPointsCount();
            var sessionProgPointsCount = ActionTrackingService.GetSeenProgPointsCount();
            
            Svc.Log.Info($"Duty Progress Debug Info:");
            Svc.Log.Info($"- Completed duties: {completedCount}");
            Svc.Log.Info($"- Duties with progress points: {progPointsDutiesCount}");
            Svc.Log.Info($"- Total progress points tracked: {totalProgPointsCount}");
            Svc.Log.Info($"- Session progress points tracked: {sessionProgPointsCount}");
            Svc.Log.Info($"- Action tracking enabled: {Configuration.EnableActionTracking}");
            Svc.Log.Info($"- Filter player actions: {Configuration.FilterPlayerActions}");
            Svc.Log.Info($"- Filter party actions: {Configuration.FilterPartyActions}");
            Svc.Log.Info($"- Reset on instance leave: {Configuration.ResetOnInstanceLeave}");
            Svc.Log.Info($"- Use {RefreshCommandName} to refresh data from game");
            
            if (completedCount > 0)
            {
                var completedDuties = DutyProgressService.GetCompletedDuties();
                Svc.Log.Info($"- First few completed duty IDs: {string.Join(", ", completedDuties.Take(10))}");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to get debug info: {ex.Message}");
        }
    }
    
    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.Now;
        var elapsed = now - lastFrameworkUpdate;

        if (elapsed.TotalSeconds >= Configuration.RefreshIntervalSeconds)
        {
            lastFrameworkUpdate = now;

            if (Configuration.AutoRefreshListings && MainWindow.IsOpen)
            {
                _ = MainWindow.LoadPartyListingsAsync();
            }
        }
    }
}
