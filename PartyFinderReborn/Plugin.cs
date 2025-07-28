using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using PartyFinderReborn.Windows;
using PartyFinderReborn.Services;

namespace PartyFinderReborn;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pfreborn";

    public Configuration Configuration { get; init; }
    public PartyFinderApiService ApiService { get; init; }
    public ContentFinderService ContentFinderService { get; init; }

    public readonly WindowSystem WindowSystem = new("PartyFinderReborn");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        // Initialize ECommons
        ECommonsMain.Init(pluginInterface, this);

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        
        // Initialize services
        ApiService = new PartyFinderApiService(Configuration);
        ContentFinderService = new ContentFinderService();

        // Initialize windows
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        // Register command
        Svc.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Party Finder Reborn interface"
        });

        // Hook into UI events
        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        Svc.Log.Info("Party Finder Reborn initialized successfully!");
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow?.Dispose();
        MainWindow?.Dispose();
        ApiService?.Dispose();
        ContentFinderService?.Dispose();

        Svc.Commands.RemoveHandler(CommandName);

        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        // Toggle main window on command
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
}
