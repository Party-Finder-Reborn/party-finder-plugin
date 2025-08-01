
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

namespace PartyFinderReborn.Windows
{
    public class CreateEditListingWindow : BaseListingWindow
    {
        // Editable fields
        private uint _editCfcId;
        private string _editDescription = string.Empty;
        private string _editStatus = string.Empty;
        private List<string> _editUserTags = new();
        private List<string> _editUserStrategies = new();
        private int _editMinItemLevel;
        private int _editMaxItemLevel;
        private List<uint> _editRequiredClears = new();
        private List<uint> _editProgPoint = new();
        private string _editExperienceLevel = string.Empty;
        private List<string> _editRequiredPlugins = new();
        private bool _editVoiceChatRequired;
        private string _editLootRules = string.Empty;
        private string _editParseRequirement = string.Empty;
        private string _editDatacenter = string.Empty;
        private string _editWorld = string.Empty;
        private string _editPfCode = string.Empty;
        private string _newTag = string.Empty;
        private string _newStrategy = string.Empty;
        private string _creatorRole = "DPS"; // Default role for creator
        private int _editMaxSize = 8; // Default party size

        // Duty selection state
        private ContentFinderCondition? _selectedDuty;
        
        public CreateEditListingWindow(Plugin plugin, PartyListing listing, bool createMode = false) : base(plugin, listing, $"{(createMode ? "Create" : "Edit")} Listing##edit_{listing.Id}")
        {
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(700, 600),
                MaximumSize = new Vector2(1200, 900)
            };

            IsCreateMode = createMode;
            IsEditing = true; // This window is always in edit mode

            // Initialize fields from the listing
            InitializeEditableFields(listing);
        }

        private void InitializeEditableFields(PartyListing listing)
        {
            _editCfcId = listing.CfcId == 0 ? ContentFinderService.GetFirstValidCfcId() : listing.CfcId;
            _editDescription = listing.Description;
            _editStatus = listing.Status;
            _editUserTags = new List<string>(listing.UserTags);
            _editUserStrategies = new List<string>(listing.UserStrategies);
            _editMinItemLevel = listing.MinItemLevel;
            _editMaxItemLevel = listing.MaxItemLevel;
            _editRequiredClears = new List<uint>(listing.RequiredClears);
            _editProgPoint = ParseProgPointFromString(listing.ProgPoint);
            _editExperienceLevel = string.IsNullOrEmpty(listing.ExperienceLevel) ? "fresh" : listing.ExperienceLevel;
            _editRequiredPlugins = new List<string>(listing.RequiredPlugins);
            _editVoiceChatRequired = listing.VoiceChatRequired;
            _editLootRules = string.IsNullOrEmpty(listing.LootRules) ? "need_greed" : listing.LootRules;
            _editParseRequirement = string.IsNullOrEmpty(listing.ParseRequirement) ? "none" : listing.ParseRequirement;
            _editDatacenter = listing.Datacenter;
            _editWorld = listing.World;
            _editPfCode = listing.PfCode;
            _editMaxSize = listing.MaxSize > 0 ? listing.MaxSize : 8; // Initialize party size with default 8
            _newTag = string.Empty;
            _newStrategy = string.Empty;
            _creatorRole = "DPS"; // Reset to default

            _selectedDuty = ContentFinderService.GetContentFinderCondition(_editCfcId);
        }

        public override void Draw()
        {
            DrawEditForm();
            ImGui.Separator();
            DrawActionButtons();
            
            DutySelectorModal.Draw();
            
            // Draw role selection popup from base class
            DrawRoleSelectionPopup();
            
            base.Draw();
        }

