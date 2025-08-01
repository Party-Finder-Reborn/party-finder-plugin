
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
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
    
    // Data Cache
    protected static List<PopularItem> PopularTags = new();
    private static bool _popularTagsLoaded = false;
    
    // UI constants
    protected static readonly Vector4 Yellow = new(1.0f, 1.0f, 0.0f, 1.0f);
    protected static readonly Vector4 Green = new(0.0f, 1.0f, 0.0f, 1.0f);
    protected static readonly Vector4 Red = new(1.0f, 0.0f, 0.0f, 1.0f);
    protected static readonly Vector4 Orange = new(1.0f, 0.5f, 0.0f, 1.0f);
    
    // Role selection popup state
    protected bool _showRoleSelectionPopup = false;
    protected string _selectedRole = string.Empty;
    protected Action<string>? _roleSelectionCallback;

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
        
        DutySelectorModal = new DutySelectorModal(ContentFinderService);
        
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
            Svc.Log.Info($"Refreshing party listing {Listing.Id}");
            
            var refreshedListing = await ApiService.GetListingAsync(Listing.Id);
            
            if (refreshedListing != null)
            {
                // Update the current listing with fresh data from server
                Listing = refreshedListing;

                Svc.Log.Info($"Successfully refreshed listing for duty #{Listing.CfcId} (ID: {Listing.Id}, CurrentSize: {Listing.CurrentSize})");
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
            
            if (isCompleted)
            {
                ImGui.TextColored(Green, $"  ✓ #{dutyId} {dutyName} (Cleared)");
            }
            else
            {
                ImGui.TextColored(Orange, $"  ✗ #{dutyId} {dutyName} (Not Cleared)");
            }
        }
        
        ImGui.Unindent();
    }
    
    protected void DrawProgressionStatus(List<uint> requiredProgPoints)
    {
        ImGui.Indent();
        ImGui.Text("Your Progress:");
        
        foreach (var actionId in requiredProgPoints)
        {
            var actionName = ActionNameService.Get(actionId);
            var hasSeen = DutyProgressService.HasSeenProgPoint(Listing.CfcId, actionId);
            
            if (hasSeen)
            {
                ImGui.TextColored(Green, $"  ✓ {actionName} (Seen)");
            }
            else
            {
                ImGui.TextColored(Orange, $"  ✗ {actionName} (Not Seen)");
            }
        }
        
        ImGui.Unindent();
    }
    
    protected void DrawRoster(PartyListing listing)
    {
        if (ImGui.BeginTable("roster_table", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Role");
            ImGui.TableSetupColumn("Player");
            ImGui.TableHeadersRow();

            // Example roster data - replace with actual participant data when available
            var roster = new Dictionary<string, string>
            {
                { "Tank", "Filled" },
                { "Tank", "Open" },
                { "Healer", "Filled" },
                { "Healer", "Open" },
                { "DPS", "Filled" },
                { "DPS", "Filled" },
                { "DPS", "Open" },
                { "DPS", "Open" }
            };

            foreach (var (role, playerName) in roster)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(role);
                ImGui.TableNextColumn();
                ImGui.Text(playerName);
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
    
    protected List<uint> ParseProgPointFromString(string progPointStr)
    {
        var progPoints = new List<uint>();
        if (string.IsNullOrWhiteSpace(progPointStr))
            return progPoints;
        
        var parts = progPointStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmedPart = part.Trim();
            
            if (uint.TryParse(trimmedPart, out var actionId))
            {
                progPoints.Add(actionId);
                continue;
            }
            
            var digitsOnly = new string(trimmedPart.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digitsOnly) && uint.TryParse(digitsOnly, out var extractedId))
            {
                progPoints.Add(extractedId);
                continue;
            }
            
            try
            {
                var matchingActions = ActionNameService.SearchByName(trimmedPart).ToList();
                if (matchingActions.Count > 0)
                {
                    progPoints.Add(matchingActions.First().id);
                }
                else
                {
                    Svc.Log.Debug($"Could not resolve progress point name '{trimmedPart}' to action ID");
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Error resolving progress point name '{trimmedPart}': {ex.Message}");
            }
        }
        
        return progPoints;
    }
    
    protected string FormatProgPointsAsString(List<uint> progPoints)
    {
        if (progPoints.Count == 0)
            return string.Empty;
        
        var names = progPoints.Select(actionId => ActionNameService.Get(actionId));
        return string.Join(", ", names);
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
            var progPoints = DutyProgressService.GetSeenProgPoints(dutyId);
            
            _cachedProgPoints = progPoints;
            _cachedProgPointsDutyId = dutyId;
            
            if (progPoints.Count == 0)
            {
                var asyncProgPoints = await DutyProgressService.GetCompletedProgPointsAsync(dutyId);
                if (asyncProgPoints != null && asyncProgPoints.Count > 0)
                {
                    _cachedProgPoints = asyncProgPoints;
                }
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
    /// Show role selection popup with a callback for when role is selected
    /// </summary>
    protected void ShowRoleSelectionPopup(Action<string> onRoleSelected)
    {
        _roleSelectionCallback = onRoleSelected;
        _selectedRole = string.Empty;
        _showRoleSelectionPopup = true;
    }
    
    /// <summary>
    /// Draw the role selection popup - should be called in Draw() method
    /// </summary>
    protected void DrawRoleSelectionPopup()
    {
        if (!_showRoleSelectionPopup) return;
        
        ImGui.OpenPopup("Select Role");
        if (ImGui.BeginPopupModal("Select Role", ref _showRoleSelectionPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Please select your role:");
            ImGui.Separator();

            if (ImGui.RadioButton("Tank", _selectedRole == "Tank"))
                _selectedRole = "Tank";
            ImGui.SameLine();
            if (ImGui.RadioButton("Healer", _selectedRole == "Healer"))
                _selectedRole = "Healer";
            ImGui.SameLine();
            if (ImGui.RadioButton("DPS", _selectedRole == "DPS"))
                _selectedRole = "DPS";

            if (ImGui.Button("Confirm") && !string.IsNullOrEmpty(_selectedRole))
            {
                _roleSelectionCallback?.Invoke(_selectedRole);
                _showRoleSelectionPopup = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _showRoleSelectionPopup = false;
            }

            ImGui.EndPopup();
        }
    }
}

