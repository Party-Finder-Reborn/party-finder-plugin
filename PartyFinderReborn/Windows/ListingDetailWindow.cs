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
    private string EditProgPoint;
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

    public ListingDetailWindow(Plugin plugin, PartyListing listing, bool createMode = false) 
        : base(createMode ? $"Create New Party Listing##create_{listing.Id}" : $"Duty #{listing.CfcId}##{listing.Id}")
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
        EditProgPoint = listing.ProgPoint;
        EditExperienceLevel = listing.ExperienceLevel;
        EditRequiredPlugins = new List<string>(listing.RequiredPlugins);
        EditVoiceChatRequired = listing.VoiceChatRequired;
        EditLootRules = listing.LootRules;
        EditParseRequirement = listing.ParseRequirement;
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
    }
    
    private void DrawViewMode()
    {
        // Header with basic information
        var dutyName = Plugin.ContentFinderService.GetDutyDisplayName(Listing.CfcId);
        ImGui.Text($"Duty: {dutyName}");
        ImGui.Text($"Content Finder ID: {Listing.CfcId}");
        ImGui.Text($"Status: {Listing.StatusDisplay}");
        ImGui.Text($"Experience Level: {Listing.ExperienceLevelDisplay}");
        ImGui.Text($"Location: {Listing.LocationDisplay}");
        
        if (!string.IsNullOrEmpty(Listing.PfCode))
        {
            ImGui.Text($"Party Finder Code: {Listing.PfCode}");
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
            ImGui.Text($"• Progression Point: {Listing.ProgPoint}");
        }
        
        if (Listing.RequiredClears.Count > 0)
        {
            ImGui.Text($"• Required Clears: {string.Join(", ", Listing.RequiredClears.Select(c => $"#{c}"))}");
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
        var datacenterItems = Datacenters.All.Values.ToArray();
        var currentDatacenterIndex = Array.IndexOf(datacenterItems, Datacenters.All.GetValueOrDefault(EditDatacenter, EditDatacenter));
        if (currentDatacenterIndex == -1) currentDatacenterIndex = 0;
        
        if (ImGui.Combo("##datacenter", ref currentDatacenterIndex, datacenterItems, datacenterItems.Length))
        {
            EditDatacenter = Datacenters.All.FirstOrDefault(kvp => kvp.Value == datacenterItems[currentDatacenterIndex]).Key;
        }
        
        ImGui.Text("World *");
        if (Datacenters.Worlds.TryGetValue(EditDatacenter, out var worlds))
        {
            var worldItems = worlds.ToArray();
            var currentWorldIndex = Array.IndexOf(worldItems, EditWorld);
            if (currentWorldIndex == -1) currentWorldIndex = 0;
            
            if (ImGui.Combo("##world", ref currentWorldIndex, worldItems, worldItems.Length))
            {
                EditWorld = worldItems[currentWorldIndex];
            }
        }
        else
        {
            ImGui.InputText("##world", ref EditWorld, 50);
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
        
        // Progression Point
        ImGui.Text("Progression Point");
        ImGui.InputText("##progpoint", ref EditProgPoint, 200);
        
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
                    EditProgPoint = Listing.ProgPoint;
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
            if (!IsCreateMode && ImGui.Button("Join Party") && Listing.IsActive)
            {
                // Handle join party action
                // Plugin.JoinParty(Listing.Id);
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
            if (ImGui.Button("Refresh") && !IsCreateMode)
            {
                // Handle refresh action
                // Plugin.RefreshListing(Listing.Id);
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
                ProgPoint = EditProgPoint.Trim(),
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
                WindowName = $"Duty #{Listing.CfcId}##{Listing.Id}";
                IsEditing = false;
                IsCreateMode = false;
                
                Svc.Log.Info($"Successfully {(IsCreateMode ? "created" : "updated")} listing for duty #{Listing.CfcId}");
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
}