        private void DrawEditForm()
        {
            ImGui.Text(IsCreateMode ? "Create New Party Listing" : "Edit Party Listing");
            ImGui.Separator();

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
            
// Party Size Slider
            ImGui.Text("Party Size");
            ImGui.SliderInt("##maxSize", ref _editMaxSize, 4, 48);

            // Description  
            ImGui.Text("Description *");
            ImGui.InputTextMultiline("##description", ref _editDescription, 1000, new Vector2(-1, 100));
            
            // Server Information
            ImGui.Text("Datacenter *");
            var datacenters = WorldService.GetAllDatacenters();
            var datacenterNames = datacenters.Select(dc => dc.Name.ExtractText()).ToArray();

            int currentDatacenterIndex = -1;
            if (!string.IsNullOrEmpty(_editDatacenter))
            {
                var currentDc = WorldService.GetDatacenterByName(_editDatacenter);
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
                    var newApiDcName = WorldService.GetApiDatacenterName(selectedDatacenter.Name.ExtractText());

                    if (_editDatacenter != newApiDcName)
                    {
                        _editDatacenter = newApiDcName;
                        var worldsForNewDc = WorldService.GetWorldsForDatacenter(selectedDatacenter.RowId);
                        var firstWorld = worldsForNewDc.FirstOrDefault();
                        _editWorld = !firstWorld.Equals(default(World)) ? firstWorld.Name.ExtractText() : "";
                    }
                }
            }

            ImGui.Text("World *");
            var selectedDcInfo = WorldService.GetDatacenterByName(_editDatacenter);

            if (selectedDcInfo.HasValue)
            {
                var worlds = WorldService.GetWorldsForDatacenter(selectedDcInfo.Value.RowId);
                var worldNames = worlds.Select(w => w.Name.ExtractText()).ToArray();

                if (worldNames.Length > 0)
                {
                    int currentWorldIndex = Array.IndexOf(worldNames, _editWorld);
                    if (currentWorldIndex == -1)
                    {
                        _editWorld = worldNames.FirstOrDefault() ?? "";
                        currentWorldIndex = 0;
                    }

                    if (ImGui.Combo("##world", ref currentWorldIndex, worldNames, worldNames.Length))
                    {
                        if (currentWorldIndex >= 0 && currentWorldIndex < worldNames.Length)
                        {
                            _editWorld = worldNames[currentWorldIndex];
                        }
                    }
                }
                else
                {
                    ImGui.TextDisabled("No public worlds for this datacenter.");
                    _editWorld = "";
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
            ImGui.InputText("##pfcode", ref _editPfCode, 10);
            
            // Creator Role display (only for create mode)
            if (IsCreateMode)
            {
                ImGui.Text("Your Role *");
                ImGui.Text($"Selected: {_creatorRole}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Change Role"))
                {
                    ShowRoleSelectionPopup(OnCreatorRoleSelected);
                }
            }
            
            // Status (only for existing listings)
            if (!IsCreateMode)
            {
                ImGui.Text("Status");
                var statusItems = PartyStatuses.All.Values.ToArray();
                var currentStatusIndex = Array.IndexOf(statusItems, PartyStatuses.All.GetValueOrDefault(_editStatus, _editStatus));
                if (currentStatusIndex == -1) currentStatusIndex = 0;
                
                if (ImGui.Combo("##status", ref currentStatusIndex, statusItems, statusItems.Length))
                {
                    _editStatus = PartyStatuses.All.FirstOrDefault(kvp => kvp.Value == statusItems[currentStatusIndex]).Key;
                }
            }
        }
    
        private void DrawRequirementsTab()
        {
            // Experience Level
            ImGui.Text("Experience Level");
            var experienceLevels = new[] { "fresh", "some_exp", "experienced", "farm", "reclear" };
            var experienceDisplays = new[] { "Fresh/Learning", "Some Experience", "Experienced", "Farm/Clear", "Weekly Reclear" };
            var currentExpIndex = Array.IndexOf(experienceLevels, _editExperienceLevel);
            if (currentExpIndex == -1) currentExpIndex = 0;
            
            if (ImGui.Combo("##experience", ref currentExpIndex, experienceDisplays, experienceDisplays.Length))
            {
                _editExperienceLevel = experienceLevels[currentExpIndex];
            }
            
            // Item Level Requirements
            ImGui.Text("Item Level Requirements");
            ImGui.SliderInt("Min Item Level##minilvl", ref _editMinItemLevel, 0, 999);
            ImGui.SliderInt("Max Item Level##maxilvl", ref _editMaxItemLevel, 0, 999);
            
            // Required Plugins Modal
            PluginSelectorModal.Draw();

            // Progress Points (Boss Abilities)
            DrawProgressPointsSection();
            
            // Required Clears
            ImGui.Text("Required Clears");
            for (int i = _editRequiredClears.Count - 1; i >= 0; i--)
            {
                var dutyName = ContentFinderService.GetDutyDisplayName(_editRequiredClears[i]);
                ImGui.Text($"• {dutyName}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##clear{i}"))
                {
                    _editRequiredClears.RemoveAt(i);
                }
            }
            
