using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Lumina.Excel.Sheets;
using PartyFinderReborn.Services;

namespace PartyFinderReborn.Windows;

/// <summary>
/// Wrapper for ContentFinderCondition to make it compatible with GenericSelectorModal
/// </summary>
public class DutySelectableItem : ISelectableItem<ContentFinderCondition>
{
    private readonly ContentFinderService _contentFinderService;
    
    public string DisplayText { get; }
    public string TooltipText { get; }
    public ContentFinderCondition Item { get; }
    
    public DutySelectableItem(ContentFinderCondition duty, ContentFinderService contentFinderService)
    {
        _contentFinderService = contentFinderService;
        Item = duty;
        DisplayText = _contentFinderService.GetDutyDropdownDisplayName(duty);
        TooltipText = $"ID: {duty.RowId}\nLevel: {duty.ClassJobLevelRequired}\nItem Level: {duty.ItemLevelRequired}";
    }
}

/// <summary>
/// A reusable modal window for selecting duties
/// </summary>
public class DutySelectorModal
{
    private readonly ContentFinderService _contentFinderService;
    private readonly GenericSelectorModal<ContentFinderCondition> _genericModal;
    
    public DutySelectorModal(ContentFinderService contentFinderService)
    {
        _contentFinderService = contentFinderService;
        
        var config = new GenericSelectorModal<ContentFinderCondition>.Config
        {
            ModalTitle = "Select Duty",
            SearchPlaceholder = "Type to filter duties...",
            ModalSize = new Vector2(700, 500),
            MaxDisplayedItems = 150,
            FilterButtons = new List<GenericSelectorModal<ContentFinderCondition>.FilterButton>
            {
                new("All Duties", () => WrapDuties(_contentFinderService.GetAllDuties())),
                new("High-End Only", () => WrapDuties(_contentFinderService.GetHighEndDuties()))
            },
            CustomSearchFunc = (searchText, allItems) => 
            {
                var searchResults = _contentFinderService.SearchDuties(searchText);
                return WrapDuties(searchResults);
            }
        };
        
        _genericModal = new GenericSelectorModal<ContentFinderCondition>(config);
    }
    
    private List<ISelectableItem<ContentFinderCondition>> WrapDuties(List<ContentFinderCondition> duties)
    {
        return duties.Select(duty => new DutySelectableItem(duty, _contentFinderService) as ISelectableItem<ContentFinderCondition>).ToList();
    }
    
    /// <summary>
    /// Opens the duty selector modal
    /// </summary>
    /// <param name="currentDuty">The currently selected duty (if any)</param>
    /// <param name="onDutySelected">Callback when a duty is selected or modal is closed</param>
    public void Open(ContentFinderCondition? currentDuty, Action<ContentFinderCondition?> onDutySelected)
    {
        var allDuties = WrapDuties(_contentFinderService.GetAllDuties());
        var defaultDuty = currentDuty ?? default(ContentFinderCondition);
        _genericModal.Open(allDuties, defaultDuty, (selectedDuty) => 
        {
            // Convert back to nullable for the original callback
            onDutySelected?.Invoke(EqualityComparer<ContentFinderCondition>.Default.Equals(selectedDuty, default(ContentFinderCondition)) ? null : selectedDuty);
        });
    }
    
    /// <summary>
    /// Draws the modal window. Call this from your main Draw() method.
    /// </summary>
    public void Draw()
    {
        _genericModal.Draw();
    }
    
    /// <summary>
    /// Check if the modal is currently open
    /// </summary>
    public bool IsOpen => _genericModal.IsOpen;
}
