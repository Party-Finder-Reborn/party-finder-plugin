using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace PartyFinderReborn.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;

    public ConfigWindow(Plugin plugin) : base("Party Finder Reborn Configuration")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(400, 300);
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
    }
}
