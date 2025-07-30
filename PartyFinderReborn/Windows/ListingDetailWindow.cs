using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using PartyFinderReborn.Models;
using System.Linq;
using System.Collections.Generic;
using ECommons.DalamudServices;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;

namespace PartyFinderReborn.Windows;

public class ListingDetailWindow : Window, IDisposable
{
    private PartyListing Listing;
    private Plugin Plugin;
    private bool IsCreateMode;
    private bool IsEditing;
    private bool IsSaving;
    
    // Editable fields
    private uint EditCfcId;
    private string EditDescription;
    private string EditStatus;
    private List<string> EditUserTags;
    private List<string> EditUserStrategies;
    private int EditMinItemLevel;
    private int EditMaxItemLevel;
    private List<uint> EditRequiredClears;
    private List<uint> EditProgPoint; // Changed from string to List<uint>
    private string EditExperienceLevel;
    private List<string> EditRequiredPlugins;
    private bool EditVoiceChatRequired;
    private string EditLootRules;
    private string EditParseRequirement;
    private string EditDatacenter;
    private string EditWorld;
    private string EditPfCode;
    private string NewTag;
    private string NewStrategy;
    private string NewRequiredPlugin;
    private List<PopularItem> PopularTags;
    
    // Duty selection state
    private ContentFinderCondition? SelectedDuty;
    private DutySelectorModal DutySelectorModal;
    
    // Async loading states
    private bool _progPointsLoading = false;
    private List<uint>? _cachedProgPoints = null;
    private uint _cachedProgPointsDutyId = 0;
    
    // Join/Leave party state
    private bool _isJoining = false;
    private bool _isLeaving = false;
    private bool _isRefreshing = false;

