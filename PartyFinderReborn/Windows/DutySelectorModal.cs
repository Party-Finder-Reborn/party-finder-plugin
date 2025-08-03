using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Lumina.Excel.Sheets;
using PartyFinderReborn.Models;
using PartyFinderReborn.Services;

namespace PartyFinderReborn.Windows;

/// <summary>
/// Wrapper for IDutyInfo to make it compatible with GenericSelectorModal
/// </summary>
public class DutySelectableItem : ISelectableItem<IDutyInfo>
{
    private readonly ContentFinderService _contentFinderService;
    
    public string DisplayText { get; }
    public string TooltipText { get; }
    public IDutyInfo Item { get; }
    
    public DutySelectableItem(IDutyInfo duty, ContentFinderService contentFinderService)
    {
        _contentFinderService = contentFinderService;
        Item = duty;
        var contentType = _contentFinderService.GetContentTypeName(duty);
        DisplayText = $"{duty.NameText} ({contentType})";
        TooltipText = $"ID: {duty.RowId}\nLevel: {duty.ClassJobLevelRequired}\nItem Level: {duty.ItemLevelRequired}";
    }
}

/// <summary>
/// A reusable modal window for selecting duties
/// </summary>
public class DutySelectorModal
{
    private readonly ContentFinderService _contentFinderService;
private readonly GenericSelectorModal<IDutyInfo> _genericModal;
    
    public DutySelectorModal(ContentFinderService contentFinderService)
    {
        _contentFinderService = contentFinderService;
        
        var config = new GenericSelectorModal<IDutyInfo>.Config
        {
            ModalTitle = "Select Duty",
            SearchPlaceholder = "Type to filter duties...",
            ModalSize = new Vector2(700, 500),
            MaxDisplayedItems = 150,
            FilterButtons = new List<GenericSelectorModal<IDutyInfo>.FilterButton>
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
        
        _genericModal = new GenericSelectorModal<IDutyInfo>(config);
    }
    
    private List<ISelectableItem<IDutyInfo>> WrapDuties(List<IDutyInfo> duties)
    {
        return duties.Select(duty => new DutySelectableItem(duty, _contentFinderService) as ISelectableItem<IDutyInfo>).ToList();
    }
    
    /// <summary>
    /// Opens the duty selector modal
    /// </summary>
    /// <param name="currentDuty">The currently selected duty (if any)</param>
    /// <param name="onDutySelected">Callback when a duty is selected or modal is closed</param>
    public void Open(ContentFinderCondition? currentDuty, Action<ContentFinderCondition?> onDutySelected)
    {
        var allDuties = WrapDuties(_contentFinderService.GetAllDuties());
        
        // Convert ContentFinderCondition to IDutyInfo for comparison
        IDutyInfo? defaultDuty = null;
        if (currentDuty.HasValue)
        {
            var cfc = currentDuty.Value;
            defaultDuty = allDuties.FirstOrDefault(item => item.Item.RowId == cfc.RowId)?.Item;
        }
        
        _genericModal.Open(allDuties, defaultDuty, (selectedDuty) => 
        {
            // Convert back to ContentFinderCondition for the original callback
            // Use GetRealDuty helper to properly handle custom duties
            if (selectedDuty != null)
            {
                var realDuty = _contentFinderService.GetRealDuty(selectedDuty.RowId);
                onDutySelected?.Invoke(realDuty);
            }
            else
            {
                onDutySelected?.Invoke(null);
            }
        });
    }
    
    /// <summary>
    /// Opens the duty selector modal with IDutyInfo
    /// </summary>
    /// <param name="currentDuty">The currently selected duty (if any)</param>
    /// <param name="onDutySelected">Callback when a duty is selected or modal is closed</param>
    public void Open(IDutyInfo? currentDuty, Action<IDutyInfo?> onDutySelected)
    {
        var allDuties = WrapDuties(_contentFinderService.GetAllDuties());
        _genericModal.Open(allDuties, currentDuty, onDutySelected);
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
