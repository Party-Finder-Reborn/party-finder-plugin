using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace PartyFinderReborn.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;

    public ConfigWindow(Plugin plugin) : base("Party Finder Reborn Configuration##config_window")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;

        Size = new Vector2(500, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

        Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Configuration options:");
        
        // UI Configuration
        if (ImGui.CollapsingHeader("UI Settings"))
        {
            var showMainWindow = Configuration.ShowMainWindow;
            if (ImGui.Checkbox("Show Main Window", ref showMainWindow))
            {
                Configuration.ShowMainWindow = showMainWindow;
                Configuration.Save();
            }
            
            var autoOpenOnLogin = Configuration.AutoOpenOnLogin;
            if (ImGui.Checkbox("Auto Open On Login", ref autoOpenOnLogin))
            {
                Configuration.AutoOpenOnLogin = autoOpenOnLogin;
                Configuration.Save();
            }

            var windowOpacity = Configuration.WindowOpacity;
            if (ImGui.SliderFloat("Window Opacity", ref windowOpacity, 0.0f, 1.0f))
            {
                Configuration.WindowOpacity = windowOpacity;
                Configuration.Save();
            }
        }
        
        // Plugin Settings
        if (ImGui.CollapsingHeader("Plugin Settings"))
        {
            var enableNotifications = Configuration.EnableNotifications;
            if (ImGui.Checkbox("Enable Notifications", ref enableNotifications))
            {
                Configuration.EnableNotifications = enableNotifications;
                Configuration.Save();
            }
            
            var autoRefreshListings = Configuration.AutoRefreshListings;
            if (ImGui.Checkbox("Auto Refresh Listings", ref autoRefreshListings))
            {
                Configuration.AutoRefreshListings = autoRefreshListings;
                Configuration.Save();
            }

            var refreshIntervalSeconds = Configuration.RefreshIntervalSeconds;
            if (ImGui.InputInt("Refresh Interval (Seconds)", ref refreshIntervalSeconds))
            {
                if (refreshIntervalSeconds > 0)
                {
                    Configuration.RefreshIntervalSeconds = refreshIntervalSeconds;
                    Configuration.Save();
                }
            }
        }
        
        // Server Connection Settings
        if (ImGui.CollapsingHeader("Server Settings"))
        {
            var apiKey = Configuration.ApiKey;
            if (ImGui.InputText("API Key", ref apiKey, 200))
            {
                Configuration.ApiKey = apiKey;
                Configuration.Save();
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Your API key for accessing the Party Finder server");
            }
        }
        
        // Action Tracking Settings
        if (ImGui.CollapsingHeader("Action Tracking"))
        {
            var enableActionTracking = Configuration.EnableActionTracking;
            if (ImGui.Checkbox("Enable Action Tracking", ref enableActionTracking))
            {
                Configuration.EnableActionTracking = enableActionTracking;
                Configuration.Save();
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Track actions for progress point synchronization");
            }
            
            if (Configuration.EnableActionTracking)
            {
                ImGui.Indent();
                
                // Filter Settings
                ImGui.Text("Filter Settings:");
                
                var filterPlayerActions = Configuration.FilterPlayerActions;
                if (ImGui.Checkbox("Filter Player Actions", ref filterPlayerActions))
                {
                    Configuration.FilterPlayerActions = filterPlayerActions;
                    Configuration.Save();
                }
                
                var filterPartyActions = Configuration.FilterPartyActions;
                if (ImGui.Checkbox("Filter Party Actions", ref filterPartyActions))
                {
                    Configuration.FilterPartyActions = filterPartyActions;
                    Configuration.Save();
                }
                
                var resetOnInstanceLeave = Configuration.ResetOnInstanceLeave;
                if (ImGui.Checkbox("Reset On Instance Leave", ref resetOnInstanceLeave))
                {
                    Configuration.ResetOnInstanceLeave = resetOnInstanceLeave;
                    Configuration.Save();
                }
                
                ImGui.Separator();
                
                // New Action Tracking Filters
                ImGui.Text("Action Type Filters:");
                
                var trackBossActionsOnly = Configuration.TrackBossActionsOnly;
                if (ImGui.Checkbox("Track Boss Actions Only", ref trackBossActionsOnly))
                {
                    Configuration.TrackBossActionsOnly = trackBossActionsOnly;
                    Configuration.Save();
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Only track actions from boss enemies (excludes trash mobs)");
                }
                
                var trackTrashMobs = Configuration.TrackTrashMobs;
                if (ImGui.Checkbox("Track Trash Mobs", ref trackTrashMobs))
                {
                    Configuration.TrackTrashMobs = trackTrashMobs;
                    Configuration.Save();
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Track actions from trash mobs (non-boss enemies)");
                }
                
                ImGui.Separator();
                
                // Sync Settings
                ImGui.Text("Synchronization:");
                
                var syncDebounceSeconds = Configuration.SyncDebounceSeconds;
                if (ImGui.SliderInt("Sync Debounce (Seconds)", ref syncDebounceSeconds, 5, 120))
                {
                    Configuration.SyncDebounceSeconds = syncDebounceSeconds;
                    Configuration.Save();
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Minimum time between progress point syncs to avoid spamming the server");
                }
                
                ImGui.Unindent();
            }
        }
    }
}