    public ListingDetailWindow(Plugin plugin, PartyListing listing, bool createMode = false) 
        : base(createMode ? $"Create New Party Listing##create_{listing.Id}" : $"{plugin.ContentFinderService.GetDutyDisplayName(listing.CfcId)}##detail_{listing.Id}")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700, 600),
            MaximumSize = new Vector2(1200, 900)
        };

        Plugin = plugin;
        Listing = listing;
        IsCreateMode = createMode;
        IsEditing = createMode;
        IsSaving = false;
        
        // Initialize editable fields
        // For create mode with CfcId = 0, use the first valid CfcId
        if (createMode && listing.CfcId == 0)
        {
            EditCfcId = Plugin.ContentFinderService.GetFirstValidCfcId();
        }
        else
        {
            EditCfcId = listing.CfcId;
        }
        
        EditDescription = listing.Description;
        EditStatus = listing.Status;
        EditUserTags = new List<string>(listing.UserTags);
        EditUserStrategies = new List<string>(listing.UserStrategies);
        EditMinItemLevel = listing.MinItemLevel;
        EditMaxItemLevel = listing.MaxItemLevel;
        EditRequiredClears = new List<uint>(listing.RequiredClears);
        // Initialize EditProgPoint: backward compatibility with string format
        EditProgPoint = ParseProgPointFromString(listing.ProgPoint);
        EditExperienceLevel = string.IsNullOrEmpty(listing.ExperienceLevel) ? "fresh" : listing.ExperienceLevel;
        EditRequiredPlugins = new List<string>(listing.RequiredPlugins);
        EditVoiceChatRequired = listing.VoiceChatRequired;
        EditLootRules = string.IsNullOrEmpty(listing.LootRules) ? "need_greed" : listing.LootRules;
        EditParseRequirement = string.IsNullOrEmpty(listing.ParseRequirement) ? "none" : listing.ParseRequirement;
        EditDatacenter = listing.Datacenter;
        EditWorld = listing.World;
        EditPfCode = listing.PfCode;
        NewTag = "";
        NewStrategy = "";
        NewRequiredPlugin = "";
        PopularTags = new List<PopularItem>();
        
        // Initialize duty selection state
        SelectedDuty = Plugin.ContentFinderService.GetContentFinderCondition(EditCfcId);
        DutySelectorModal = new DutySelectorModal(Plugin.ContentFinderService);
        
        // Load popular tags
        _ = LoadPopularTagsAsync();
        
        // Auto-refresh existing listings to ensure all fields are properly populated
        if (!createMode && !string.IsNullOrEmpty(listing.Id))
        {
            _ = RefreshListingAsync();
        }
    }

    public void Dispose() 
    {
        // Make sure the window removes itself from the WindowSystem when disposed
        // But only if it's actually registered
        try
        {
            if (Plugin.WindowSystem.Windows.Contains(this))
            {
                Plugin.WindowSystem.RemoveWindow(this);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error removing window from system: {ex.Message}");
        }
    }
    
    private async Task LoadPopularTagsAsync()
    {
        try
        {
            var response = await Plugin.ApiService.GetPopularTagsAsync();
            if (response != null)
            {
                PopularTags = response.Results.Take(10).ToList(); // Limit to top 10 for the detail window
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load popular tags: {ex.Message}");
        }
    }

    public override void Draw()
    {
        if (IsEditing)
        {
            DrawEditForm();
        }
        else
        {
            DrawViewMode();
        }
        
        ImGui.Separator();
        DrawActionButtons();
        
        // Draw the duty selector modal
        DutySelectorModal.Draw();
        
        // Draw loading spinner overlay if saving or joining
        if (IsSaving || _isJoining)
        {
            DrawLoadingSpinner();
        }
    }
    
    private void DrawViewMode()
    {
        // Header with basic information and participant count
        var dutyName = Plugin.ContentFinderService.GetDutyDisplayName(Listing.CfcId);
        ImGui.Text($"Duty: {dutyName}");
        ImGui.Text($"Content Finder ID: {Listing.CfcId}");
        
        // Party size with progress bar
        ImGui.Text($"Party Size: ({Listing.CurrentSize}/{Listing.MaxSize})");
        var progressRatio = (float)Listing.CurrentSize / Listing.MaxSize;
        ImGui.ProgressBar(progressRatio, new Vector2(-1, 0), $"{Listing.CurrentSize}/{Listing.MaxSize}");
        
        ImGui.Text($"Status: {Listing.StatusDisplay}");
        ImGui.Text($"Experience Level: {Listing.ExperienceLevelDisplay}");
        ImGui.Text($"Location: {Listing.LocationDisplay}");
        
        if (!string.IsNullOrEmpty(Listing.PfCode))
        {
            ImGui.Text($"Party Finder Code: {Listing.PfCode}");
        }

        ImGui.Separator();

        // Roster Section
        ImGui.Text("Party Roster:");
        if (ImGui.BeginChild("Roster", new Vector2(-1, 100), true))
        {
            var localPlayerName = Svc.ClientState.LocalPlayer?.Name?.TextValue ?? "";
            foreach (var participant in Listing.Participants)
            {
                var isLocalPlayer = participant == localPlayerName;
                if (isLocalPlayer)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.0f, 1.0f)); // Highlight local player
                    ImGui.Text($"• {participant} (You)");
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.Text($"• {participant}");
                }
            }
            ImGui.EndChild();
        }

        ImGui.Separator();

        // Description
        if (!string.IsNullOrEmpty(Listing.Description))
        {
            ImGui.Text("Description:");
            ImGui.TextWrapped(Listing.Description);
            ImGui.Separator();
        }

        // Requirements Section
        ImGui.Text("Requirements:");
        ImGui.Text(Listing.RequirementsDisplay);
        
        // Detailed Requirements
        if (Listing.MinItemLevel > 0 || Listing.MaxItemLevel > 0)
        {
            if (Listing.MaxItemLevel > 0)
                ImGui.Text($"• Item Level: {Listing.MinItemLevel} - {Listing.MaxItemLevel}");
            else
                ImGui.Text($"• Minimum Item Level: {Listing.MinItemLevel}");
        }
        
        if (!string.IsNullOrEmpty(Listing.ProgPoint))
        {
            // Try to display progress points as resolved action names
            var progPoints = ParseProgPointFromString(Listing.ProgPoint);
            if (progPoints.Count > 0)
            {
                var progPointNames = progPoints.Select(id => Plugin.ActionNameService.Get(id));
                ImGui.Text($"• Progress Points: {string.Join(", ", progPointNames)}");
                
                // Show progression status for each required progress point
                DrawProgressionStatus(progPoints);
            }
            else
            {
                // Fallback to displaying the raw string
                ImGui.Text($"• Progression Point: {Listing.ProgPoint}");
            }
        }
        
        if (Listing.RequiredClears.Count > 0)
        {
            ImGui.Text($"• Required Clears: {string.Join(", ", Listing.RequiredClears.Select(c => $"#{c}"))}");
            
            // Show completion status for each required clear
            DrawRequiredClearsStatus(Listing.RequiredClears);
        }
        
        if (Listing.RequiredPlugins.Count > 0)
        {
            ImGui.Text($"• Required Plugins: {string.Join(", ", Listing.RequiredPlugins)}");
        }
        
        if (Listing.VoiceChatRequired)
        {
            ImGui.Text("• Voice Chat Required");
        }
        
        ImGui.Text($"• Loot Rules: {Listing.LootRulesDisplay}");
        
        if (Listing.ParseRequirement != "none")
        {
            ImGui.Text($"• Parse Requirement: {Listing.ParseRequirementDisplay}");
        }

        ImGui.Separator();

        // Tags and Strategies
        if (Listing.UserTags.Count > 0)
        {
            ImGui.Text("Tags:");
            ImGui.TextWrapped(Listing.TagsDisplay);
        }

        if (Listing.UserStrategies.Count > 0)
        {
            ImGui.Text("Strategies:");
            ImGui.TextWrapped(Listing.StrategiesDisplay);
        }

        if (Listing.UserTags.Count > 0 || Listing.UserStrategies.Count > 0)
        {
            ImGui.Separator();
        }

        // Creator Information
        if (Listing.Creator != null)
        {
            ImGui.Text("Created by:");
            ImGui.Text($"Name: {Listing.CreatorDisplay}");
            ImGui.Text($"Created: {Listing.CreatedAt:yyyy-MM-dd HH:mm}");
            ImGui.Text($"Updated: {Listing.UpdatedAt:yyyy-MM-dd HH:mm}");
        }
    }
    
    private void DrawEditForm()
    {
        ImGui.Text(IsCreateMode ? "Create New Party Listing" : "Edit Party Listing");
        ImGui.Separator();
        
        // Use tabs for different sections
        if (ImGui.BeginTabBar("EditTabs"))
        {
            if (ImGui.BeginTabItem("Basic Info"))
            {
                DrawBasicInfoTab();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Requirements"))
            {
                DrawRequirementsTab();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Tags & Strategies"))
            {
                DrawTagsAndStrategiesTab();
                ImGui.EndTabItem();
            }
            
            ImGui.EndTabBar();
        }
    }
    
    private void DrawBasicInfoTab()
    {
        // Duty Selection
        ImGui.Text("Select Duty *");
        DrawDutySelector();
        
        // Description  
        ImGui.Text("Description");
        ImGui.InputTextMultiline("##description", ref EditDescription, 1000, new Vector2(-1, 100));
        
        // Server Information
        ImGui.Text("Datacenter *");
        var datacenters = Plugin.WorldService.GetAllDatacenters();
        var datacenterNames = datacenters.Select(dc => dc.Name.ToString()).ToArray();

        int currentDatacenterIndex = -1;
        if (!string.IsNullOrEmpty(EditDatacenter))
        {
            var currentDc = Plugin.WorldService.GetDatacenterByName(EditDatacenter);
            if (currentDc.HasValue)
            {
                currentDatacenterIndex = Array.FindIndex(datacenters, dc => dc.RowId == currentDc.Value.RowId);
            }
        }

        if (ImGui.Combo("##datacenter", ref currentDatacenterIndex, datacenterNames, datacenterNames.Length))
        {
            if (currentDatacenterIndex >= 0 && currentDatacenterIndex < datacenters.Length)
            {
                var selectedDatacenter = datacenters[currentDatacenterIndex];
                var newApiDcName = Plugin.WorldService.GetApiDatacenterName(selectedDatacenter.Name.ToString());

                if (EditDatacenter != newApiDcName)
                {
                    EditDatacenter = newApiDcName;
                    var worldsForNewDc = Plugin.WorldService.GetWorldsForDatacenter(selectedDatacenter.RowId);
                    var firstWorld = worldsForNewDc.FirstOrDefault();
                    EditWorld = !firstWorld.Equals(default(World)) ? firstWorld.Name.ToString() : "";
                }
            }
        }

        ImGui.Text("World *");
        var selectedDcInfo = Plugin.WorldService.GetDatacenterByName(EditDatacenter);

        if (selectedDcInfo.HasValue)
        {
            var worlds = Plugin.WorldService.GetWorldsForDatacenter(selectedDcInfo.Value.RowId);
            var worldNames = worlds.Select(w => w.Name.ToString()).ToArray();

            if (worldNames.Length > 0)
            {
                int currentWorldIndex = Array.IndexOf(worldNames, EditWorld);
                if (currentWorldIndex == -1)
                {
                    EditWorld = worldNames.FirstOrDefault() ?? "";
                    currentWorldIndex = 0;
                }

                if (ImGui.Combo("##world", ref currentWorldIndex, worldNames, worldNames.Length))
                {
                    if (currentWorldIndex >= 0 && currentWorldIndex < worldNames.Length)
                    {
                        EditWorld = worldNames[currentWorldIndex];
                    }
                }
            }
            else
            {
                ImGui.TextDisabled("No public worlds for this datacenter.");
                EditWorld = "";
            }
        }
        else
        {
            ImGui.BeginDisabled();
            string[] preview = { "Select a datacenter first" };
            int previewIndex = 0;
            ImGui.Combo("##world", ref previewIndex, preview, preview.Length);
            ImGui.EndDisabled();
        }
        
        // Party Finder Code
        ImGui.Text("Party Finder Code (Optional)");
        ImGui.InputText("##pfcode", ref EditPfCode, 10);
        
        // Status (only for existing listings)
        if (!IsCreateMode)
        {
            ImGui.Text("Status");
            var statusItems = PartyStatuses.All.Values.ToArray();
            var currentStatusIndex = Array.IndexOf(statusItems, PartyStatuses.All.GetValueOrDefault(EditStatus, EditStatus));
            if (currentStatusIndex == -1) currentStatusIndex = 0;
            
            if (ImGui.Combo("##status", ref currentStatusIndex, statusItems, statusItems.Length))
            {
                EditStatus = PartyStatuses.All.FirstOrDefault(kvp => kvp.Value == statusItems[currentStatusIndex]).Key;
            }
        }
    }
    
    private void DrawRequirementsTab()
    {
        // Experience Level
        ImGui.Text("Experience Level");
        var experienceLevels = new[] { "fresh", "some_exp", "experienced", "farm", "reclear" };
        var experienceDisplays = new[] { "Fresh/Learning", "Some Experience", "Experienced", "Farm/Clear", "Weekly Reclear" };
        var currentExpIndex = Array.IndexOf(experienceLevels, EditExperienceLevel);
        if (currentExpIndex == -1) currentExpIndex = 0;
        
        if (ImGui.Combo("##experience", ref currentExpIndex, experienceDisplays, experienceDisplays.Length))
        {
            EditExperienceLevel = experienceLevels[currentExpIndex];
        }
        
        // Item Level Requirements
        ImGui.Text("Item Level Requirements");
        ImGui.SliderInt("Min Item Level##minilvl", ref EditMinItemLevel, 0, 999);
        ImGui.SliderInt("Max Item Level##maxilvl", ref EditMaxItemLevel, 0, 999);
        
        // Progress Points (Boss Abilities)
        DrawProgressPointsSection();
        
        // Required Clears
        ImGui.Text("Required Clears");
        for (int i = EditRequiredClears.Count - 1; i >= 0; i--)
        {
            var dutyName = Plugin.ContentFinderService.GetDutyDisplayName(EditRequiredClears[i]);
            ImGui.Text($"• {dutyName}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##clear{i}"))
            {
                EditRequiredClears.RemoveAt(i);
            }
        }
        
        if (ImGui.Button("Add Required Clear"))
        {
            DutySelectorModal.Open(null, OnRequiredClearSelected);
        }
        
        // Required Plugins
        ImGui.Text("Required Plugins");
        for (int i = EditRequiredPlugins.Count - 1; i >= 0; i--)
        {
            ImGui.Text($"• {EditRequiredPlugins[i]}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##plugin{i}"))
            {
                EditRequiredPlugins.RemoveAt(i);
            }
        }
        
        ImGui.InputText("Add Required Plugin", ref NewRequiredPlugin, 100);
        ImGui.SameLine();
        if (ImGui.Button("Add Plugin") && !string.IsNullOrWhiteSpace(NewRequiredPlugin) && !EditRequiredPlugins.Contains(NewRequiredPlugin))
        {
            EditRequiredPlugins.Add(NewRequiredPlugin.Trim());
            NewRequiredPlugin = "";
        }
        
        // Voice Chat Required
        ImGui.Checkbox("Voice Chat Required", ref EditVoiceChatRequired);
        
        // Loot Rules
        ImGui.Text("Loot Rules");
        var lootRulesKeys = new[] { "ffa", "need_greed", "master_loot", "reserved", "discuss" };
        var lootRulesDisplays = new[] { "Free for All", "Need/Greed", "Master Loot", "Reserved Items", "Discuss Before" };
        var currentLootIndex = Array.IndexOf(lootRulesKeys, EditLootRules);
        if (currentLootIndex == -1) currentLootIndex = 1; // Default to need_greed
        
        if (ImGui.Combo("##lootrules", ref currentLootIndex, lootRulesDisplays, lootRulesDisplays.Length))
        {
            EditLootRules = lootRulesKeys[currentLootIndex];
        }
        
        // Parse Requirement
        ImGui.Text("Parse Requirement");
        var parseKeys = new[] { "none", "grey", "green", "blue", "purple", "orange", "gold" };
        var parseDisplays = new[] { "No Parse Requirement", "Grey+ (1-24th percentile)", "Green+ (25-49th percentile)", "Blue+ (50-74th percentile)", "Purple+ (75-94th percentile)", "Orange+ (95-98th percentile)", "Gold+ (99th percentile)" };
        var currentParseIndex = Array.IndexOf(parseKeys, EditParseRequirement);
        if (currentParseIndex == -1) currentParseIndex = 0;
        
        if (ImGui.Combo("##parserequirement", ref currentParseIndex, parseDisplays, parseDisplays.Length))
        {
            EditParseRequirement = parseKeys[currentParseIndex];
        }
    }
    
    private void DrawTagsAndStrategiesTab()
    {
        // Tags
        ImGui.Text("Tags");
        for (int i = EditUserTags.Count - 1; i >= 0; i--)
        {
            ImGui.Text($"• {EditUserTags[i]}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##tag{i}"))
            {
                EditUserTags.RemoveAt(i);
            }
        }
        
        ImGui.Text("Popular Tags");
        if (ImGui.BeginListBox("##popularlisttags", new Vector2(0, 100)))
        {
            foreach (var tag in PopularTags.Select(t => t.Name))
            {
                if (ImGui.Selectable(tag, tag == NewTag))
                {
                    NewTag = tag;
                }
            }
            ImGui.EndListBox();
        }

        ImGui.InputText("Add Tag", ref NewTag, 50);
        ImGui.SameLine();
        if (ImGui.Button("Add Tag") && !string.IsNullOrWhiteSpace(NewTag) && !EditUserTags.Contains(NewTag))
        {
            EditUserTags.Add(NewTag.Trim());
            NewTag = "";
        }
        
        ImGui.Separator();
        
        // Strategies
        ImGui.Text("Strategies");
        for (int i = EditUserStrategies.Count - 1; i >= 0; i--)
        {
            ImGui.Text($"• {EditUserStrategies[i]}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##strategy{i}"))
            {
                EditUserStrategies.RemoveAt(i);
            }
        }
        
        ImGui.Text("Popular Strategies/Tags");
        if (ImGui.BeginListBox("##popularstrategies", new Vector2(0, 80)))
        {
            foreach (var tag in PopularTags.Select(t => t.Name))
            {
                if (ImGui.Selectable(tag, tag == NewStrategy))
                {
                    NewStrategy = tag;
                }
            }
            ImGui.EndListBox();
        }
        
        ImGui.InputText("Add Strategy", ref NewStrategy, 100);
        ImGui.SameLine();
        if (ImGui.Button("Add Strategy") && !string.IsNullOrWhiteSpace(NewStrategy) && !EditUserStrategies.Contains(NewStrategy))
        {
            EditUserStrategies.Add(NewStrategy.Trim());
            NewStrategy = "";
        }
    }
    
    private void DrawActionButtons()
    {
        if (IsEditing)
        {
            // Save/Cancel buttons for edit mode
            if (IsSaving)
            {
                ImGui.Button("Saving...");
            }
            else if (ImGui.Button(IsCreateMode ? "Create Listing" : "Save Changes"))
            {
                _ = SaveListingAsync();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                if (IsCreateMode)
                {
                    IsOpen = false;
                }
                else
                {
                    IsEditing = false;
                    // Reset fields to original values
                    EditCfcId = Listing.CfcId;
                    EditDescription = Listing.Description;
                    EditStatus = Listing.Status;
                    EditUserTags = new List<string>(Listing.UserTags);
                    EditUserStrategies = new List<string>(Listing.UserStrategies);
                    EditMinItemLevel = Listing.MinItemLevel;
                    EditMaxItemLevel = Listing.MaxItemLevel;
                    EditRequiredClears = new List<uint>(Listing.RequiredClears);
                    EditProgPoint = ParseProgPointFromString(Listing.ProgPoint);
                    EditExperienceLevel = Listing.ExperienceLevel;
                    EditRequiredPlugins = new List<string>(Listing.RequiredPlugins);
                    EditVoiceChatRequired = Listing.VoiceChatRequired;
                    EditLootRules = Listing.LootRules;
                    EditParseRequirement = Listing.ParseRequirement;
                    EditDatacenter = Listing.Datacenter;
                    EditWorld = Listing.World;
                    EditPfCode = Listing.PfCode;
                }
            }
        }
        else
        {
            // View mode buttons
            if (!IsCreateMode && Listing.IsActive)
            {
                var partyFull = Listing.CurrentSize >= Listing.MaxSize;
                
                if (_isJoining)
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Joining...");
                    ImGui.EndDisabled();
                }
                else if (_isLeaving)
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Leaving...");
                    ImGui.EndDisabled();
                }
                else if (Listing.IsOwner)
                {
                    // User is owner - show Close Listing button
                    if (ImGui.Button("Close Listing"))
                    {
                        _ = CloseListingAsync();
                    }
                }
                else if (Listing.HasJoined)
                {
                    // User has joined - show Leave button
                    if (ImGui.Button("Leave Party"))
                    {
                        _ = LeavePartyAsync();
                    }
                }
                else if (!partyFull)
                {
                    // Not joined, not owner, not full - show Join button
                    if (ImGui.Button("Join Party"))
                    {
                        _ = JoinPartyAsync();
                    }
                }
                else if (partyFull)
                {
                    // Party is full - show disabled Join button
                    ImGui.BeginDisabled();
                    ImGui.Button("Party Full");
                    ImGui.EndDisabled();
                }
            }
            
            if (!IsCreateMode)
            {
                ImGui.SameLine();
                if (ImGui.Button("Edit"))
                {
                    IsEditing = true;
                }
            }
            
            ImGui.SameLine();
            if (_isRefreshing)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Refreshing...");
                ImGui.EndDisabled();
            }
            else if (ImGui.Button("Refresh") && !IsCreateMode)
            {
                _ = RefreshListingAsync();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Close"))
            {
                IsOpen = false;
            }
        }
    }
    
    private void DrawDutySelector()
    {
        // Display current selection as a selectable
        var currentDutyName = SelectedDuty.HasValue && !SelectedDuty.Value.Name.IsEmpty ? SelectedDuty.Value.Name.ExtractText() : "No duty selected";
        var currentDutyType = SelectedDuty.HasValue ? Plugin.ContentFinderService.GetContentTypeName(SelectedDuty.Value) : "";
        var displayText = SelectedDuty.HasValue ? $"{currentDutyName} ({currentDutyType})" : "Click to select duty...";
        
        // Make the current duty name selectable to open duty selection
        if (ImGui.Selectable(displayText, false, ImGuiSelectableFlags.None, new Vector2(0, 25)))
        {
            // Open duty selector modal
            DutySelectorModal.Open(SelectedDuty, OnDutySelected);
        }
        
        // Add tooltip to the selectable
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to change duty");
        }

        // Display current selection info
        if (SelectedDuty.HasValue)
        {
            ImGui.TextDisabled($"ID: {SelectedDuty.Value.RowId}, Level: {SelectedDuty.Value.ClassJobLevelRequired}, Item Level: {SelectedDuty.Value.ItemLevelRequired}");
        }

        ImGui.Separator();
    }

    private void OnDutySelected(ContentFinderCondition? selectedDuty)
    {
        SelectedDuty = selectedDuty;
        EditCfcId = selectedDuty?.RowId ?? 0;
    }
    
    private void OnRequiredClearSelected(ContentFinderCondition? selectedDuty)
    {
        if (selectedDuty.HasValue && !EditRequiredClears.Contains(selectedDuty.Value.RowId))
        {
            EditRequiredClears.Add(selectedDuty.Value.RowId);
        }
    }
    
    private async Task SaveListingAsync()
    {
        // Validation
        if (EditCfcId == 0)
        {
            Svc.Log.Warning("Content Finder ID is required");
            return;
        }
        
        if (string.IsNullOrWhiteSpace(EditDatacenter))
        {
            Svc.Log.Warning("Datacenter is required");
            return;
        }
        
        if (string.IsNullOrWhiteSpace(EditWorld))
        {
            Svc.Log.Warning("World is required");
            return;
        }
        
        IsSaving = true;
        
        try
        {
            // Update listing with edited values
            var updatedListing = new PartyListing
            {
                Id = Listing.Id,
                CfcId = EditCfcId,
                Description = EditDescription.Trim(),
                Status = IsCreateMode ? "active" : EditStatus,
                UserTags = new List<string>(EditUserTags),
                UserStrategies = new List<string>(EditUserStrategies),
                MinItemLevel = EditMinItemLevel,
                MaxItemLevel = EditMaxItemLevel,
                RequiredClears = new List<uint>(EditRequiredClears),
                ProgPoint = FormatProgPointsAsString(EditProgPoint),
                ExperienceLevel = EditExperienceLevel,
                RequiredPlugins = new List<string>(EditRequiredPlugins),
                VoiceChatRequired = EditVoiceChatRequired,
                JobRequirements = new Dictionary<string, object>(), // TODO: Implement job requirements UI
                LootRules = EditLootRules,
                ParseRequirement = EditParseRequirement,
                Datacenter = EditDatacenter,
                World = EditWorld,
                PfCode = EditPfCode.Trim(),
                Creator = Listing.Creator,
                CreatedAt = Listing.CreatedAt,
                UpdatedAt = DateTime.Now
            };
            
            PartyListing? result;
            if (IsCreateMode)
            {
                result = await Plugin.ApiService.CreateListingAsync(updatedListing);
            }
            else
            {
                result = await Plugin.ApiService.UpdateListingAsync(Listing.Id, updatedListing);
            }
            
            if (result != null)
            {
                // Update the current listing with the server response
                Listing = result;
                
                // Update SelectedDuty to match the listing's CfcId
                SelectedDuty = Plugin.ContentFinderService.GetContentFinderCondition(Listing.CfcId);
                
                var dutyName = Plugin.ContentFinderService.GetDutyDisplayName(Listing.CfcId);
                WindowName = $"{dutyName}##detail_{Listing.Id}";
                IsEditing = false;
                var wasCreateMode = IsCreateMode;
                IsCreateMode = false;
                
                Svc.Log.Info($"Successfully {(wasCreateMode ? "created" : "updated")} listing for duty {dutyName} (ID: {Listing.Id}, CurrentSize: {Listing.CurrentSize})");
            }
            else
            {
                Svc.Log.Error($"Failed to {(IsCreateMode ? "create" : "update")} listing");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error saving listing: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }
    
    /// <summary>
    /// Parse progress points from string format for backward compatibility
    /// Handles both numeric IDs and textual names (split by comma, strip non-digits, map via ActionNameService when possible)
    /// </summary>
    private List<uint> ParseProgPointFromString(string progPointStr)
    {
        var progPoints = new List<uint>();
        if (string.IsNullOrWhiteSpace(progPointStr))
            return progPoints;
        
        // Split by comma and process each part
        var parts = progPointStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmedPart = part.Trim();
            
            // First try to parse as numeric ID directly
            if (uint.TryParse(trimmedPart, out var actionId))
            {
                progPoints.Add(actionId);
                continue;
            }
            
            // If not numeric, try to handle textual names
            // Strip non-digits to see if there's a numeric part
            var digitsOnly = new string(trimmedPart.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digitsOnly) && uint.TryParse(digitsOnly, out var extractedId))
            {
                progPoints.Add(extractedId);
                continue;
            }
            
            // If we can't extract a numeric ID, try to map via ActionNameService
            // This is a reverse lookup - search for actions that match the name
            try
            {
                var matchingActions = Plugin.ActionNameService.SearchByName(trimmedPart).ToList();
                if (matchingActions.Count > 0)
                {
                    // Take the first match
                    progPoints.Add(matchingActions.First().id);
                }
                else
                {
                    // If no match found, log it for debugging but don't fail
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
    
    /// <summary>
    /// Convert progress points list to string format for API compatibility
    /// </summary>
    private string FormatProgPointsAsString(List<uint> progPoints)
    {
        if (progPoints.Count == 0)
            return string.Empty;
        
        // If we have action IDs, try to convert them to names for better readability
        var names = progPoints.Select(actionId => Plugin.ActionNameService.Get(actionId));
        return string.Join(", ", names);
    }

    /// <summary>
    /// Get user's seen progress points for the current duty from DutyProgressService
    /// </summary>
    private async Task<List<uint>> GetUserSeenProgPointsAsync(uint dutyId)
    {
        try
        {
            return await Plugin.DutyProgressService.GetCompletedProgPointsAsync(dutyId) ?? new List<uint>();
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to get user seen prog points: {ex.Message}");
        }
        
        return new List<uint>();
    }
    
    /// <summary>
    /// Get available progress points using cached results for responsive UI
    /// </summary>
    private List<uint> GetAvailableProgPointsWithCache(uint dutyId)
    {
        if (dutyId == 0)
            return new List<uint>();
        
        // Check if we have cached data for this duty
        if (_cachedProgPointsDutyId == dutyId && _cachedProgPoints != null)
        {
            return _cachedProgPoints;
        }
        
        // Check if we're already loading
        if (_progPointsLoading)
        {
            return new List<uint>();
        }
        
        // Start async load if needed
        if (_cachedProgPointsDutyId != dutyId || _cachedProgPoints == null)
        {
            _ = LoadProgPointsAsync(dutyId);
        }
        
        // Return cached data if available, or empty list while loading
        return _cachedProgPoints ?? new List<uint>();
    }
    
    /// <summary>
    /// Load progress points asynchronously and cache them
    /// </summary>
    private async Task LoadProgPointsAsync(uint dutyId)
    {
        if (_progPointsLoading || dutyId == 0)
            return;
            
        _progPointsLoading = true;
        
        try
        {
            // Use DutyProgressService to get seen progress points from cached local mirror
            // This is fast as it uses cached data from the service
            var progPoints = Plugin.DutyProgressService.GetSeenProgPoints(dutyId);
            
            // Cache the results
            _cachedProgPoints = progPoints;
            _cachedProgPointsDutyId = dutyId;
            
            // If no cached data available, try async fetch as fallback
            if (progPoints.Count == 0)
            {
                var asyncProgPoints = await Plugin.DutyProgressService.GetCompletedProgPointsAsync(dutyId);
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
    /// Draw completion status for required duty clears
    /// </summary>
    private void DrawRequiredClearsStatus(List<uint> requiredClears)
    {
        ImGui.Indent();
        ImGui.Text("Your Completion Status:");
        
        foreach (var dutyId in requiredClears)
        {
            var dutyName = Plugin.ContentFinderService.GetDutyDisplayName(dutyId);
            var isCompleted = Plugin.DutyProgressService.IsDutyCompleted(dutyId);
            
            if (isCompleted)
            {
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), $"  ✓ #{dutyId} {dutyName} (Cleared)");
            }
            else
            {
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), $"  ✗ #{dutyId} {dutyName} (Not Cleared)");
            }
        }
        
        ImGui.Unindent();
    }
    
    /// <summary>
    /// Draw progression status for required progress points
    /// </summary>
    private void DrawProgressionStatus(List<uint> requiredProgPoints)
    {
        ImGui.Indent();
        ImGui.Text("Your Progress:");
        
        foreach (var actionId in requiredProgPoints)
        {
            var actionName = Plugin.ActionNameService.Get(actionId);
            var hasSeen = Plugin.DutyProgressService.HasSeenProgPoint(Listing.CfcId, actionId);
            
            if (hasSeen)
            {
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), $"  ✓ {actionName} (Seen)");
            }
            else
            {
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), $"  ✗ {actionName} (Not Seen)");
            }
        }
        
        ImGui.Unindent();
    }
    
    /// <summary>
    /// Join the party listing asynchronously
    /// </summary>
    private async Task JoinPartyAsync()
    {
        if (_isJoining)
            return;
            
        _isJoining = true;
        
        try
        {
            Svc.Log.Info($"Attempting to join party listing {Listing.Id}");
            
            var joinResult = await Plugin.ApiService.JoinListingAsync(Listing.Id);
            
            if (joinResult != null && joinResult.Success)
            {
                Svc.Log.Info($"Successfully joined party: {joinResult.Message}");
                
                // Show success message via chat
                Svc.Chat.Print($"[Party Finder Reborn] {joinResult.Message}");
                
                // If pf_code is present, copy it to clipboard and show toast
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
                
                // Update local listing status if party is now full
                if (joinResult.PartyFull)
                {
                    Listing.Status = "full";
                    Svc.Log.Info("Party is now full, updated local status");
                }
                
                // Refresh the listing to get updated participant info
                await RefreshListingAsync();
                
                // Close the window after successful join
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
    private async Task LeavePartyAsync()
    {
        if (_isLeaving)
            return;
            
        _isLeaving = true;
        
        try
        {
            Svc.Log.Info($"Attempting to leave party listing {Listing.Id}");
            
            var leaveResult = await Plugin.ApiService.LeaveListingAsync(Listing.Id);
            
            if (leaveResult != null && leaveResult.Success)
            {
                Svc.Log.Info($"Successfully left party: {leaveResult.Message}");
                
                // Show success message via chat
                Svc.Chat.Print($"[Party Finder Reborn] {leaveResult.Message}");
                
                // Refresh the listing to get updated participant info
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
    
    /// <summary>
    /// Close (delete) the listing as the owner
    /// </summary>
    private async Task CloseListingAsync()
    {
        if (IsSaving) return;
        IsSaving = true;

        try
        {
            Svc.Log.Info($"Closing party listing {Listing.Id}");
            var success = await Plugin.ApiService.DeleteListingAsync(Listing.Id);

            if (success)
            {
                Svc.Log.Info($"Successfully closed listing {Listing.Id}");
                Svc.Chat.Print("[Party Finder Reborn] Your party listing has been closed.");
                IsOpen = false; // Close the window after successful deletion
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
            Svc.Chat.PrintError($"[Party Finder Reborn] An error occurred while closing your listing.");
        }
        finally
        {
            IsSaving = false;
        }
    }
    
    /// <summary>
    /// Refresh the listing data from the server
    /// </summary>
    private async Task RefreshListingAsync()
    {
        if (_isRefreshing)
            return;
            
        _isRefreshing = true;
        
        try
        {
            Svc.Log.Info($"Refreshing party listing {Listing.Id}");
            
            var refreshedListing = await Plugin.ApiService.GetListingAsync(Listing.Id);
            
            if (refreshedListing != null)
            {
                // Update the current listing with fresh data from server
                Listing = refreshedListing;
                
                // Update SelectedDuty to match the refreshed listing's CfcId
                SelectedDuty = Plugin.ContentFinderService.GetContentFinderCondition(Listing.CfcId);
                
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
    
    /// <summary>
    /// Draw the Progress Points (Boss Abilities) section with multi-select interface
    /// </summary>
    private void DrawProgressPointsSection()
    {
        ImGui.Text("Progress Points (Boss Abilities)");
        ImGui.TextDisabled("Select specific boss abilities that party members must have seen");
        
        try
        {
            // Show currently selected progress points
            ImGui.Text("Currently Selected:");
            if (EditProgPoint.Count > 0)
            {
                if (ImGui.BeginChild("SelectedProgPoints", new Vector2(-1, 80), true))
                {
                    for (int i = EditProgPoint.Count - 1; i >= 0; i--)
                    {
                        var actionId = EditProgPoint[i];
                        var actionName = Plugin.ActionNameService.Get(actionId);
                        ImGui.Text($"• {actionName}");
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"✕##remove_progpoint_{i}"))
                        {
                            EditProgPoint.RemoveAt(i);
                        }
                    }
                }
                ImGui.EndChild();
            }
            else
            {
                ImGui.TextDisabled("No progress points selected");
            }
            
            ImGui.Spacing();
            
            // Get available progress points from cached data or initiate async load
            var availableProgPoints = GetAvailableProgPointsWithCache(EditCfcId);
            
            ImGui.Text("Add Progress Points:");
            if (_progPointsLoading)
            {
                ImGui.TextDisabled("Loading available progress points...");
            }
            else if (availableProgPoints.Count > 0)
            {
                ImGui.TextDisabled($"Available abilities for this duty ({availableProgPoints.Count} total)");

                // Dropdown selector for adding progress points
                if (ImGui.BeginCombo("##select_progpoint", "Select an ability to add..."))
                {
                    foreach (var actionId in availableProgPoints)
                    {
                        // Skip already selected items
                        if (EditProgPoint.Contains(actionId))
                            continue;

                        var actionName = Plugin.ActionNameService.Get(actionId);

                        if (ImGui.Selectable($"{actionName}##add_progpoint_{actionId}"))
                        {
                            if (!EditProgPoint.Contains(actionId))
                            {
                                EditProgPoint.Add(actionId);
                            }
                        }
                    }
                    ImGui.EndCombo();
                }
            }
            else
            {
                ImGui.TextDisabled("No progress points available for this duty.");
                ImGui.TextDisabled("Progress points will appear here after you've encountered boss abilities in-game.");
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Error drawing progress points section: {ex.Message}");
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Error loading progress points");
        }
    }
    
    private void DrawLoadingSpinner()
    {
        var center = ImGui.GetIO().DisplaySize / 2;
        var drawList = ImGui.GetForegroundDrawList();
        var spinnerColor = ImGui.GetColorU32(ImGuiCol.ButtonHovered);
        var backgroundColor = ImGui.GetColorU32(ImGuiCol.FrameBg, 0.7f);
        var radius = 30;
        var thickness = 5;
        
        drawList.AddCircleFilled(center, radius + 5, backgroundColor);
        drawList.PathArcTo(center, radius, (float)(Math.PI * 0.5f * (Environment.TickCount / 100 % 4)), (float)(Math.PI * 0.5f * (Environment.TickCount / 100 % 4)) + (float)(Math.PI * 1.5f), 32);
        drawList.PathStroke(spinnerColor, ImDrawFlags.None, thickness);
    }
}
