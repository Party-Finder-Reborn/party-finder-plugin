using Dalamud.Configuration;
using ECommons.DalamudServices;
using System;

namespace PartyFinderReborn;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // UI Configuration
    public bool ShowMainWindow { get; set; } = false;
    public bool AutoOpenOnLogin { get; set; } = false;
    public float WindowOpacity { get; set; } = 1.0f;
    
    // Plugin Settings
    public bool EnableNotifications { get; set; } = true;
    public bool AutoRefreshListings { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 30;
    
    // Server Connection Settings
    public string ApiKey { get; set; } = string.Empty;
    
    // Action Tracking Configuration
    public bool EnableActionTracking { get; set; } = true;
    public bool FilterPlayerActions { get; set; } = true;
    public bool FilterPartyActions { get; set; } = true;
    public bool ResetOnInstanceLeave { get; set; } = true;
    
    // Action Tracking Filters
    public bool TrackBossActionsOnly { get; set; } = true;
    public bool TrackTrashMobs { get; set; } = false;
    public int SyncDebounceSeconds { get; set; } = 30;

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }
}
