using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Linq;
using PartyFinderReborn.Models;
using System.Collections.Generic;
using ECommons.DalamudServices;
using System.Threading.Tasks;

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
    private Dictionary<string, ListingDetailWindow> OpenDetailWindows;
    private List<PopularItem> PopularTags;
    private string NewFilterTag;

    public MainWindow(Plugin plugin) : base("Party Finder Reborn")
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
        OpenDetailWindows = new Dictionary<string, ListingDetailWindow>();
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
        }
    }

    private async Task LoadPartyListingsAsync()
    {
        try
        {
            IsLoading = true;
            var response = await Plugin.ApiService.GetListingsAsync(CurrentFilters);
            if (response != null)
            {
                PartyListings = response.Results;
                ApplyFilters();
                LastRefresh = DateTime.Now;
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
        
        // Create new window with unique ID
        var detailWindow = new ListingDetailWindow(Plugin, listing);
        Plugin.WindowSystem.AddWindow(detailWindow);
        detailWindow.IsOpen = true;
        
        // Track the window
        OpenDetailWindows[listing.Id] = detailWindow;
    }
    
    private void OpenCreateListingWindow()
    {
        // Create a new blank listing for creation
        var newListing = new PartyListing
        {
            Id = Guid.NewGuid().ToString(), // Generate temporary ID for create mode
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
            Datacenter = "", // Will be set based on user's current datacenter
            World = "", // Will be set based on user's current world
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
        
        var createWindow = new ListingDetailWindow(Plugin, newListing, true); // true = create mode
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
            if (Plugin.WindowSystem.Windows.Contains(closedWindow.Value))
            {
                Plugin.WindowSystem.RemoveWindow(closedWindow.Value);
            }
            closedWindow.Value.Dispose();
            OpenDetailWindows.Remove(closedWindow.Key);
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
        
        // Filters on the left
        ImGui.BeginChild("FiltersPanel", new Vector2(250, contentSize.Y), true);
        DrawFilters();
        ImGui.EndChild();

        ImGui.SameLine();

        // Party listings table on the right
        ImGui.BeginChild("ListingsPanel", new Vector2(contentSize.X - 260, contentSize.Y), false);
        DrawListingsTable();
        ImGui.EndChild();
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
        ImGui.Text($"Total Parties: {FilteredListings.Count}");
        
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
        if (ImGui.Button("🔄 Refresh Listings"))
        {
            _ = LoadPartyListingsAsync();
            _ = LoadPopularTagsAsync(); // Also refresh popular tags
        }
        
        ImGui.SameLine();
        if (ImGui.Button("➕ Create Listing"))
        {
            OpenCreateListingWindow();
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
            ApplyFilters();
        }
        
        // Status filter
        var statusItems = new[] { "All", "active", "full", "completed" };
        var currentStatusIndex = string.IsNullOrEmpty(CurrentFilters.Status) ? 0 : 
            Array.IndexOf(statusItems, CurrentFilters.Status);
        if (currentStatusIndex == -1) currentStatusIndex = 0;
        
        if (ImGui.Combo("Status", ref currentStatusIndex, statusItems, statusItems.Length))
        {
            CurrentFilters.Status = currentStatusIndex == 0 ? null : statusItems[currentStatusIndex];
            ApplyFilters();
        }
        
        // Datacenter filter
        var datacenterItems = new[] { "All" }.Concat(Datacenters.All.Values).ToArray();
        var currentDatacenterIndex = string.IsNullOrEmpty(CurrentFilters.Datacenter) ? 0 :
            Array.IndexOf(datacenterItems, Datacenters.All.GetValueOrDefault(CurrentFilters.Datacenter, CurrentFilters.Datacenter));
        if (currentDatacenterIndex == -1) currentDatacenterIndex = 0;
        
        if (ImGui.Combo("Datacenter", ref currentDatacenterIndex, datacenterItems, datacenterItems.Length))
        {
            if (currentDatacenterIndex == 0)
            {
                CurrentFilters.Datacenter = null;
            }
            else
            {
                var selectedDatacenter = datacenterItems[currentDatacenterIndex];
                CurrentFilters.Datacenter = Datacenters.All.FirstOrDefault(kvp => kvp.Value == selectedDatacenter).Key;
            }
            ApplyFilters();
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
            ApplyFilters();
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
        
        // Tag filter entry
        ImGui.InputText("Add Filter Tag", ref NewFilterTag, 50);
        ImGui.SameLine();
        if (ImGui.Button("Add Tag") && !string.IsNullOrWhiteSpace(NewFilterTag) && !CurrentFilters.Tags.Contains(NewFilterTag))
        {
            CurrentFilters.Tags.Add(NewFilterTag);
            NewFilterTag = "";
            ApplyFilters();
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
                ApplyFilters();
            }
        }
        
        ImGui.Separator();

        // Reset filters button
        if (ImGui.Button("Reset Filters"))
        {
            CurrentFilters.Reset();
            ApplyFilters();
        }
    }

    private void DrawListingsTable()
    {
        ImGui.Text($"Party Listings ({FilteredListings.Count} found)");
        
        if (ImGui.BeginTable("PartiesTable", 6, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | 
            ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Duty ID", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Experience", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Requirements", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var listing in FilteredListings)
            {
                ImGui.TableNextRow();

                // Duty ID column (clickable)
                if (ImGui.TableNextColumn())
                {
                    if (ImGui.Selectable($"#{listing.CfcId}", false, ImGuiSelectableFlags.SpanAllColumns))
                    {
                        OpenListingWindow(listing);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{listing.Description}\nClick to view details");
                    }
                }

                // Status column
                if (ImGui.TableNextColumn())
                {
                    var statusColor = listing.Status switch
                    {
                        "active" => new Vector4(0.0f, 1.0f, 0.0f, 1.0f), // Green
                        "full" => new Vector4(1.0f, 1.0f, 0.0f, 1.0f),   // Yellow
                        "completed" => new Vector4(0.5f, 0.5f, 0.5f, 1.0f), // Gray
                        _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f) // White
                    };
                    ImGui.TextColored(statusColor, listing.StatusDisplay);
                }

                // Experience level column
                if (ImGui.TableNextColumn())
                {
                    ImGui.Text(listing.ExperienceLevelDisplay);
                }

                // Requirements column
                if (ImGui.TableNextColumn())
                {
                    var requirements = listing.RequirementsDisplay;
                    if (requirements.Length > 30)
                        requirements = requirements.Substring(0, 27) + "...";
                    ImGui.Text(requirements);
                    if (ImGui.IsItemHovered() && listing.RequirementsDisplay.Length > 30)
                    {
                        ImGui.SetTooltip(listing.RequirementsDisplay);
                    }
                }

                // Location column
                if (ImGui.TableNextColumn())
                {
                    ImGui.Text(listing.LocationDisplay);
                }

                // Tags column
                if (ImGui.TableNextColumn())
                {
                    var tagsDisplay = listing.TagsDisplay;
                    if (tagsDisplay.Length > 40)
                        tagsDisplay = tagsDisplay.Substring(0, 37) + "...";
                    ImGui.Text(tagsDisplay);
                    if (ImGui.IsItemHovered() && listing.TagsDisplay.Length > 40)
                    {
                        ImGui.SetTooltip(listing.TagsDisplay);
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
}
