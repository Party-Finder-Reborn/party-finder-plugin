using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace PartyFinderReborn.Windows;

/// <summary>
/// Interface for items that can be displayed in a GenericSelectorModal
/// </summary>
/// <typeparam name="T">The type of the selectable item</typeparam>
public interface ISelectableItem<T>
{
    /// <summary>
    /// The text to display in the list
    /// </summary>
    string DisplayText { get; }
    
    /// <summary>
    /// The text to show in tooltips when hovering over the item
    /// </summary>
    string TooltipText { get; }
    
    /// <summary>
    /// The underlying data item
    /// </summary>
    T Item { get; }
}

/// <summary>
/// A reusable generic modal window for selecting items from a searchable list
/// </summary>
/// <typeparam name="T">The type of items to select from</typeparam>
public class GenericSelectorModal<T>
{
    private bool _isOpen;
    private bool _shouldOpen;
    private string _searchText;
    private List<ISelectableItem<T>> _filteredItems;
    private List<ISelectableItem<T>> _allItems;
    private ISelectableItem<T>? _selectedItem;
    private ISelectableItem<T>? _initialSelection;
    private object? _onItemSelected;
    private string _modalTitle;
    private string _searchPlaceholder;
    
    // Configuration options
    private readonly List<FilterButton> _filterButtons;
    private readonly Func<string, List<ISelectableItem<T>>, List<ISelectableItem<T>>>? _customSearchFunc;
    private Vector2 _modalSize;
    private int _maxDisplayedItems;
    
    /// <summary>
    /// Represents a filter button that can be added to the modal
    /// </summary>
    public class FilterButton
    {
        public string Label { get; set; }
        public Func<List<ISelectableItem<T>>> GetFilteredItems { get; set; }
        
        public FilterButton(string label, Func<List<ISelectableItem<T>>> getFilteredItems)
        {
            Label = label;
            GetFilteredItems = getFilteredItems;
        }
    }
    
    /// <summary>
    /// Configuration for the GenericSelectorModal
    /// </summary>
    public class Config
    {
        public string ModalTitle { get; set; } = "Select Item";
        public string SearchPlaceholder { get; set; } = "Type to filter items...";
        public Vector2 ModalSize { get; set; } = new(700, 500);
        public int MaxDisplayedItems { get; set; } = 150;
        public List<FilterButton> FilterButtons { get; set; } = new();
        /// <summary>
        /// Optional custom search function that takes search text and returns filtered items
        /// If null, uses default text-based filtering on DisplayText and TooltipText
        /// </summary>
        public Func<string, List<ISelectableItem<T>>, List<ISelectableItem<T>>>? CustomSearchFunc { get; set; }
    }
    
    public GenericSelectorModal(Config? config = null)
    {
        var cfg = config ?? new Config();
        
        _modalTitle = cfg.ModalTitle;
        _searchPlaceholder = cfg.SearchPlaceholder;
        _modalSize = cfg.ModalSize;
        _maxDisplayedItems = cfg.MaxDisplayedItems;
        _filterButtons = cfg.FilterButtons;
        _customSearchFunc = cfg.CustomSearchFunc;
        
        _searchText = "";
        _filteredItems = new List<ISelectableItem<T>>();
        _allItems = new List<ISelectableItem<T>>();
        _isOpen = false;
        _shouldOpen = false;
    }
    
    /// <summary>
    /// Opens the selector modal
    /// </summary>
    /// <param name="items">The items to choose from</param>
    /// <param name="currentItem">The currently selected item (if any)</param>
    /// <param name="onItemSelected">Callback when an item is selected or modal is closed</param>
    public void Open(List<ISelectableItem<T>> items, T currentItem, Action<T> onItemSelected)
    {
        _allItems = items;
        _initialSelection = currentItem != null && !EqualityComparer<T>.Default.Equals(currentItem, default(T)) ? 
            items.FirstOrDefault(i => EqualityComparer<T>.Default.Equals(i.Item, currentItem)) : null;
        _selectedItem = _initialSelection;
        _onItemSelected = onItemSelected;
        _searchText = "";
        _filteredItems = items;
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
            ImGui.OpenPopup($"{_modalTitle}##GenericSelectorModal");
            _shouldOpen = false;
        }
        
        if (!_isOpen) return;
        
        // Set modal window size
        var viewport = ImGui.GetMainViewport();
        var modalSize = new Vector2(
            Math.Min(_modalSize.X, viewport.WorkSize.X * 0.8f),
            Math.Min(_modalSize.Y, viewport.WorkSize.Y * 0.8f)
        );
        
        ImGui.SetNextWindowSize(modalSize, ImGuiCond.Always);
        ImGui.SetNextWindowPos(viewport.WorkPos + (viewport.WorkSize - modalSize) * 0.5f, ImGuiCond.Always);
        
