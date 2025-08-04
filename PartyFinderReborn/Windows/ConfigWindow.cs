using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using PartyFinderReborn.Services;
using Dalamud.Interface;
using ECommons.DalamudServices;

namespace PartyFinderReborn.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Plugin Plugin;
    private Configuration Configuration;
    private PartyFinderApiService ApiService;
    
    // API key validation state
    private bool _isValidatingApiKey = false;
    private bool? _apiKeyValid = null;
    private string _lastValidatedApiKey = string.Empty;
    private bool _hasSyncedDutiesAfterValidation = false;

    public ConfigWindow(Plugin plugin) : base("Party Finder Reborn Configuration##config_window")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;

        Size = new Vector2(500, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

        Plugin = plugin;
        Configuration = plugin.Configuration;
        ApiService = plugin.ApiService;
    }

    public void Dispose() { }
    
    /// <summary>
    /// Validates the API key on plugin startup (public method)
    /// </summary>
    public async Task ValidateApiKeyOnStartupAsync()
    {
        await ValidateApiKeyAsync();
    }
    
    /// <summary>
    /// Validates the current API key asynchronously
    /// </summary>
    private async Task ValidateApiKeyAsync()
    {
        var previousValidationState = _apiKeyValid;
        
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            _apiKeyValid = false;
            _lastValidatedApiKey = string.Empty;
        }
        else
        {
            // Don't validate the same key multiple times
            if (_lastValidatedApiKey == Configuration.ApiKey && _apiKeyValid.HasValue)
            {
                return;
            }
            
            _isValidatingApiKey = true;
            _lastValidatedApiKey = Configuration.ApiKey;
            
            try
            {
                var isValid = await ApiService.TestConnectionAsync();
                _apiKeyValid = isValid;
            }
            catch (Exception)
            {
                _apiKeyValid = false;
            }
            finally
            {
                _isValidatingApiKey = false;
            }
        }
        
        // If validation state changed from invalid to valid, refresh the main window data
        if (previousValidationState != true && _apiKeyValid == true)
        {
            _ = Plugin.MainWindow.RefreshAllAuthenticatedDataAsync();
            
            // Initialize and sync duties after successful API key validation if not yet synced
            if (!_hasSyncedDutiesAfterValidation)
            {
                _hasSyncedDutiesAfterValidation = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Initialize the DutyProgressService - this will do the force complete sync
                        await Plugin.DutyProgressService.InitializeAsync();
                    }
                    catch (Exception ex)
                    {
                        Svc.Log.Error($"Failed to initialize DutyProgressService after API key validation: {ex.Message}");
                    }
                });
            }
        }
    }
    
    /// <summary>
    /// Shows the API key validation feedback (checkmark, X, or loading spinner)
    /// </summary>
    private void ShowApiKeyValidationFeedback()
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            // Draw invisible text to maintain layout when API key is empty
            ImGui.TextUnformatted("");
            return;
        }
        
        if (_isValidatingApiKey)
        {
            // Show loading indicator
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
            ImGui.TextUnformatted(FontAwesomeIcon.Spinner.ToIconString());
            ImGui.PopStyleColor();
            ImGui.PopFont();
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Validating API key...");
            }
        }
        else if (_apiKeyValid.HasValue)
        {
            if (_apiKeyValid.Value)
            {
                // Show green checkmark
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 1.0f, 0.0f, 1.0f));
                ImGui.TextUnformatted(FontAwesomeIcon.Check.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("API key is valid");
                }
            }
            else
            {
                // Show red X
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                ImGui.TextUnformatted(FontAwesomeIcon.Times.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("API key is invalid or server is unreachable");
                }
            }
        }
    }
    
    /// <summary>
    /// Checks if the API key is valid (for use by other services)
    /// </summary>
    public bool IsApiKeyValid => _apiKeyValid == true;
    
    /// <summary>
    /// Gets whether API requests should proceed (API key is present and valid)
    /// </summary>
    public bool ShouldAllowApiRequests => !string.IsNullOrEmpty(Configuration.ApiKey) && IsApiKeyValid;

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
        }
        
        // Server Connection Settings
        if (ImGui.CollapsingHeader("Server Settings"))
        {
            var apiKey = Configuration.ApiKey;
            if (ImGui.InputText("API Key", ref apiKey, 200))
            {
                Configuration.ApiKey = apiKey;
                Configuration.Save();
                
                // Reset validation state when API key changes
                _apiKeyValid = null;
                _lastValidatedApiKey = string.Empty;
                _hasSyncedDutiesAfterValidation = false;
                
                // Trigger validation if key is not empty
                if (!string.IsNullOrEmpty(apiKey))
                {
                    _ = ValidateApiKeyAsync();
                }
            }
            
            // Show validation feedback next to the API key field
            ImGui.SameLine();
            ShowApiKeyValidationFeedback();
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Your API key for accessing the Party Finder server");
            }
            
            // Validate on first load if we have an API key
            if (_apiKeyValid == null && !string.IsNullOrEmpty(Configuration.ApiKey) && !_isValidatingApiKey)
            {
                _ = ValidateApiKeyAsync();
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
                
                
                ImGui.Unindent();
            }
        }
    }
}
