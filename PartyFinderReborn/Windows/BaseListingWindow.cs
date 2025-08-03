
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using ImGuiNET;
using Lumina.Excel.Sheets;
using PartyFinderReborn.Models;
using PartyFinderReborn.Services;
using PartyFinderReborn.Utils;

namespace PartyFinderReborn.Windows;

public abstract class BaseListingWindow : Window, IDisposable
{
    // === PROTECTED READONLY CORE SERVICES ===
    protected readonly Plugin Plugin;
    protected readonly PartyFinderApiService ApiService;
    protected readonly ContentFinderService ContentFinderService;
    protected readonly ActionNameService ActionNameService;
    protected readonly DutyProgressService DutyProgressService;
    protected readonly ActionTrackingService ActionTrackingService;
    protected readonly WorldService WorldService;
    protected readonly PluginService PluginService;
    
    // === STATE MANAGEMENT ===
    protected PartyListing Listing;
    protected bool IsSaving;
    protected bool IsCreateMode;
    protected bool IsEditing;
    
    // Async loading states
    protected bool _isJoining = false;
    protected bool _isLeaving = false;
    protected bool _isRefreshing = false;
    protected bool _progPointsLoading = false;
    protected List<uint>? _cachedProgPoints = null;
    protected uint _cachedProgPointsDutyId = 0;
    
    // Protected getters for derived classes
    protected bool IsJoining => _isJoining;
    protected bool IsLeaving => _isLeaving;
    protected bool IsRefreshing => _isRefreshing;
    protected bool ProgPointsLoading => _progPointsLoading;
    
    // UI Components
    protected DutySelectorModal DutySelectorModal;
    protected PluginSelectorModal PluginSelectorModal;
    
    // Data Cache
    protected static List<PopularItem> PopularTags = new();
    private static bool _popularTagsLoaded = false;
    
    // UI constants
    protected static readonly Vector4 Yellow = new(1.0f, 1.0f, 0.0f, 1.0f);
    protected static readonly Vector4 Green = new(0.0f, 1.0f, 0.0f, 1.0f);
    protected static readonly Vector4 Red = new(1.0f, 0.0f, 0.0f, 1.0f);
    protected static readonly Vector4 Orange = new(1.0f, 0.5f, 0.0f, 1.0f);
    
    // Job selection popup state
    protected bool _showJobSelectionPopup = false;
    protected string _selectedJob = string.Empty;
    protected Action<string>? _jobSelectionCallback;
    
    // Job categories with their respective jobs
    protected static readonly Dictionary<string, List<string>> JobCategories = new()
    {
        ["Tank"] = new() { "Paladin", "Warrior", "Dark Knight", "Gunbreaker" },
        ["Healer"] = new() { "White Mage", "Scholar", "Astrologian", "Sage" },
        ["Melee DPS"] = new() { "Monk", "Dragoon", "Ninja", "Samurai", "Reaper", "Viper" },
        ["Physical Ranged DPS"] = new() { "Bard", "Machinist", "Dancer" },
        ["Magical Ranged DPS"] = new() { "Black Mage", "Summoner", "Red Mage", "Pictomancer", "Blue Mage" }
    };

    protected BaseListingWindow(Plugin plugin, PartyListing listing, string name) : base(name)
    {
        Plugin = plugin;
        Listing = listing;
        
        // Assign services from plugin
        ApiService = plugin.ApiService;
        ContentFinderService = plugin.ContentFinderService;
        ActionNameService = plugin.ActionNameService;
        DutyProgressService = plugin.DutyProgressService;
        ActionTrackingService = plugin.ActionTrackingService;
        WorldService = plugin.WorldService;
        PluginService = plugin.PluginService;
        
        DutySelectorModal = new DutySelectorModal(ContentFinderService);
        PluginSelectorModal = new PluginSelectorModal(PluginService);
        
        // Load popular tags if not already loaded
        if (!_popularTagsLoaded)
        {
            _ = LoadPopularTagsAsync();
        }
    }