        // Begin modal popup
        if (ImGui.BeginPopupModal($"{_modalTitle}##GenericSelectorModal", ref _isOpen, 
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
        ImGui.Text(_modalTitle);
        ImGui.Separator();
        
        // Search bar
        ImGui.Text("Search:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##genericsearch", _searchPlaceholder, ref _searchText, 256))
        {
            UpdateFilteredItems();
        }
        
        ImGui.Spacing();
        
        // Filter buttons
        DrawFilterButtons();
        
        ImGui.Separator();
        
        // Item list
        var listHeight = ImGui.GetContentRegionAvail().Y - 60; // Leave space for buttons
        if (ImGui.BeginListBox("##itemlist", new Vector2(-1, listHeight)))
        {
            if (_filteredItems.Count > 0)
            {
                var displayCount = Math.Min(_filteredItems.Count, _maxDisplayedItems);
                ImGui.TextDisabled($"Showing {displayCount} of {_filteredItems.Count} items");
                ImGui.Separator();
                
                // Limit displayed items for performance
                var displayedItems = _filteredItems.Take(_maxDisplayedItems).ToList();
                
                foreach (var item in displayedItems)
                {
                    var isSelected = _selectedItem != null && EqualityComparer<T>.Default.Equals(item.Item, _selectedItem.Item);
                    
                    if (ImGui.Selectable(item.DisplayText, isSelected))
                    {
                        _selectedItem = item;
                    }
                    
                    // Double-click to select and close
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _selectedItem = item;
                        HandleModalClose(true);
                        break;
                    }
                    
                    // Tooltip with additional info
                    if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(item.TooltipText))
                    {
                        ImGui.SetTooltip($"{item.TooltipText}\n\nDouble-click to select");
                    }
                }
                
                if (_filteredItems.Count > _maxDisplayedItems)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled($"... and {_filteredItems.Count - _maxDisplayedItems} more. Refine your search to see more.");
                }
            }
            else
            {
                ImGui.TextDisabled("No items found matching your search.");
            }
            
            ImGui.EndListBox();
        }
        
        ImGui.Separator();
        
        // Bottom buttons
        var buttonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        
        // Submit button (only enabled if an item is selected)
        var hasSelection = _selectedItem != null;
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
        if (_selectedItem != null)
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Selected: {_selectedItem.DisplayText}");
        }
    }
    
    private void DrawFilterButtons()
    {
        if (_filterButtons.Count == 0) return;
        
        var buttonsPerRow = Math.Max(1, _filterButtons.Count);
        
        for (int i = 0; i < _filterButtons.Count; i++)
        {
            var button = _filterButtons[i];
            
            if (ImGui.Button(button.Label))
            {
                _filteredItems = button.GetFilteredItems();
                _searchText = "";
            }
            
            // Add spacing between buttons, but not after the last one in a row
            if (i < _filterButtons.Count - 1)
            {
                ImGui.SameLine();
            }
        }
        
        // Clear search button
        if (_filterButtons.Count > 0)
        {
            ImGui.SameLine();
        }
        
        if (ImGui.Button("Clear Search"))
        {
            _searchText = "";
            // Reset to the first filter button if available, otherwise just use current filtered items
            if (_filterButtons.Count > 0)
            {
                _filteredItems = _filterButtons[0].GetFilteredItems();
            }
        }
    }
    
    private void UpdateFilteredItems()
    {
        if (string.IsNullOrEmpty(_searchText))
        {
            // If we have filter buttons, use the first one as default, otherwise use all items
            if (_filterButtons.Count > 0)
            {
                _filteredItems = _filterButtons[0].GetFilteredItems();
            }
            else
            {
                _filteredItems = _allItems;
            }
        }
        else
        {
            // Use custom search function if provided, otherwise use default text-based search
            if (_customSearchFunc != null)
            {
                _filteredItems = _customSearchFunc(_searchText, _allItems);
            }
            else
            {
                var lowerSearch = _searchText.ToLowerInvariant();
                _filteredItems = _allItems
                    .Where(item => item.DisplayText.ToLowerInvariant().Contains(lowerSearch) ||
                                   item.TooltipText.ToLowerInvariant().Contains(lowerSearch))
                    .ToList();
            }
        }
    }
    
    private void HandleModalClose(bool submitted)
    {
        _isOpen = false;
        
        if (_onItemSelected is Action<T> callback)
        {
            if (submitted && _selectedItem != null)
            {
                // Only pass the selected item if the user submitted
                callback(_selectedItem.Item);
            }
            // If cancelled, don't call the callback at all - no changes should be made
        }
        
        // Clean up
        _onItemSelected = null;
        _selectedItem = null;
        _initialSelection = null;
        _searchText = "";
    }
    
    /// <summary>
    /// Check if the modal is currently open
    /// </summary>
    public bool IsOpen => _isOpen;
}
