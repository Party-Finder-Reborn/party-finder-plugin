using Dalamud.Configuration;
using ECommons.DalamudServices;
using System;

namespace PartyFinderReborn;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    
    // Event that fires when configuration is updated
    public event Action? ConfigUpdated;

    // UI Configuration
    public bool ShowMainWindow { get; set; } = false;
    public bool AutoOpenOnLogin { get; set; } = false;
    
    // Plugin Settings
    public bool EnableNotifications { get; set; } = true;
    
    // Server Connection Settings
    public string ApiKey { get; set; } = string.Empty;
    
    // Action Tracking Configuration
    public bool EnableActionTracking { get; set; } = true;
    public bool FilterPlayerActions { get; set; } = true;
    public bool FilterPartyActions { get; set; } = true;
    public bool ResetOnInstanceLeave { get; set; } = true;
    
    // Progress Point Tracking Configuration
    public bool EnableProgPointTracking { get; set; } = true;
    

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
        
        // Fire the ConfigUpdated event after saving
        ConfigUpdated?.Invoke();
    }
}
