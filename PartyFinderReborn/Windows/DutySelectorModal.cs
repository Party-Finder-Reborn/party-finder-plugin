using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Lumina.Excel.Sheets;
using PartyFinderReborn.Services;

namespace PartyFinderReborn.Windows;

/// <summary>
/// A reusable modal window for selecting duties
/// </summary>
public class DutySelectorModal
{
    private readonly ContentFinderService _contentFinderService;
    private bool _isOpen;
    private bool _shouldOpen;
    private string _searchText;
    private List<ContentFinderCondition> _filteredDuties;
    private ContentFinderCondition? _selectedDuty;
    private ContentFinderCondition? _initialSelection;
    private Action<ContentFinderCondition?>? _onDutySelected;
    
    public DutySelectorModal(ContentFinderService contentFinderService)
    {
        _contentFinderService = contentFinderService;
        _searchText = "";
        _filteredDuties = new List<ContentFinderCondition>();
        _isOpen = false;
        _shouldOpen = false;
    }
    
    /// <summary>
    /// Opens the duty selector modal
    /// </summary>
    /// <param name="currentDuty">The currently selected duty (if any)</param>
    /// <param name="onDutySelected">Callback when a duty is selected or modal is closed</param>
    public void Open(ContentFinderCondition? currentDuty, Action<ContentFinderCondition?> onDutySelected)
    {
        _initialSelection = currentDuty;
        _selectedDuty = currentDuty;
        _onDutySelected = onDutySelected;
        _searchText = "";
        _filteredDuties = _contentFinderService.GetAllDuties();
        _shouldOpen = true;
        _isOpen = true;
    }
    
    /// <summary>
    /// Draws the modal window. Call this from your main Draw() method.
    /// </summary>
    public void Draw()
    {
        // Handle opening the popup
        if (_shouldOpen)
        {
            ImGui.OpenPopup("Select Duty##DutySelectorModal");
            _shouldOpen = false;
        }
        
        if (!_isOpen) return;
        
        // Set modal window size
        var viewport = ImGui.GetMainViewport();
        var modalSize = new Vector2(
            Math.Min(700, viewport.WorkSize.X * 0.8f),
            Math.Min(500, viewport.WorkSize.Y * 0.8f)
        );
        
        ImGui.SetNextWindowSize(modalSize, ImGuiCond.Always);
        ImGui.SetNextWindowPos(viewport.WorkPos + (viewport.WorkSize - modalSize) * 0.5f, ImGuiCond.Always);
        
        // Begin modal popup
        if (ImGui.BeginPopupModal("Select Duty##DutySelectorModal", ref _isOpen, 
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            DrawModalContent();
            ImGui.EndPopup();
        }
        
        // If modal was closed by clicking outside or ESC, handle it
        if (!_isOpen)
        {
            HandleModalClose(false);
        }
    }
    
    private void DrawModalContent()
    {
        // Header
        ImGui.Text("Select a Duty");
        ImGui.Separator();
        
        // Search bar
        ImGui.Text("Search:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##dutysearch", "Type to filter duties...", ref _searchText, 256))
        {
            UpdateFilteredDuties();
        }
        
        ImGui.Spacing();
        
        // Quick filter buttons  
        if (ImGui.Button("All Duties"))
        {
            _filteredDuties = _contentFinderService.GetAllDuties();
            _searchText = "";
        }
        ImGui.SameLine();
        if (ImGui.Button("High-End Only"))
        {
            _filteredDuties = _contentFinderService.GetHighEndDuties();
            _searchText = "";
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear Search"))
        {
            _searchText = "";
            _filteredDuties = _contentFinderService.GetAllDuties();
        }
        
        ImGui.Separator();
        
        // Duty list
        var listHeight = ImGui.GetContentRegionAvail().Y - 60; // Leave space for buttons
        if (ImGui.BeginListBox("##dutylist", new Vector2(-1, listHeight)))
        {
            if (_filteredDuties.Count > 0)
            {
                ImGui.TextDisabled($"Showing {Math.Min(_filteredDuties.Count, 150)} of {_filteredDuties.Count} duties");
                ImGui.Separator();
                
                // Limit to first 150 results for performance
                var displayedDuties = _filteredDuties.Take(150).ToList();
                
                foreach (var duty in displayedDuties)
                {
                    var dutyDisplayName = _contentFinderService.GetDutyDropdownDisplayName(duty);
                    var isSelected = _selectedDuty.HasValue && duty.RowId == _selectedDuty.Value.RowId;
                    
                    if (ImGui.Selectable(dutyDisplayName, isSelected))
                    {
                        _selectedDuty = duty;
                    }
                    
                    // Double-click to select and close
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _selectedDuty = duty;
                        HandleModalClose(true);
                        break;
                    }
                    
                    // Tooltip with additional info
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"ID: {duty.RowId}\nLevel: {duty.ClassJobLevelRequired}\nItem Level: {duty.ItemLevelRequired}\n\nDouble-click to select");
                    }
                }
                
                if (_filteredDuties.Count > 150)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled($"... and {_filteredDuties.Count - 150} more. Refine your search to see more.");
                }
            }
            else
            {
                ImGui.TextDisabled("No duties found matching your search.");
            }
            
            ImGui.EndListBox();
        }
        
        ImGui.Separator();
        
        // Bottom buttons
        var buttonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        
        // Submit button (only enabled if a duty is selected)
        var hasSelection = _selectedDuty.HasValue;
        if (!hasSelection)
        {
            ImGui.BeginDisabled();
        }
        
        if (ImGui.Button("Select", new Vector2(buttonWidth, 0)))
        {
            HandleModalClose(true);
        }
        
        if (!hasSelection)
        {
            ImGui.EndDisabled();
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
        {
            HandleModalClose(false);
        }
        
        // Show current selection info
        if (_selectedDuty.HasValue)
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Selected: {_contentFinderService.GetDutyDropdownDisplayName(_selectedDuty.Value)}");
            ImGui.TextDisabled($"ID: {_selectedDuty.Value.RowId}, Level: {_selectedDuty.Value.ClassJobLevelRequired}, Item Level: {_selectedDuty.Value.ItemLevelRequired}");
        }
    }
    
    private void UpdateFilteredDuties()
    {
        if (string.IsNullOrEmpty(_searchText))
        {
            _filteredDuties = _contentFinderService.GetAllDuties();
        }
        else
        {
            _filteredDuties = _contentFinderService.SearchDuties(_searchText);
        }
    }
    
    private void HandleModalClose(bool submitted)
    {
        _isOpen = false;
        
        if (submitted && _selectedDuty.HasValue)
        {
            _onDutySelected?.Invoke(_selectedDuty);
        }
        else if (!submitted)
        {
            // If cancelled, return the original selection
            _onDutySelected?.Invoke(_initialSelection);
        }
        
        // Clean up - but don't clear the filtered duties list
        // as it will be repopulated when the modal is opened again
        _onDutySelected = null;
        _selectedDuty = null;
        _initialSelection = null;
        _searchText = "";
        // Don't clear _filteredDuties here as it causes issues with repopulation
    }
    
    /// <summary>
    /// Check if the modal is currently open
    /// </summary>
    public bool IsOpen => _isOpen;
}