            if (ImGui.Button("Add Required Clear"))
            {
                DutySelectorModal.Open(null, OnRequiredClearSelected);
            }
            
            // Required Plugins
            ImGui.Text("Required Plugins");
            for (int i = _editRequiredPlugins.Count - 1; i >= 0; i--)
            {
                ImGui.Text($"• {_editRequiredPlugins[i]}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##plugin{i}"))
                {
                    _editRequiredPlugins.RemoveAt(i);
                }
            }
            
            if (ImGui.Button("Add Required Plugin"))
            {
                PluginSelectorModal.OpenForName(null, OnRequiredPluginSelected);
            }
            
            // Voice Chat Required
            ImGui.Checkbox("Voice Chat Required", ref _editVoiceChatRequired);
            
            // Loot Rules
            ImGui.Text("Loot Rules");
            var lootRulesKeys = new[] { "ffa", "need_greed", "master_loot", "reserved", "discuss" };
            var lootRulesDisplays = new[] { "Free for All", "Need/Greed", "Master Loot", "Reserved Items", "Discuss Before" };
            var currentLootIndex = Array.IndexOf(lootRulesKeys, _editLootRules);
            if (currentLootIndex == -1) currentLootIndex = 1; // Default to need_greed
            
            if (ImGui.Combo("##lootrules", ref currentLootIndex, lootRulesDisplays, lootRulesDisplays.Length))
            {
                _editLootRules = lootRulesKeys[currentLootIndex];
            }
            
            // Parse Requirement
            ImGui.Text("Parse Requirement");
            var parseKeys = new[] { "none", "grey", "green", "blue", "purple", "orange", "gold" };
            var parseDisplays = new[] { "No Parse Requirement", "Grey+ (1-24th percentile)", "Green+ (25-49th percentile)", "Blue+ (50-74th percentile)", "Purple+ (75-94th percentile)", "Orange+ (95-98th percentile)", "Gold+ (99th percentile)" };
            var currentParseIndex = Array.IndexOf(parseKeys, _editParseRequirement);
            if (currentParseIndex == -1) currentParseIndex = 0;
            
            if (ImGui.Combo("##parserequirement", ref currentParseIndex, parseDisplays, parseDisplays.Length))
            {
                _editParseRequirement = parseKeys[currentParseIndex];
            }
        }
    
        private void DrawTagsAndStrategiesTab()
        {
            // Tags
            ImGui.Text("Tags");
            for (int i = _editUserTags.Count - 1; i >= 0; i--)
            {
                ImGui.Text($"• {_editUserTags[i]}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##tag{i}"))
                {
                    _editUserTags.RemoveAt(i);
                }
            }
            
            ImGui.Text("Popular Tags");
            if (ImGui.BeginListBox("##popularlisttags", new Vector2(0, 100)))
            {
                foreach (var tag in PopularTags.Select(t => t.Name))
                {
                    if (ImGui.Selectable(tag, tag == _newTag))
                    {
                        _newTag = tag;
                    }
                }
                ImGui.EndListBox();
            }

            ImGui.InputText("Add Tag", ref _newTag, 50);
            ImGui.SameLine();
            if (ImGui.Button("Add Tag") && !string.IsNullOrWhiteSpace(_newTag) && !_editUserTags.Contains(_newTag))
            {
                _editUserTags.Add(_newTag.Trim());
                _newTag = "";
            }
            
            ImGui.Separator();
            
            // Strategies
            ImGui.Text("Strategies");
            for (int i = _editUserStrategies.Count - 1; i >= 0; i--)
            {
                ImGui.Text($"• {_editUserStrategies[i]}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##strategy{i}"))
                {
                    _editUserStrategies.RemoveAt(i);
                }
            }
            
            ImGui.Text("Popular Strategies/Tags");
            if (ImGui.BeginListBox("##popularstrategies", new Vector2(0, 80)))
            {
                foreach (var tag in PopularTags.Select(t => t.Name))
                {
                    if (ImGui.Selectable(tag, tag == _newStrategy))
                    {
                        _newStrategy = tag;
                    }
                }
                ImGui.EndListBox();
            }
            
            ImGui.InputText("Add Strategy", ref _newStrategy, 100);
            ImGui.SameLine();
            if (ImGui.Button("Add Strategy") && !string.IsNullOrWhiteSpace(_newStrategy) && !_editUserStrategies.Contains(_newStrategy))
            {
                _editUserStrategies.Add(_newStrategy.Trim());
                _newStrategy = "";
            }
        }

        private void DrawActionButtons()
        {
            if (IsSaving)
            {
                ImGui.Button("Saving...");
            }
            else if (ImGui.Button(IsCreateMode ? "Create Listing" : "Save Changes"))
            {
                if (IsCreateMode)
                {
                    ShowRoleSelectionPopup(OnCreateWithRole);
                }
                else
                {
                    _ = SaveListingAsync();
                }
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                IsOpen = false;
            }
        }
        
        private void DrawDutySelector()
        {
            var currentDutyName = _selectedDuty.HasValue && !_selectedDuty.Value.Name.IsEmpty ? _selectedDuty.Value.Name.ExtractText() : "No duty selected";
            var currentDutyType = _selectedDuty.HasValue ? ContentFinderService.GetContentTypeName(_selectedDuty.Value) : "";
            var displayText = _selectedDuty.HasValue ? $"{currentDutyName} ({currentDutyType})" : "Click to select duty...";
            
            if (ImGui.Selectable(displayText, false, ImGuiSelectableFlags.None, new Vector2(0, 25)))
            {
                DutySelectorModal.Open(_selectedDuty, OnDutySelected);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Click to change duty");
            }

            if (_selectedDuty.HasValue)
            {
                ImGui.TextDisabled($"ID: {_selectedDuty.Value.RowId}, Level: {_selectedDuty.Value.ClassJobLevelRequired}, Item Level: {_selectedDuty.Value.ItemLevelRequired}");
            }

            ImGui.Separator();
        }

        private void OnDutySelected(ContentFinderCondition? selectedDuty)
        {
            _selectedDuty = selectedDuty;
            _editCfcId = selectedDuty?.RowId ?? 0;
        }
        
        private void OnRequiredClearSelected(ContentFinderCondition? selectedDuty)
        {
            if (selectedDuty.HasValue && !_editRequiredClears.Contains(selectedDuty.Value.RowId))
            {
                _editRequiredClears.Add(selectedDuty.Value.RowId);
            }
        }
        
        private void OnCreatorRoleSelected(string role)
        {
            _creatorRole = role;
        }
        
        private void OnCreateWithRole(string role)
        {
            _creatorRole = role;
            _ = SaveListingAsync();
        }
        
        private void OnRequiredPluginSelected(string? pluginName)
        {
            if (pluginName != null && !_editRequiredPlugins.Contains(pluginName))
            {
                _editRequiredPlugins.Add(pluginName);
            }
        }
        
        private async Task SaveListingAsync()
        {
            if (_editCfcId == 0)
            {
                Svc.Log.Warning("Content Finder ID is required");
                Svc.Chat.PrintError("[Party Finder Reborn] Please select a duty before creating the listing.");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(_editDescription))
            {
                Svc.Log.Warning("Description is required for party listings");
                Svc.Chat.PrintError("[Party Finder Reborn] Please add a description to your party listing. This helps other players understand what you're looking for!");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(_editDatacenter))
            {
                Svc.Log.Warning("Datacenter is required");
                Svc.Chat.PrintError("[Party Finder Reborn] Please select a datacenter before creating the listing.");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(_editWorld))
            {
                Svc.Log.Warning("World is required");
                Svc.Chat.PrintError("[Party Finder Reborn] Please select a world before creating the listing.");
                return;
            }
            
            IsSaving = true;
            
            try
            {
                var updatedListing = new PartyListing
                {
                    Id = Listing.Id,
                    CfcId = _editCfcId,
                    Description = _editDescription.Trim(),
                    Status = IsCreateMode ? "active" : _editStatus,
                    UserTags = new List<string>(_editUserTags),
                    UserStrategies = new List<string>(_editUserStrategies),
                    MaxSize = _editMaxSize,
                    MinItemLevel = _editMinItemLevel,
                    MaxItemLevel = _editMaxItemLevel,
                    RequiredClears = new List<uint>(_editRequiredClears),
                    ProgPoint = FormatProgPointsAsString(_editProgPoint),
                    ExperienceLevel = _editExperienceLevel,
                    // API expects friendly plugin names (not internal names) for proper client-side matching
                    // This allows joiners to check if they have the required plugins by name
                    RequiredPlugins = new List<string>(_editRequiredPlugins),
                    VoiceChatRequired = _editVoiceChatRequired,
                    JobRequirements = new Dictionary<string, object>(), 
                    LootRules = _editLootRules,
                    ParseRequirement = _editParseRequirement,
                    Datacenter = _editDatacenter,
                    World = _editWorld,
                    PfCode = _editPfCode.Trim(),
                    Creator = Listing.Creator,
                    CreatedAt = Listing.CreatedAt,
                    UpdatedAt = DateTime.Now
                };
                
                // Add creator role for new listings
                if (IsCreateMode)
                {
                    updatedListing.CreatorRole = _creatorRole;
                }
                
                PartyListing? result;
                if (IsCreateMode)
                {
                    result = await ApiService.CreateListingAsync(updatedListing);
                }
                else
                {
                    result = await ApiService.UpdateListingAsync(Listing.Id, updatedListing);
                }
                
                if (result != null)
                {
                    Listing = result;
                    
                    _selectedDuty = ContentFinderService.GetContentFinderCondition(Listing.CfcId);
                    
                    var dutyName = ContentFinderService.GetDutyDisplayName(Listing.CfcId);
                    WindowName = $"{dutyName}##detail_{Listing.Id}";
                    IsEditing = false;
                    var wasCreateMode = IsCreateMode;
                    IsCreateMode = false;
                    
Svc.Log.Info($"Successfully {(wasCreateMode ? "created" : "updated")} listing for duty {dutyName} (ID: {Listing.Id}, CurrentSize: {Listing.CurrentSize})");

                    // Start notification worker for newly created listings
                    if (wasCreateMode)
                    {
                        Plugin.StartJoinNotificationWorker(Listing.Id);
                    }

                    IsOpen = false;
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
        
        private void DrawProgressPointsSection()
        {
            ImGui.Text("Progress Points (Boss Abilities)");
            ImGui.TextDisabled("Select specific boss abilities that party members must have seen");
            
            try
            {
                ImGui.Text("Currently Selected:");
                if (_editProgPoint.Count > 0)
                {
                    if (ImGui.BeginChild("SelectedProgPoints", new Vector2(-1, 80), true))
                    {
                        for (int i = _editProgPoint.Count - 1; i >= 0; i--)
                        {
                            var actionId = _editProgPoint[i];
                            var actionName = ActionNameService.Get(actionId);
                            ImGui.Text($"• {actionName}");
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"✕##remove_progpoint_{i}"))
                            {
                                _editProgPoint.RemoveAt(i);
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
                
                var availableProgPoints = GetAvailableProgPointsWithCache(_editCfcId);
                
                ImGui.Text("Add Progress Points:");
                if (ProgPointsLoading)
                {
                    ImGui.TextDisabled("Loading available progress points...");
                }
                else if (availableProgPoints.Count > 0)
                {
                    ImGui.TextDisabled($"Available abilities for this duty ({availableProgPoints.Count} total)");

                    if (ImGui.BeginCombo("##select_progpoint", "Select an ability to add..."))
                    {
                        foreach (var actionId in availableProgPoints)
                        {
                            if (_editProgPoint.Contains(actionId))
                                continue;

                            var actionName = ActionNameService.Get(actionId);

                            if (ImGui.Selectable($"{actionName}##add_progpoint_{actionId}"))
                            {
                                if (!_editProgPoint.Contains(actionId))
                                {
                                    _editProgPoint.Add(actionId);
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
    }
}