    public void Dispose()
    {
        // Only remove from WindowSystem if it's still registered
        try
        {
            if (Plugin.WindowSystem.Windows.Contains(this))
            {
                Plugin.WindowSystem.RemoveWindow(this);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error removing window from WindowSystem: {ex.Message}");
        }
    }

    public override void OnClose()
    {
        base.OnClose();
        Dispose();
    }

    public override void Draw()
    {
        if (IsSaving || _isJoining || _isLeaving || _isRefreshing)
        {
            LoadingHelper.DrawLoadingSpinner();
        }
    }

    protected async Task LoadPopularTagsAsync()
    {
        if (_popularTagsLoaded) return;
        
        try
        {
            // Check if API key is valid before making request
            if (!Plugin.ConfigWindow.ShouldAllowApiRequests)
            {
                Svc.Log.Debug("Skipping popular tags load - API key validation required");
                return;
            }
            
            var response = await ApiService.GetPopularTagsAsync();
            if (response != null)
            {
                PopularTags = response.Results.Take(10).ToList(); // Limit to top 10 for the detail window
                _popularTagsLoaded = true;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load popular tags: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Refresh the listing data from the server
    /// </summary>
    protected async Task RefreshListingAsync()
    {
        if (_isRefreshing)
            return;
            
        _isRefreshing = true;
        
        try
        {
            // Check if API key is valid before making request
            if (!Plugin.ConfigWindow.ShouldAllowApiRequests)
            {
                Svc.Log.Warning("Cannot refresh listing - API key validation required");
                return;
            }
            
            
            var refreshedListing = await ApiService.GetListingAsync(Listing.Id);
            
            if (refreshedListing != null)
            {
                // Update the current listing with fresh data from server
                Listing = refreshedListing;

            }
            else
            {
                Svc.Log.Warning($"Failed to refresh listing {Listing.Id} - listing may no longer exist");
                Svc.Chat.PrintError($"[Party Finder Reborn] Failed to refresh listing - it may no longer exist");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error refreshing listing: {ex.Message}");
            Svc.Chat.PrintError($"[Party Finder Reborn] Error refreshing listing: {ex.Message}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }
    
    protected void DrawRequiredClearsStatus(List<uint> requiredClears)
    {
        ImGui.Indent();
        ImGui.Text("Your Completion Status:");
        
        foreach (var dutyId in requiredClears)
        {
            var dutyName = ContentFinderService.GetDutyDisplayName(dutyId);
            var isCompleted = DutyProgressService.IsDutyCompleted(dutyId);
            
            // If synchronous check says not completed, try async fallback for more thorough check
            if (!isCompleted)
            {
                // Start async check but don't await - this will populate the cache for next time
                _ = Task.Run(async () => await DutyProgressService.IsDutyCompletedAsync(dutyId));
            }
            
            if (isCompleted)
            {
                ImGui.Text("  ");
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, Green);
                ImGui.TextUnformatted(FontAwesomeIcon.Check.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(Green, $" #{dutyId} {dutyName} (Cleared)");
            }
            else
            {
                ImGui.Text("  ");
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, Orange);
                ImGui.TextUnformatted(FontAwesomeIcon.Times.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(Orange, $" #{dutyId} {dutyName} (Not Cleared)");
            }
        }
        
        ImGui.Unindent();
    }
    
    protected void DrawProgressionStatus(List<uint> requiredProgPoints)
    {
        foreach (var actionId in requiredProgPoints)
        {
            // Use server-provided friendly name first, fallback to ActionNameService
            var actionName = DutyProgressService.GetProgPointFriendlyName(Listing.CfcId, actionId);
            
            // Check if we got a server-provided friendly name or need to use ActionNameService
            var isServerFriendlyName = !actionName.StartsWith("Action #");
            if (!isServerFriendlyName)
            {
                actionName = ActionNameService.Get(actionId);
            }
            
            var hasSeen = DutyProgressService.HasSeenProgPoint(Listing.CfcId, actionId);
            
            // If not seen locally, try async check to update cache for next time
            if (!hasSeen)
            {
                _ = Task.Run(async () => await DutyProgressService.IsProgPointSeenAsync(Listing.CfcId, actionId));
            }
            
            // Display the icon and text on the same line
            ImGui.PushFont(UiBuilder.IconFont);
            if (hasSeen)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Green);
                ImGui.TextUnformatted(FontAwesomeIcon.Check.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(Green, $" {actionName} (Clear)");
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Orange);
                ImGui.TextUnformatted(FontAwesomeIcon.Times.ToIconString());
                ImGui.PopStyleColor();
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(Orange, $" {actionName} (Not Clear)");
            }
        }
    }
    
    protected void DrawRoster(PartyListing listing)
    {
        if (ImGui.BeginTable("roster_table", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Job");
            ImGui.TableSetupColumn("Player");
            ImGui.TableHeadersRow();

            // Show actual participant data using Job property
            for (var i = 0; i < listing.MaxSize; i++)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                
                if (i < listing.Participants.Count)
                {
                    var participant = listing.Participants[i];
                    // Display actual job name instead of generic role
                    ImGui.Text(!string.IsNullOrEmpty(participant.Job) ? participant.Job : "Any Job");
                    ImGui.TableNextColumn();
                    ImGui.Text(participant.Name);
                }
                else
                {
                    // Show "Any Job" for open slots instead of generic role categories
                    ImGui.TextDisabled("Any Job");
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled("Open Slot");
                }
            }

            ImGui.EndTable();
        }
    }
    
    /// <summary>
    /// Join the party listing asynchronously
    /// </summary>
    protected async Task JoinPartyAsync()
    {
        if (_isJoining)
            return;
            
        _isJoining = true;
        
        try
        {
            // Check if API key is valid before making request
            if (!Plugin.ConfigWindow.ShouldAllowApiRequests)
            {
                Svc.Chat.PrintError("[Party Finder Reborn] Cannot join party - API key validation required");
                return;
            }
            
            // Validate required plugins client-side before attempting to join
            if (!ValidateRequiredPlugins(Listing.RequiredPlugins))
            {
                // Validation failed, error message already shown in ValidateRequiredPlugins
                return;
            }
            
            Svc.Log.Info($"Attempting to join party listing {Listing.Id}");
            
            var joinResult = await ApiService.JoinListingAsync(Listing.Id);
            
            if (joinResult != null && joinResult.Success)
            {
                Svc.Log.Info($"Successfully joined party: {joinResult.Message}");
                
                Svc.Chat.Print($"[Party Finder Reborn] {joinResult.Message}");
                
                if (!string.IsNullOrEmpty(joinResult.PfCode))
                {
                    try
                    {
                        ImGui.SetClipboardText(joinResult.PfCode);
                        Svc.Chat.Print($"[Party Finder Reborn] Party Finder code '{joinResult.PfCode}' copied to clipboard!");
                        Svc.Log.Info($"Copied PF code to clipboard: {joinResult.PfCode}");
                    }
                    catch (Exception ex)
                    {
                        Svc.Log.Error($"Failed to copy PF code to clipboard: {ex.Message}");
                        Svc.Chat.PrintError($"[Party Finder Reborn] Failed to copy PF code to clipboard. PF Code: {joinResult.PfCode}");
                    }
                }
                
                if (joinResult.PartyFull)
                {
                    Listing.Status = "full";
                    Svc.Log.Info("Party is now full, updated local status");
                }
                
                await RefreshListingAsync();
                
                IsOpen = false;
            }
            else
            {
                var errorMessage = joinResult?.Message ?? "Unknown error occurred while joining party";
                Svc.Log.Error($"Failed to join party: {errorMessage}");
                Svc.Chat.PrintError($"[Party Finder Reborn] Failed to join party: {errorMessage}");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error joining party: {ex.Message}");
            Svc.Chat.PrintError($"[Party Finder Reborn] Error joining party: {ex.Message}");
        }
        finally
        {
            _isJoining = false;
        }
    }
    
    /// <summary>
    /// Leave the party listing asynchronously
    /// </summary>
    protected async Task LeavePartyAsync()
    {
        if (_isLeaving)
            return;
            
        _isLeaving = true;
        
        try
        {
            // Check if API key is valid before making request
            if (!Plugin.ConfigWindow.ShouldAllowApiRequests)
            {
                Svc.Chat.PrintError("[Party Finder Reborn] Cannot leave party - API key validation required");
                return;
            }
            
            Svc.Log.Info($"Attempting to leave party listing {Listing.Id}");
            
            var leaveResult = await ApiService.LeaveListingAsync(Listing.Id);
            
            if (leaveResult != null && leaveResult.Success)
            {
                Svc.Log.Info($"Successfully left party: {leaveResult.Message}");

                Svc.Chat.Print($"[Party Finder Reborn] {leaveResult.Message}");

                await RefreshListingAsync();
            }
            else
            {
                var errorMessage = leaveResult?.Message ?? "Unknown error occurred while leaving party";
                Svc.Log.Error($"Failed to leave party: {errorMessage}");
                Svc.Chat.PrintError($"[Party Finder Reborn] Failed to leave party: {errorMessage}");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error leaving party: {ex.Message}");
            Svc.Chat.PrintError($"[Party Finder Reborn] Error leaving party: {ex.Message}");
        }
        finally
        {
            _isLeaving = false;
        }
    }
    
    protected List<uint> ParseProgPointFromStringUsingServerData(string progPointStr, uint cfcId)
    {
        var progPoints = new List<uint>();
        if (string.IsNullOrWhiteSpace(progPointStr))
            return progPoints;
        
        Svc.Log.Debug($"Parsing progression point string: '{progPointStr}' for duty {cfcId}");
        
        // Get the cached friendly names for this duty
        var activeFriendlyNames = DutyProgressService.GetActiveCfcId() == cfcId ? 
            DutyProgressService.GetActiveProgPointFriendlyNames() : null;
            
        if (activeFriendlyNames == null)
        {
            Svc.Log.Warning($"No friendly names loaded for duty {cfcId}. Attempting to load from server...");
            
            // Try to load progression points from server as fallback
            _ = Task.Run(async () => await DutyProgressService.LoadAndCacheAllowedProgPointsAsync(cfcId));
            
            return progPoints;
        }
        
        // Log available friendly names for debugging
        Svc.Log.Debug($"Available friendly names for duty {cfcId}: [{string.Join(", ", activeFriendlyNames.Select(kvp => $"{kvp.Key}='{kvp.Value}'"))}]");
        
        var parts = progPointStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmedPart = part.Trim();
            Svc.Log.Debug($"Looking for friendly name match for: '{trimmedPart}'");
            
            // Find action ID by matching friendly name (case-insensitive)
            var matchingEntry = activeFriendlyNames.FirstOrDefault(kvp => 
                string.Equals(kvp.Value, trimmedPart, StringComparison.OrdinalIgnoreCase));
            
            if (matchingEntry.Key != 0) // Found a match
            {
                Svc.Log.Debug($"Found match: '{trimmedPart}' -> action ID {matchingEntry.Key}");
                progPoints.Add(matchingEntry.Key);
            }
            else
            {
                Svc.Log.Warning($"No matching progression point for '{trimmedPart}' in server data for duty {cfcId}");
                
                // Try direct parsing as uint as fallback
                if (uint.TryParse(trimmedPart, out var directId))
                {
                    Svc.Log.Debug($"Using direct ID fallback: {directId}");
                    progPoints.Add(directId);
                }
                else
                {
                    Svc.Log.Warning($"Could not parse '{trimmedPart}' as action ID either - skipping");
                }
            }
        }
        
        Svc.Log.Debug($"Final parsed progression points: [{string.Join(", ", progPoints)}]");
        return progPoints;
    }
    
    /// <summary>
    /// Validates that all required plugins are installed before allowing the user to join
    /// </summary>
    /// <param name="requiredPlugins">List of required plugin names from the listing</param>
    /// <returns>True if all plugins are installed, false otherwise</returns>
    protected bool ValidateRequiredPlugins(List<string> requiredPlugins)
    {
        if (requiredPlugins == null || requiredPlugins.Count == 0)
            return true;
        
        try
        {
            var installedPlugins = PluginService.GetInstalled().ToList();
            var missingPlugins = new List<string>();
            
            foreach (var requiredPlugin in requiredPlugins)
            {
                // Check if the plugin is installed by friendly name or internal name
                var isInstalled = installedPlugins.Any(p => 
                    p.Name.Equals(requiredPlugin, StringComparison.OrdinalIgnoreCase) ||
                    p.InternalName.Equals(requiredPlugin, StringComparison.OrdinalIgnoreCase));
                
                if (!isInstalled)
                {
                    missingPlugins.Add(requiredPlugin);
                }
            }
            
            if (missingPlugins.Count > 0)
            {
                var pluginList = string.Join(", ", missingPlugins);
                Svc.Chat.PrintError($"[Party Finder Reborn] Cannot join party - missing required plugins: {pluginList}");
                Svc.Log.Warning($"Join blocked due to missing required plugins: {pluginList}");
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error validating required plugins: {ex.Message}");
            // In case of error, allow the join attempt (fail open)
            return true;
        }
    }
    
    protected List<uint> FormatProgPointsForJson(List<uint> progPoints)
    {
        return progPoints;
    }

    protected async Task<List<uint>> GetUserSeenProgPointsAsync(uint dutyId)
    {
        try
        {
            return await DutyProgressService.GetCompletedProgPointsAsync(dutyId) ?? new List<uint>();
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to get user seen prog points: {ex.Message}");
        }
        
        return new List<uint>();
    }

    protected List<uint> GetAvailableProgPointsWithCache(uint dutyId)
    {
        if (dutyId == 0)
            return new List<uint>();
        
        if (_cachedProgPointsDutyId == dutyId && _cachedProgPoints != null)
        {
            return _cachedProgPoints;
        }
        
        if (_progPointsLoading)
        {
            return new List<uint>();
        }
        
        if (_cachedProgPointsDutyId != dutyId || _cachedProgPoints == null)
        {
            _ = LoadProgPointsAsync(dutyId);
        }
        
        return _cachedProgPoints ?? new List<uint>();
    }

    private async Task LoadProgPointsAsync(uint dutyId)
    {
        if (_progPointsLoading || dutyId == 0)
            return;
            
        _progPointsLoading = true;
        
        try
        {
            // Get allowed prog points for this duty from the server
            var allowedProgPoints = DutyProgressService.GetActiveAllowedProgPoints();
            
            // Filter to only show allowed prog points for the current duty
            var progPoints = new List<uint>();
            if (allowedProgPoints != null)
            {
                progPoints = allowedProgPoints.ToList();
            }
            
            _cachedProgPoints = progPoints;
            _cachedProgPointsDutyId = dutyId;
            
            // If no active allowed prog points, try to get them from the API
            if (progPoints.Count == 0)
            {
                _ = Task.Run(async () => await DutyProgressService.LoadAndCacheAllowedProgPointsAsync(dutyId));
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load progress points for duty {dutyId}: {ex.Message}");
            _cachedProgPoints = new List<uint>();
        }
        finally
        {
            _progPointsLoading = false;
        }
    }
    
    /// <summary>
    /// Close (delete) the listing as the owner
    /// </summary>
    protected async Task CloseListingAsync()
    {
        if (IsSaving) return;
        IsSaving = true;

        try
        {
            // Check if API key is valid before making request
            if (!Plugin.ConfigWindow.ShouldAllowApiRequests)
            {
                Svc.Chat.PrintError("[Party Finder Reborn] Cannot close listing - API key validation required");
                return;
            }
            
            Svc.Log.Info($"Closing party listing {Listing.Id}");
            var success = await ApiService.DeleteListingAsync(Listing.Id);

            if (success)
            {
                Svc.Log.Info($"Successfully closed listing {Listing.Id}");
                Svc.Chat.Print("[Party Finder Reborn] Your party listing has been closed.");
                IsOpen = false;
            }
            else
            {
                Svc.Log.Error($"Failed to close listing {Listing.Id}");
                Svc.Chat.PrintError("[Party Finder Reborn] Failed to close your party listing.");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error closing listing: {ex.Message}");
            Svc.Chat.PrintError("[Party Finder Reborn] An error occurred while closing your listing.");
        }
        finally
        {
            IsSaving = false;
        }
    }
    
    /// <summary>
    /// Show job selection popup with a callback for when job is selected
    /// </summary>
    protected void ShowJobSelectionPopup(Action<string> onJobSelected)
    {
        _jobSelectionCallback = onJobSelected;
        _selectedJob = string.Empty;
        _showJobSelectionPopup = true;
    }
    
    /// <summary>
    /// Draw the job selection popup - should be called in Draw() method
    /// </summary>
    protected void DrawJobSelectionPopup()
    {
        if (!_showJobSelectionPopup) return;
        
        ImGui.OpenPopup("Select Job");
        if (ImGui.BeginPopupModal("Select Job", ref _showJobSelectionPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Please select your job:");
            ImGui.Separator();

            // Create collapsible headers for each category
            foreach (var category in JobCategories)
            {
                if (ImGui.CollapsingHeader(category.Key))
                {
                    ImGui.Indent();
                    foreach (var job in category.Value)
                    {
                        if (ImGui.RadioButton(job, _selectedJob == job))
                            _selectedJob = job;
                    }
                    ImGui.Unindent();
                }
            }

            ImGui.Separator();
            
            if (ImGui.Button("Confirm") && !string.IsNullOrEmpty(_selectedJob))
            {
                _jobSelectionCallback?.Invoke(_selectedJob);
                _showJobSelectionPopup = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _showJobSelectionPopup = false;
            }

            ImGui.EndPopup();
        }
    }
}

