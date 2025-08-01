using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Linq;
using PartyFinderReborn.Models;
using System.Collections.Generic;
using ECommons.DalamudServices;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using PartyFinderReborn.Utils;

namespace PartyFinderReborn.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin;
    private UserProfile? CurrentUserProfile;
    private List<PartyListing> PartyListings;
    private List<PartyListing> FilteredListings;
    private ListingFilters CurrentFilters;
    private bool IsLoading;
    private string ConnectionStatus;
    private DateTime LastRefresh;
    private Dictionary<string, BaseListingWindow> OpenDetailWindows;
    private List<PopularItem> PopularTags;
    private string NewFilterTag;
    
    // Pagination state
    private string? _nextPageUrl;
    private string? _prevPageUrl;
    private int _totalCount;
    private ApiResponse<PartyListing>? _currentResponse;

    public MainWindow(Plugin plugin) : base("Party Finder Reborn##main_window")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(900, 700),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        PartyListings = new List<PartyListing>();
        FilteredListings = new List<PartyListing>();
        CurrentFilters = new ListingFilters();
        IsLoading = false;
        ConnectionStatus = "Disconnected";
        LastRefresh = DateTime.MinValue;
        OpenDetailWindows = new Dictionary<string, BaseListingWindow>();
        PopularTags = new List<PopularItem>();
        NewFilterTag = "";

        // Load data on initialization
        _ = LoadUserDataAsync();
        _ = LoadPartyListingsAsync();
        _ = LoadPopularTagsAsync();
    }

    private async Task LoadUserDataAsync()
    {
        try
        {
            ConnectionStatus = "Connecting...";
            var profile = await Plugin.ApiService.GetUserProfileAsync();
            CurrentUserProfile = profile;
            ConnectionStatus = "Connected";
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load user data: {ex.Message}");
            ConnectionStatus = "Error fetching user data";
            CurrentUserProfile = null; // Clear profile on error
        }
    }
    
    /// <summary>
    /// Refresh all authentication-dependent data - used when API key changes
    /// </summary>
    public async Task RefreshAllAuthenticatedDataAsync()
    {
        try
        {
            // Reset state
            CurrentUserProfile = null;
            PartyListings.Clear();
            FilteredListings.Clear();
            PopularTags.Clear();
            _nextPageUrl = null;
            _prevPageUrl = null;
            _totalCount = 0;
            _currentResponse = null;
            
            // Reload all data that requires authentication
            await LoadUserDataAsync();
            await LoadPartyListingsAsync();
            await LoadPopularTagsAsync();
            
            // Trigger duty progress refresh through the plugin
            _ = Plugin.DutyProgressService.RefreshProgressData();
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to refresh authenticated data: {ex.Message}");
        }
    }

    public async Task LoadPartyListingsAsync(string? pageUrl = null)
    {
        try
        {
            IsLoading = true;
            var response = await Plugin.ApiService.GetListingsAsync(CurrentFilters, pageUrl);
            
if (response != null)
            {
                _currentResponse = response;
                PartyListings = response.Results;
                _nextPageUrl = response.Next;
                _prevPageUrl = response.Previous;
                _totalCount = response.Count;
                
                ApplyFilters();
                LastRefresh = DateTime.Now;

                // Start notification workers for listings where the current user is the owner
                foreach (var listing in PartyListings)
                {
                    if (listing.IsOwner)
                    {
                        Plugin.StartJoinNotificationWorker(listing.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load party listings: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPartyListingsPageAsync(string? pageUrl)
    {
        if (string.IsNullOrEmpty(pageUrl)) return;
        await LoadPartyListingsAsync(pageUrl);
    }

    private async Task LoadPopularTagsAsync()
    {
        try
        {
            var response = await Plugin.ApiService.GetPopularTagsAsync();
            if (response != null)
            {
                PopularTags = response.Results.Take(20).ToList(); // Limit to top 20 popular tags
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load popular tags: {ex.Message}");
        }
    }

    private void ApplyFilters()
    {
        FilteredListings = PartyListings.Where(listing =>
        {
            // Search filter
            if (!string.IsNullOrEmpty(CurrentFilters.Search) && 
                !listing.Description.Contains(CurrentFilters.Search, StringComparison.OrdinalIgnoreCase))
                return false;

            // Status filter
            if (!string.IsNullOrEmpty(CurrentFilters.Status) && listing.Status != CurrentFilters.Status)
                return false;

            // Datacenter filter
            if (!string.IsNullOrEmpty(CurrentFilters.Datacenter) && 
                listing.Datacenter != CurrentFilters.Datacenter)
                return false;

            // World filter
            if (!string.IsNullOrEmpty(CurrentFilters.World) && 
                listing.World != CurrentFilters.World)
                return false;

            // RP flag filter
            if (CurrentFilters.RpFlag.HasValue && listing.IsRoleplay != CurrentFilters.RpFlag.Value)
                return false;

            // Owner filter - only show listings where user is owner
            if (CurrentFilters.IsOwner.HasValue && listing.IsOwner != CurrentFilters.IsOwner.Value)
                return false;

            // Tag filters - listing must contain all selected tags
            if (CurrentFilters.Tags.Count > 0)
            {
                foreach (var filterTag in CurrentFilters.Tags)
                {
                    if (!listing.UserTags.Any(tag => tag.Contains(filterTag, StringComparison.OrdinalIgnoreCase)))
                        return false;
                }
            }

            return true;
        }).ToList();
    }

    private void OpenListingWindow(PartyListing listing)
    {
        // Check if window for this listing is already open
        if (OpenDetailWindows.TryGetValue(listing.Id, out var existingWindow))
        {
            // If window exists and is open, just focus it
            if (existingWindow.IsOpen)
            {
                // Window is already open, just bring it to focus
                return;
            }
            else
            {
                // Window exists but is closed, remove it from tracking
                OpenDetailWindows.Remove(listing.Id);
                Plugin.WindowSystem.RemoveWindow(existingWindow);
                existingWindow.Dispose();
            }
        }
        
        // Create new ViewListingWindow for read-only display
        var viewWindow = new ViewListingWindow(Plugin, listing);
        Plugin.WindowSystem.AddWindow(viewWindow);
        viewWindow.IsOpen = true;
        
        // Track the window
        OpenDetailWindows[listing.Id] = viewWindow;
    }

    private void OpenCreateListingWindow()
    {
        // Create a new blank listing for creation
        var newListing = new PartyListing
        {
            Id = "create_new", // Key for create mode
            CfcId = 0, // Will be set by user
            Description = "",
            Status = "draft",
            UserTags = new List<string>(),
            UserStrategies = new List<string>(),
            MinItemLevel = 0,
            MaxItemLevel = 0,
            RequiredClears = new List<uint>(),
            ProgPoint = "",
            ExperienceLevel = "fresh",
            RequiredPlugins = new List<string>(),
            VoiceChatRequired = false,
            JobRequirements = new Dictionary<string, object>(),
            LootRules = "need_greed",
            ParseRequirement = "none",
            Datacenter = Plugin.WorldService.GetApiDatacenterName(Plugin.WorldService.GetCurrentPlayerHomeDataCenter() ?? ""),
            World = Plugin.WorldService.GetCurrentPlayerHomeWorld() ?? "",
            PfCode = "",
            Creator = CurrentUserProfile,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        
        // Check if a create window is already open
        var createKey = "create_new";
        if (OpenDetailWindows.TryGetValue(createKey, out var existingCreateWindow))
        {
            if (existingCreateWindow.IsOpen)
            {
                return; // Already have a create window open
            }
            else
            {
                OpenDetailWindows.Remove(createKey);
                Plugin.WindowSystem.RemoveWindow(existingCreateWindow);
                existingCreateWindow.Dispose();
            }
        }
        
        var createWindow = new CreateEditListingWindow(Plugin, newListing, true); // true = create mode
        Plugin.WindowSystem.AddWindow(createWindow);
        createWindow.IsOpen = true;
        
        // Track the create window
        OpenDetailWindows[createKey] = createWindow;
    }

    public void Dispose() 
    {
        // Clean up all open detail windows
        foreach (var window in OpenDetailWindows.Values)
        {
            try
            {
                if (Plugin.WindowSystem.Windows.Contains(window))
                {
                    Plugin.WindowSystem.RemoveWindow(window);
                }
                window.Dispose();
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Error disposing window: {ex.Message}");
            }
        }
        OpenDetailWindows.Clear();
    }

    private void CleanupClosedWindows()
    {
        // Remove closed windows from tracking
        var closedWindows = OpenDetailWindows.Where(kvp => !kvp.Value.IsOpen).ToList();
        foreach (var closedWindow in closedWindows)
        {
            try
            {
                // Remove from WindowSystem first, then dispose
                if (Plugin.WindowSystem.Windows.Contains(closedWindow.Value))
                {
                    Plugin.WindowSystem.RemoveWindow(closedWindow.Value);
                }
                // Since we already removed it from WindowSystem, BaseListingWindow.Dispose() won't try to remove it again
                closedWindow.Value.Dispose();
                OpenDetailWindows.Remove(closedWindow.Key);
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Error cleaning up closed window: {ex.Message}");
                // Still try to remove from tracking even if disposal failed
                OpenDetailWindows.Remove(closedWindow.Key);
            }
        }
    }

    public override void Draw()
    {
        // Clean up any closed windows
        CleanupClosedWindows();
        
        DrawHeader();
        ImGui.Separator();
        
        // Main content area
        var contentSize = ImGui.GetContentRegionAvail();
        
        // Calculate filter panel width - smaller and properly sized
        var filterPanelWidth = Math.Max(200, contentSize.X * 0.2f); // Reduced to 20% with 200px minimum
        
        // Filters on the left - no border to prevent clipping issues
        ImGui.BeginChild("FiltersPanel##filters", new Vector2(filterPanelWidth, contentSize.Y), false);
        DrawFilters();
        ImGui.EndChild();

        ImGui.SameLine();

        // Party listings table on the right
        ImGui.BeginChild("ListingsPanel##listings", new Vector2(contentSize.X - filterPanelWidth - 10, contentSize.Y), false);
        
        // Calculate space for pagination at the bottom
        var childContentSize = ImGui.GetContentRegionAvail();
        var paginationHeight = 30f;
        
        // Draw table in the remaining space above pagination
        ImGui.BeginChild("TableArea##table", new Vector2(childContentSize.X, childContentSize.Y - paginationHeight), false);
        DrawListingsTable();
        ImGui.EndChild();
        
        // Draw pagination controls at the bottom of the listings panel
        DrawPaginationControls();
        
        ImGui.EndChild();
        
        // Draw loading spinner overlay if loading
        if (IsLoading)
        {
            LoadingHelper.DrawLoadingSpinner();
        }
    }

    private void DrawHeader()
    {
        // Connection status and user info in a header row
        ImGui.Columns(3, "HeaderColumns", false);
        
        // Left: Connection status
        ImGui.Text($"Status: {ConnectionStatus}");
        if (IsLoading)
        {
            ImGui.SameLine();
            ImGui.Text("(Loading...)");
        }
        
        ImGui.NextColumn();
        
        // Center: General info
        var displayCount = _totalCount > 0 ? $"Showing: {FilteredListings.Count} | Total: {_totalCount}" : $"Total Parties: {FilteredListings.Count}";
        ImGui.Text(displayCount);
        
        ImGui.NextColumn();
        
        // Right: User info
        if (CurrentUserProfile != null)
        {
            ImGui.Text($"User: {CurrentUserProfile.DisplayName}");
        }
        else
        {
            ImGui.Text("Not logged in");
        }
        
        ImGui.Columns(1);
        
        // Action buttons
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Sync))
        {
            // Reset pagination state on refresh
            _nextPageUrl = null;
            _prevPageUrl = null;
            _totalCount = 0;
            _currentResponse = null;
            
            _ = LoadPartyListingsAsync();
            _ = LoadPopularTagsAsync(); // Also refresh popular tags
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Refresh Listings");
        }
        
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            OpenCreateListingWindow();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Create Listing");
        }

        ImGui.SameLine();
        ImGui.Text($"Last Refresh: {LastRefresh:HH:mm:ss}");
    }

    private void DrawFilters()
    {
        ImGui.Text("Filters");
        ImGui.Separator();
        
        // Search filter
        var search = CurrentFilters.Search ?? "";
        if (ImGui.InputText("Search", ref search, 100))
        {
            CurrentFilters.Search = string.IsNullOrEmpty(search) ? null : search;
            ResetPaginationAndRefresh();
        }
        
        // Status filter
        var statusItems = new[] { "All", "active", "full", "completed" };
        var currentStatusIndex = string.IsNullOrEmpty(CurrentFilters.Status) ? 0 : 
            Array.IndexOf(statusItems, CurrentFilters.Status);
        if (currentStatusIndex == -1) currentStatusIndex = 0;
        
        if (ImGui.Combo("Status", ref currentStatusIndex, statusItems, statusItems.Length))
        {
            CurrentFilters.Status = currentStatusIndex == 0 ? null : statusItems[currentStatusIndex];
            ResetPaginationAndRefresh();
        }
        
        // Datacenter filter with safe guards against missing world lists
        var datacenters = Plugin.WorldService.GetAllDatacenters();
        var datacenterItems = new[] { "All" }.Concat(datacenters.Select(dc => dc.Name.ExtractText())).ToArray();
        var currentDatacenterIndex = 0;
        
        if (!string.IsNullOrEmpty(CurrentFilters.Datacenter))
        {
            var currentDc = Plugin.WorldService.GetDatacenterByName(CurrentFilters.Datacenter);
            if (currentDc.HasValue)
            {
                var dcName = currentDc.Value.Name.ExtractText();
                currentDatacenterIndex = Array.IndexOf(datacenterItems, dcName);
                // Guard against missing datacenter - reset safely
                if (currentDatacenterIndex == -1) 
                {
                    currentDatacenterIndex = 0;
                    CurrentFilters.Datacenter = null; // Reset filter to prevent inconsistent state
                }
            }
        }
        
        if (ImGui.Combo("Datacenter", ref currentDatacenterIndex, datacenterItems, datacenterItems.Length))
        {
            if (currentDatacenterIndex == 0)
            {
                CurrentFilters.Datacenter = null;
                // When datacenter changes to "All", also clear world filter to prevent orphaned world selections
                CurrentFilters.World = null;
            }
            else
            {
                var selectedDatacenterName = datacenterItems[currentDatacenterIndex];
                var selectedDatacenter = datacenters.FirstOrDefault(dc => dc.Name.ExtractText() == selectedDatacenterName);
                if (!selectedDatacenter.Equals(default(Lumina.Excel.Sheets.WorldDCGroupType)))
                {
                    CurrentFilters.Datacenter = Plugin.WorldService.GetApiDatacenterName(selectedDatacenter.Name.ExtractText());
                    // When datacenter changes, reset world filter to prevent invalid world selections
                    CurrentFilters.World = null;
                }
            }
            ResetPaginationAndRefresh();
        }
        
        // RP Flag filter
        var rpFlagItems = new[] { "All", "RP Only", "Non-RP Only" };
        var currentRpIndex = !CurrentFilters.RpFlag.HasValue ? 0 : (CurrentFilters.RpFlag.Value ? 1 : 2);
        
        if (ImGui.Combo("RP Filter", ref currentRpIndex, rpFlagItems, rpFlagItems.Length))
        {
            CurrentFilters.RpFlag = currentRpIndex switch
            {
                1 => true,
                2 => false,
                _ => null
            };
            ResetPaginationAndRefresh();
        }
        
        // My Listings toggle filter
        var myListingsEnabled = CurrentFilters.IsOwner.HasValue && CurrentFilters.IsOwner.Value;
        if (ImGui.Checkbox("My Listings Only", ref myListingsEnabled))
        {
            CurrentFilters.IsOwner = myListingsEnabled ? true : null;
            ResetPaginationAndRefresh();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show only party listings you created");
        }
        
        ImGui.Separator();

        // Popular Tags
        ImGui.Text("Popular Tags");
        
        if (ImGui.BeginListBox("##populartags", new Vector2(0, 100)))
        {
            foreach (var tag in PopularTags.Select(t => t.Name))
            {
                if (ImGui.Selectable(tag, tag == NewFilterTag))
                {
                    NewFilterTag = tag;
                }
            }
            ImGui.EndListBox();
        }
        
        // Tag filter entry - rearrange to prevent clipping
        ImGui.InputText("Add Filter Tag", ref NewFilterTag, 50);
        if (ImGui.Button("Add Tag") && !string.IsNullOrWhiteSpace(NewFilterTag) && !CurrentFilters.Tags.Contains(NewFilterTag))
        {
            CurrentFilters.Tags.Add(NewFilterTag);
            NewFilterTag = "";
            ResetPaginationAndRefresh();
        }

        // Display current filter tags
        ImGui.Text("Filter Tags:");
        for (int i = CurrentFilters.Tags.Count - 1; i >= 0; i--)
        {
            ImGui.Text($"• {CurrentFilters.Tags[i]}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##filtertag{i}"))
            {
                CurrentFilters.Tags.RemoveAt(i);
                ResetPaginationAndRefresh();
            }
        }
        
        ImGui.Separator();

        // Reset filters button
        if (ImGui.Button("Reset Filters"))
        {
            CurrentFilters.Reset();
            ResetPaginationAndRefresh();
        }
    }

    private void DrawListingsTable()
    {
        ImGui.Text($"Party Listings ({FilteredListings.Count} found)");
        
        if (ImGui.BeginTable("PartiesTable", 5, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | 
            ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Duty Name", ImGuiTableColumnFlags.WidthFixed, 180);
            ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Roster", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableHeadersRow();

            foreach (var listing in FilteredListings)
            {
                ImGui.TableNextRow();

                // Duty Name column (clickable)
                if (ImGui.TableNextColumn())
                {
                    var dutyName = Plugin.ContentFinderService.GetDutyDisplayName(listing.CfcId);
                    if (ImGui.Selectable(dutyName, false, ImGuiSelectableFlags.SpanAllColumns))
                    {
                        OpenListingWindow(listing);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Click to view details");
                    }
                }

                // Description column
                if (ImGui.TableNextColumn())
                {
                    var description = string.IsNullOrWhiteSpace(listing.Description) ? "No description" : listing.Description;
                    // Use dynamic text wrapping for description
                    ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + ImGui.GetColumnWidth());
                    ImGui.TextWrapped(description);
                    ImGui.PopTextWrapPos();
                    
                    // Tooltip for full text if description is long
                    if (ImGui.IsItemHovered() && description.Length > 80)
                    {
                        ImGui.SetTooltip(description);
                    }
                }

                // Roster count column
                if (ImGui.TableNextColumn())
                {
                    var rosterText = $"{listing.CurrentSize}/{listing.MaxSize}";
                    var rosterColor = listing.CurrentSize >= listing.MaxSize 
                        ? new Vector4(1.0f, 1.0f, 0.0f, 1.0f)  // Yellow when full
                        : new Vector4(1.0f, 1.0f, 1.0f, 1.0f); // White when not full
                    ImGui.TextColored(rosterColor, rosterText);
                }

                // Location column
                if (ImGui.TableNextColumn())
                {
                    ImGui.Text(listing.LocationDisplay);
                }

                // Tags column
                if (ImGui.TableNextColumn())
                {
                    var tagsDisplay = string.IsNullOrWhiteSpace(listing.TagsDisplay) ? "No tags" : listing.TagsDisplay;
                    // Use dynamic text wrapping instead of manual truncation
                    ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + ImGui.GetColumnWidth());
                    ImGui.TextWrapped(tagsDisplay);
                    ImGui.PopTextWrapPos();
                    
                    // Fallback tooltip for full text
                    if (ImGui.IsItemHovered() && tagsDisplay.Length > 40)
                    {
                        ImGui.SetTooltip(tagsDisplay);
                    }
                }
            }

            ImGui.EndTable();
        }
        
        if (FilteredListings.Count == 0)
        {
            ImGui.Text("No parties found matching current filters.");
        }
    }
    
    private void DrawPaginationControls()
    {
        if (_currentResponse == null) return;
        
        ImGui.Separator();
        
        // Pagination info and controls
        ImGui.Columns(3, "PaginationColumns", false);
        
        // Left: Previous button
        var hasPrevious = !string.IsNullOrEmpty(_prevPageUrl);
        if (!hasPrevious) ImGui.BeginDisabled();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.AngleLeft) && hasPrevious)
        {
            _ = LoadPartyListingsPageAsync(_prevPageUrl);
        }
        if (ImGui.IsItemHovered() && hasPrevious)
        {
            ImGui.SetTooltip("Previous Page");
        }
        if (!hasPrevious) ImGui.EndDisabled();
        
        ImGui.NextColumn();
        
        // Center: Page info
        var pageInfo = $"Total: {_totalCount} listings";
        var textSize = ImGui.CalcTextSize(pageInfo);
        var columnWidth = ImGui.GetColumnWidth();
        var textPosX = (columnWidth - textSize.X) * 0.5f;
        if (textPosX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + textPosX);
        ImGui.Text(pageInfo);
        
        ImGui.NextColumn();
        
        // Right: Next button
        var hasNext = !string.IsNullOrEmpty(_nextPageUrl);
        var nextColumnWidth = ImGui.GetColumnWidth();
        var buttonSize = new Vector2(ImGui.GetFrameHeight()); // Square button size
        var buttonPosX = nextColumnWidth - buttonSize.X - 20; // 20 for padding
        if (buttonPosX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + buttonPosX);
        
        if (!hasNext) ImGui.BeginDisabled();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.AngleRight) && hasNext)
        {
            _ = LoadPartyListingsPageAsync(_nextPageUrl);
        }
        if (ImGui.IsItemHovered() && hasNext)
        {
            ImGui.SetTooltip("Next Page");
        }
        if (!hasNext) ImGui.EndDisabled();
        
        ImGui.Columns(1);
    }
    
    
    /// <summary>
    /// Reset pagination state and reload listings from the first page
    /// </summary>
    private void ResetPaginationAndRefresh()
    {
        _nextPageUrl = null;
        _prevPageUrl = null;
        _totalCount = 0;
        _currentResponse = null;
        
        // Load fresh data from the first page
        _ = LoadPartyListingsAsync();
    }
}
